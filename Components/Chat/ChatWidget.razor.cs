using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.SignalR.Client;
using PetShop.Hubs;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop_PRN222.Components.Chat;

/// <summary>
/// Code-behind của <c>ChatWidget.razor</c> — widget chat 1-1 khách ↔ nhân viên (Q21).
///
/// <para>
/// Tách riêng khỏi <c>.razor</c> theo convention của dự án (file <c>.razor</c>
/// không vượt quá 300 dòng).
/// </para>
///
/// <para>
/// <b>Kiến trúc:</b> Component → <see cref="IChatClient"/> (self-HTTP tới
/// <c>api/chat/*</c>) → Controller → <see cref="IChatService"/> →
/// <see cref="IChatRepository"/> → DbContext. Component <b>không</b> inject
/// <c>ShopPetDatabaseContext</c> (3-layer).
/// </para>
///
/// <para>
/// <b>Realtime:</b> kết nối SignalR tới <c>/hubs/notification</c> (dùng chung
/// <see cref="NotificationHub"/>), join group <c>user:{userId}</c> để nhận
/// event <c>ChatReceived</c>. Mọi lỗi realtime đều bị nuốt kèm log ⇒ nếu
/// SignalR không kết nối được, người dùng vẫn gửi tin được qua HTTP
/// (fallback) và UI không bị hỏng.
/// </para>
/// </summary>
public partial class ChatWidget : ComponentBase, IAsyncDisposable
{
    [Inject] private IChatClient ChatClient { get; set; } = default!;

    [Inject] private UserSessionService UserSession { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Inject] private ILogger<ChatWidget> Logger { get; set; } = default!;

    /// <summary>Đường dẫn hub — phải khớp với <c>app.MapHub&lt;NotificationHub&gt;("/hubs/notification")</c>.</summary>
    private const string HubPath = "/hubs/notification";

    private bool isOpen;
    private bool loading;
    private bool sending;
    private bool realtimeConnected;
    private int unreadCount;
    private string? error;
    private string draft = string.Empty;

    private IReadOnlyList<ChatPeerDto> peers = Array.Empty<ChatPeerDto>();
    private IReadOnlyList<ChatMessageDto> messages = Array.Empty<ChatMessageDto>();
    private int selectedPeerId;
    private HubConnection? _hubConnection;

    /// <summary>Chỉ hiển thị khi người dùng đã đăng nhập.</summary>
    private bool IsVisible => UserSession.CurrentUser is not null && UserSession.CurrentUser.UserId > 0;

    /// <summary>Panel chat đang mở hay không.</summary>
    private bool IsOpen => isOpen;

    /// <summary>Đang tải dữ liệu hay không.</summary>
    private bool Loading => loading;

    /// <summary>Đang gửi tin hay không.</summary>
    private bool Sending => sending;

    /// <summary>Kết nối realtime có thành công hay không (để hiển thị trạng thái).</summary>
    private bool RealtimeConnected => realtimeConnected;

    /// <summary>Số tin nhắn chưa xem (badge trên nút tròn).</summary>
    private int UnreadCount => unreadCount;

    /// <summary>Thông báo lỗi hiển thị trong khung chat.</summary>
    private string? Error => error;

    /// <summary>Danh sách đối tác có thể chat.</summary>
    private IReadOnlyList<ChatPeerDto> Peers => peers;

    /// <summary>Lịch sử tin nhắn với đối tác đang chọn.</summary>
    private IReadOnlyList<ChatMessageDto> Messages => messages;

    /// <summary>Id đối tác đang chọn.</summary>
    private int SelectedPeerId => selectedPeerId;

    /// <summary>Đối tác đang chọn (null nếu chưa chọn).</summary>
    private ChatPeerDto? SelectedPeer => peers.FirstOrDefault(p => p.PeerId == selectedPeerId);

    /// <summary>Nội dung ô nhập hiện tại.</summary>
    private string Draft => draft;

    /// <summary>Placeholder của ô nhập — đổi theo vai trò đang đăng nhập.</summary>
    private string InputPlaceholder =>
        IsStaffSide ? "Nhắn tin cho khách hàng…" : "Nhắn tin cho nhân viên…";

    /// <summary>Người đang đăng nhập có phải nhân viên (staff / admin / doctor) không.</summary>
    private bool IsStaffSide
    {
        get
        {
            var role = (UserSession.CurrentUser?.Role ?? string.Empty).Trim().ToLowerInvariant();
            return role is "staff" or "admin" or "doctor";
        }
    }

    /// <summary>Id người dùng hiện tại.</summary>
    private int CurrentUserId => UserSession.CurrentUser?.UserId ?? 0;

    protected override async Task OnInitializedAsync()
    {
        UserSession.Changed += HandleSessionChanged;

        try { await UserSession.LoadAsync(); }
        catch (Exception ex) { Logger.LineWarning($"[ChatWidget] UserSession.LoadAsync threw: {ex.GetType().Name}"); }

        if (IsVisible)
        {
            await LoadPeersAsync();
            await StartRealtimeAsync();
        }
    }

    // ---------------------------------------------------------------------
    // Realtime (SignalR) — LS-11
    // ---------------------------------------------------------------------

    /// <summary>
    /// Mở kết nối SignalR tới hub thông báo và vào group <c>user:{userId}</c>.
    /// Lỗi bị nuốt kèm log: nếu không kết nối được thì widget vẫn dùng được
    /// qua đường HTTP (fallback).
    /// </summary>
    private async Task StartRealtimeAsync()
    {
        var userId = CurrentUserId;
        if (userId <= 0 || _hubConnection is not null)
        {
            return;
        }

        try
        {
            var hubUrl = BuildHubUrl(userId);

            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
                .Build();

            // Payload server gửi là 1 object DTO ⇒ handler phải khớp đúng số tham số.
            connection.On<ChatEnvelope>(
                NotificationHub.ChatReceivedEvent,
                chat =>
                {
                    _ = HandleChatReceivedAsync(chat);
                });

            connection.Closed += async _ =>
            {
                realtimeConnected = false;
                Logger.LineWarning("[ChatWidget] Kết nối SignalR đã đóng — realtime tạm thời gián đoạn (HTTP fallback vẫn chạy).");
                await InvokeAsync(StateHasChanged);
            };

            await connection.StartAsync();
            await connection.InvokeAsync("JoinGroup", userId);

            _hubConnection = connection;
            realtimeConnected = true;

            Logger.Line($"[ChatWidget] Đã kết nối SignalR tới {hubUrl} và vào group user:{userId}.");
        }
        catch (Exception ex)
        {
            realtimeConnected = false;
            Logger.LineWarning($"[ChatWidget] Không kết nối được SignalR (fallback sang HTTP): {ex.GetType().Name} — {ex.Message}");
            await DisposeHubAsync();
        }
    }

    /// <summary>Dựng URL hub trên cùng host (dùng query string <c>userId</c> như LOCAL DEMO).</summary>
    private string BuildHubUrl(int userId)
    {
        var baseUri = Navigation.BaseUri;
        var separator = baseUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';

        return $"{baseUri.TrimEnd('/')}{HubPath}{separator}userId={userId}";
    }

    /// <summary>
    /// Xử lý tin nhắn realtime đến: nếu đang chat với người gửi thì thêm vào
    /// khung chat; ngược lại tăng badge chưa đọc.
    /// </summary>
    private async Task HandleChatReceivedAsync(ChatEnvelope chat)
    {
        try
        {
            await InvokeAsync(async () =>
            {
                var senderId = chat.FromUserId;

                if (selectedPeerId > 0 && senderId == selectedPeerId)
                {
                    // Đang mở đúng cuộc hội thoại ⇒ thêm tin vào danh sách.
                    if (!messages.Any(m => m.Id == chat.MessageId))
                    {
                        var dto = new ChatMessageDto(
                            chat.MessageId,
                            senderId,
                            CurrentUserId,
                            chat.Message,
                            chat.SentAt,
                            IsMine: false);

                        messages = messages.Append(dto).ToList();
                    }
                }
                else
                {
                    unreadCount++;
                }

                await Task.CompletedTask;
                StateHasChanged();
            });
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[ChatWidget] Không xử lý được sự kiện chat realtime: {ex.GetType().Name}");
        }
    }

    /// <summary>Huỷ kết nối SignalR (an toàn khi đã dispose).</summary>
    private async Task DisposeHubAsync()
    {
        var connection = _hubConnection;
        _hubConnection = null;
        realtimeConnected = false;

        if (connection is null)
        {
            return;
        }

        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[ChatWidget] Lỗi khi huỷ kết nối SignalR: {ex.GetType().Name}");
        }
    }

    // ---------------------------------------------------------------------
    // Dữ liệu
    // ---------------------------------------------------------------------

    /// <summary>Tải danh sách đối tác có thể chat.</summary>
    private async Task LoadPeersAsync()
    {
        loading = true;
        error = null;
        StateHasChanged();

        try
        {
            var result = await ChatClient.GetPeersAsync();
            if (!result.IsSuccess)
            {
                error = "Không tải được danh sách đối tác.";
                Logger.LineWarning($"[ChatWidget] GetPeersAsync thất bại: {result.Error}");
                return;
            }

            peers = result.Data ?? Array.Empty<ChatPeerDto>();

            // Tự chọn đối tác đầu tiên nếu chưa chọn.
            if (selectedPeerId == 0 && peers.Count > 0)
            {
                selectedPeerId = peers[0].PeerId;
                await LoadHistoryAsync();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LineError(ex, $"[ChatWidget] Không tải được danh sách đối tác: {ex.Message}");
            error = "Không tải được danh sách đối tác.";
        }
        finally
        {
            loading = false;
            StateHasChanged();
        }
    }

    /// <summary>Tải lịch sử chat với đối tác đang chọn.</summary>
    private async Task LoadHistoryAsync()
    {
        if (selectedPeerId <= 0)
        {
            messages = Array.Empty<ChatMessageDto>();
            return;
        }

        loading = true;
        error = null;
        StateHasChanged();

        try
        {
            var peerKind = SelectedPeer?.PeerKind ?? (IsStaffSide ? ChatRepositoryKind.Customer : ChatRepositoryKind.Staff);
            var result = await ChatClient.GetHistoryAsync(selectedPeerId, peerKind);

            if (!result.IsSuccess)
            {
                error = "Không tải được lịch sử chat.";
                Logger.LineWarning($"[ChatWidget] GetHistoryAsync thất bại: {result.Error}");
                return;
            }

            messages = result.Data ?? Array.Empty<ChatMessageDto>();
            unreadCount = 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LineError(ex, $"[ChatWidget] Không tải được lịch sử chat: {ex.Message}");
            error = "Không tải được lịch sử chat.";
        }
        finally
        {
            loading = false;
            StateHasChanged();
        }
    }

    // ---------------------------------------------------------------------
    // Tương tác
    // ---------------------------------------------------------------------

    /// <summary>Mở / đóng panel chat.</summary>
    private async Task TogglePanel()
    {
        isOpen = !isOpen;

        if (isOpen)
        {
            if (peers.Count == 0)
            {
                await LoadPeersAsync();
            }
            else if (messages.Count == 0)
            {
                await LoadHistoryAsync();
            }

            unreadCount = 0;
        }

        StateHasChanged();
    }

    /// <summary>Đổi đối tác đang chọn ⇒ tải lại lịch sử chat.</summary>
    private async Task OnPeerChangedAsync(ChangeEventArgs args)
    {
        var raw = args.Value?.ToString();
        if (!int.TryParse(raw, out var peerId) || peerId <= 0)
        {
            selectedPeerId = 0;
            messages = Array.Empty<ChatMessageDto>();
            StateHasChanged();
            return;
        }

        selectedPeerId = peerId;
        await LoadHistoryAsync();
    }

    /// <summary>Cập nhật nội dung ô nhập.</summary>
    private void OnInputChanged(ChangeEventArgs args)
    {
        draft = args.Value?.ToString() ?? string.Empty;
    }

    /// <summary>Gửi tin khi nhấn Enter (Shift+Enter không gửi).</summary>
    private async Task OnKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter" && !args.ShiftKey)
        {
            await SendAsync();
        }
    }

    /// <summary>
    /// Gửi tin nhắn: gọi <c>POST api/chat/send</c> (ghi DB + đẩy realtime).
    ///
    /// <para>
    /// Nếu SignalR đang kết nối thì vẫn gửi qua HTTP để bảo đảm tin được
    /// lưu vào DB và trả về DTO chuẩn — realtime chỉ là kênh nhận.
    /// </para>
    /// </summary>
    private async Task SendAsync()
    {
        var text = (draft ?? string.Empty).Trim();

        if (text.Length == 0 || selectedPeerId <= 0 || sending)
        {
            return;
        }

        var peer = SelectedPeer;
        if (peer is null)
        {
            return;
        }

        sending = true;
        error = null;
        StateHasChanged();

        try
        {
            var result = await ChatClient.SendMessageAsync(new ChatSendRequestDto(
                selectedPeerId,
                text,
                peer.PeerKind));

            if (!result.IsSuccess)
            {
                error = "Không gửi được tin nhắn.";
                Logger.LineWarning($"[ChatWidget] SendMessageAsync thất bại: {result.Error}");
                return;
            }

            var sent = result.Data?.Message;
            if (sent is not null && !messages.Any(m => m.Id == sent.Id))
            {
                messages = messages.Append(sent).ToList();
            }

            draft = string.Empty;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LineError(ex, $"[ChatWidget] Không gửi được tin nhắn: {ex.Message}");
            error = "Không gửi được tin nhắn.";
        }
        finally
        {
            sending = false;
            StateHasChanged();
        }
    }

    /// <summary>Định dạng thời gian tương đối cho tin nhắn.</summary>
    private static string FormatTime(DateTime createdAt)
    {
        if (createdAt == default)
        {
            return string.Empty;
        }

        var diff = DateTime.Now - createdAt;

        if (diff.TotalMinutes < 1) return "Vừa xong";
        if (diff.TotalHours < 1) return $"{Math.Floor(diff.TotalMinutes)} phút";
        if (diff.TotalDays < 1) return $"{Math.Floor(diff.TotalHours)} giờ";
        return createdAt.ToString("dd/MM HH:mm");
    }

    /// <summary>Khi phiên đăng nhập thay đổi ⇒ nạp lại dữ liệu + kết nối lại realtime.</summary>
    private void HandleSessionChanged() => InvokeAsync(async () =>
    {
        if (!IsVisible)
        {
            await DisposeHubAsync();
            peers = Array.Empty<ChatPeerDto>();
            messages = Array.Empty<ChatMessageDto>();
            selectedPeerId = 0;
            StateHasChanged();
            return;
        }

        await LoadPeersAsync();
        await StartRealtimeAsync();
        StateHasChanged();
    });

    public void Dispose()
    {
        UserSession.Changed -= HandleSessionChanged;
    }

    /// <summary>Giải phóng kết nối SignalR khi component bị huỷ.</summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await DisposeHubAsync();
    }
}
