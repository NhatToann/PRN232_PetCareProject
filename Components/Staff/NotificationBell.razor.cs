using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using PetShop.Hubs;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop_PRN222.Components.Staff;

/// <summary>
/// Phần code-behind của <c>NotificationBell.razor</c>.
/// Tách riêng để file .razor không vượt quá 500 dòng (quy ước trong AGENTS.md §2.3).
///
/// Port nguyên văn từ <c>Projectojt/frontend/src/components/staff/NotificationBell.jsx</c> (nguồn OJT).
/// Dữ liệu lấy qua <see cref="INotificationClient"/> (self-HTTP tới <c>api/notifications/*</c>
/// và <c>api/shift-exchange/*</c>) — không inject DbContext trực tiếp vào component.
///
/// <para><b>LS-11 — realtime (SignalR):</b> component cũng kết nối hub
/// <c>/hubs/notification</c> để nhận thông báo mới ngay khi có, thay cho việc chỉ
/// tải dữ liệu theo thao tác của người dùng. Toàn bộ thao tác SignalR được bọc
/// try/catch: nếu hub lỗi thì component vẫn hoạt động bình thường qua đường HTTP
/// cũ (fallback) — UI hiển thị không thay đổi.
/// </para>
/// </summary>
public partial class NotificationBell : ComponentBase, IAsyncDisposable
{
    [Inject] private INotificationClient NotificationClient { get; set; } = default!;

    [Inject] private UserSessionService UserSession { get; set; } = default!;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Inject] private ILogger<NotificationBell> Logger { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    /// <summary>
    /// Biến thể hiển thị: "default" hoặc "topbar" (dùng trong <c>TopBar</c>).
    /// </summary>
    [Parameter] public string Variant { get; set; } = "default";

    /// <summary>
    /// Id nhân viên. Nếu không truyền thì lấy từ phiên đăng nhập hiện tại.
    /// </summary>
    [Parameter] public int? StaffId { get; set; }

    /// <summary>
    /// Sự kiện báo cho biết danh sách yêu cầu đổi ca đã thay đổi (port <c>onShiftExchangeUpdate</c>).
    /// </summary>
    [Parameter] public EventCallback OnShiftExchangeUpdate { get; set; }

    private bool panelOpen;
    private IReadOnlyList<NotificationItemDto> notifications = Array.Empty<NotificationItemDto>();
    private IReadOnlyList<PendingRequestDto> pendingRequests = Array.Empty<PendingRequestDto>();
    private IReadOnlyList<MyRequestDto> myRequests = Array.Empty<MyRequestDto>();
    private int unreadCount;
    private bool loading;
    private int? actionLoadingId;
    private string? error;

    /// <summary>Panel thông báo có đang mở hay không.</summary>
    public bool PanelOpen => panelOpen;

    /// <summary>Số thông báo chưa đọc (badge).</summary>
    public int UnreadCount => unreadCount;

    /// <summary>Đang tải dữ liệu hay không.</summary>
    public bool Loading => loading;

    /// <summary>Thông báo lỗi hiển thị trong panel.</summary>
    public string? Error => error;

    /// <summary>Danh sách thông báo.</summary>
    public IReadOnlyList<NotificationItemDto> Notifications => notifications;

    /// <summary>Các yêu cầu đổi ca đang chờ mình duyệt.</summary>
    public IReadOnlyList<PendingRequestDto> PendingRequests => pendingRequests;

    /// <summary>Các yêu cầu đổi ca do mình gửi.</summary>
    public IReadOnlyList<MyRequestDto> MyRequests => myRequests;

    /// <summary>Id yêu cầu đang được xử lý (dùng để disable nút).</summary>
    public int? ActionLoadingId => actionLoadingId;

    /// <summary>Kích thước icon chuông (22 cho topbar, 20 cho mặc định).</summary>
    public int IconSize => Variant == "topbar" ? 22 : 20;

    private int EffectiveStaffId => StaffId ?? UserSession.CurrentUser?.UserId ?? 0;

    protected override async Task OnInitializedAsync()
    {
        UserSession.Changed += HandleSessionChanged;

        try { await UserSession.LoadAsync(); }
        catch (Exception ex) { Logger.LineWarning($"[NotificationBell] UserSession.LoadAsync threw: {ex.GetType().Name}"); }

        await LoadAllAsync();

        // LS-11 — kết nối hub realtime (chạy nền, lỗi không ảnh hưởng UI).
        await StartRealtimeAsync();
    }

    // ---------------------------------------------------------------------
    // LS-11 — SignalR realtime
    // ---------------------------------------------------------------------

    /// <summary>Đường dẫn hub — phải khớp với <c>app.MapHub&lt;NotificationHub&gt;("/hubs/notification")</c>.</summary>
    private const string HubPath = "/hubs/notification";

    private HubConnection? _hubConnection;

    /// <summary>
    /// Mở kết nối SignalR tới hub thông báo và vào group <c>user:{userId}</c>.
    ///
    /// <para>
    /// Mọi lỗi đều bị nuốt kèm log: component tiếp tục hoạt động qua đường HTTP cũ
    /// (fallback). Không được để lỗi realtime làm hỏng UI hiện tại.
    /// </para>
    /// </summary>
    private async Task StartRealtimeAsync()
    {
        try
        {
            var staffId = EffectiveStaffId;
            if (staffId <= 0)
            {
                Logger.Line("[NotificationBell] Bỏ qua kết nối SignalR: chưa xác định được user id.");
                return;
            }

            if (_hubConnection is not null)
            {
                return;
            }

            var hubUrl = BuildHubUrl(staffId);

            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
                .Build();

            // Payload server gửi là 1 object DTO (RealtimeNotificationDto / ChatEnvelope)
            // ⇒ handler phải khớp đúng số tham số, nếu không client sẽ không giải mã được.
            connection.On<RealtimeNotificationDto>(
                NotificationHub.NotificationReceivedEvent,
                notification =>
                {
                    _ = HandleNotificationReceivedAsync(notification);
                });

            connection.On<ChatEnvelope>(
                NotificationHub.ChatReceivedEvent,
                chat =>
                {
                    _ = HandleChatReceivedAsync(chat);
                });

            connection.Closed += async _ =>
            {
                Logger.LineWarning("[NotificationBell] Kết nối SignalR đã đóng — sẽ thử kết nối lại ở lần render sau.");
                await Task.CompletedTask;
            };

            await connection.StartAsync();

            await connection.InvokeAsync("JoinGroup", staffId);

            _hubConnection = connection;

            Logger.Line($"[NotificationBell] Đã kết nối SignalR tới {hubUrl} và vào group user:{staffId}.");
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[NotificationBell] Không kết nối được SignalR (fallback sang HTTP): {ex.GetType().Name} — {ex.Message}");

            await DisposeHubAsync();
        }
    }

    /// <summary>
    /// Dựng URL hub trên cùng host.
    ///
    /// <para>
    /// Dùng query string <c>userId</c> vì client Blazor (circuit server-side) không
    /// gửi header session tới kết nối WebSocket; đây là chế độ LOCAL DEMO giống
    /// <c>SessionHeaderAuthenticationHandler</c> (không phải cơ chế bảo mật production).
    /// </para>
    /// </summary>
    private string BuildHubUrl(int staffId)
    {
        var baseUri = Navigation.BaseUri;
        var separator = baseUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';

        return $"{baseUri.TrimEnd('/')}{HubPath}{separator}userId={staffId}";
    }

    /// <summary>
    /// Xử lý sự kiện nhận thông báo mới — chỉ cần tải lại dữ liệu, không đổi markup.
    /// </summary>
    private async Task HandleNotificationReceivedAsync(RealtimeNotificationDto notification)
    {
        try
        {
            Logger.Line($"[NotificationBell] Nhận thông báo realtime: {notification.Title}");

            await InvokeAsync(async () =>
            {
                await LoadAllAsync();
                StateHasChanged();
            });
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[NotificationBell] Không xử lý được sự kiện thông báo realtime: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Xử lý sự kiện nhận tin nhắn chat — hiện chỉ ghi log (chưa có UI chat widget).
    /// </summary>
    private async Task HandleChatReceivedAsync(ChatEnvelope chat)
    {
        try
        {
            Logger.Line($"[NotificationBell] Nhận tin nhắn chat realtime từ user:{chat.FromUserId}.");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[NotificationBell] Không xử lý được sự kiện chat realtime: {ex.GetType().Name}");
        }
    }

    /// <summary>Huỷ kết nối SignalR (an toàn khi đã dispose).</summary>
    private async Task DisposeHubAsync()
    {
        var connection = _hubConnection;
        _hubConnection = null;

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
            Logger.LineWarning($"[NotificationBell] Lỗi khi huỷ kết nối SignalR: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Nạp song song 4 nguồn dữ liệu như nguồn (thông báo, số chưa đọc,
    /// yêu cầu chờ duyệt, yêu cầu của bạn).
    /// </summary>
    private async Task LoadAllAsync()
    {
        var staffId = EffectiveStaffId;
        if (staffId <= 0) return;

        loading = true;
        error = null;
        StateHasChanged();

        try
        {
            var notifsTask = NotificationClient.GetNotificationsAsync(staffId);
            var countTask = NotificationClient.GetUnreadCountAsync(staffId);
            var pendingTask = NotificationClient.GetPendingRequestsAsync(staffId);
            var mineTask = NotificationClient.GetMyRequestsAsync(staffId);

            await Task.WhenAll(notifsTask, countTask, pendingTask, mineTask);

            var notifsResult = await notifsTask;
            var countResult = await countTask;
            var pendingResult = await pendingTask;
            var mineResult = await mineTask;

            notifications = notifsResult.Data ?? Array.Empty<NotificationItemDto>();
            unreadCount = countResult.Data?.Count ?? 0;
            pendingRequests = pendingResult.Data ?? Array.Empty<PendingRequestDto>();
            myRequests = mineResult.Data ?? Array.Empty<MyRequestDto>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LineError(ex, $"[NotificationBell] Không tải được thông báo: {ex.Message}");
            error = ex.Message;
        }
        finally
        {
            loading = false;
            StateHasChanged();
        }
    }

    /// <summary>Mở/đóng panel thông báo.</summary>
    private void TogglePanel()
    {
        panelOpen = !panelOpen;
    }

    /// <summary>Đánh dấu toàn bộ thông báo đã đọc (port <c>handleMarkAllRead</c>).</summary>
    private async Task MarkAllReadAsync()
    {
        var staffId = EffectiveStaffId;
        if (staffId <= 0) return;

        var result = await NotificationClient.MarkAllAsReadAsync(staffId);
        if (!result.IsSuccess)
        {
            Logger.LineWarning($"[NotificationBell] Đánh dấu đã đọc thất bại: {result.Error}");
            return;
        }

        notifications = notifications.Select(n => n with { IsRead = true }).ToList();
        unreadCount = 0;
        StateHasChanged();
    }

    /// <summary>Chấp nhận yêu cầu đổi ca (port <c>handleAccept</c>).</summary>
    private async Task AcceptAsync(int requestId)
    {
        var staffId = EffectiveStaffId;
        if (staffId <= 0) return;

        actionLoadingId = requestId;
        StateHasChanged();

        try
        {
            var result = await NotificationClient.AcceptRequestAsync(requestId, staffId);
            if (!result.IsSuccess)
            {
                await JS.InvokeVoidAsync("alert", result.Error ?? "Không thể chấp nhận.");
                return;
            }

            await JS.InvokeVoidAsync("alert", "Đã chấp nhận yêu cầu đổi ca.");
            await LoadAllAsync();
            await OnShiftExchangeUpdate.InvokeAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LineError(ex, $"[NotificationBell] Không thể chấp nhận yêu cầu #{requestId}: {ex.Message}");
            await JS.InvokeVoidAsync("alert", ex.Message);
        }
        finally
        {
            actionLoadingId = null;
            StateHasChanged();
        }
    }

    /// <summary>Từ chối yêu cầu đổi ca (port <c>handleDecline</c>).</summary>
    private async Task DeclineAsync(int requestId)
    {
        var staffId = EffectiveStaffId;
        if (staffId <= 0) return;

        var confirmed = await JS.InvokeAsync<bool>("confirm", "Bạn có chắc muốn từ chối yêu cầu này?");
        if (!confirmed) return;

        actionLoadingId = requestId;
        StateHasChanged();

        try
        {
            var result = await NotificationClient.DeclineRequestAsync(requestId, staffId);
            if (!result.IsSuccess)
            {
                await JS.InvokeVoidAsync("alert", result.Error ?? "Không thể từ chối.");
                return;
            }

            await LoadAllAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LineError(ex, $"[NotificationBell] Không thể từ chối yêu cầu #{requestId}: {ex.Message}");
            await JS.InvokeVoidAsync("alert", ex.Message);
        }
        finally
        {
            actionLoadingId = null;
            StateHasChanged();
        }
    }

    /// <summary>Đánh dấu một thông báo đã đọc (port <c>handleMarkRead</c>).</summary>
    private async Task MarkReadAsync(int notificationId)
    {
        var staffId = EffectiveStaffId;
        if (staffId <= 0) return;

        var result = await NotificationClient.MarkAsReadAsync(notificationId, staffId);
        if (!result.IsSuccess)
        {
            Logger.LineWarning($"[NotificationBell] Đánh dấu đã đọc thất bại: {result.Error}");
            return;
        }

        notifications = notifications
            .Select(n => n.NotificationId == notificationId ? n with { IsRead = true } : n)
            .ToList();
        unreadCount = Math.Max(0, unreadCount - 1);
        StateHasChanged();
    }

    /// <summary>
    /// Nhãn ca cho phần "Cho:" (loại Swap) — phục vụ <c>RenderShiftInfo</c>.
    /// </summary>
    private static string ShiftGiveLabel(PendingRequestDto req) =>
        req.Type == "Swap"
            ? $"Cho: {ShiftLabel(req.FromShiftId)} · {req.FromDate}"
            : $"{ShiftLabel(req.FromShiftId)} · {req.FromDate}";

    /// <summary>Nhãn ca cho phần "Nhận:" (loại Swap).</summary>
    private static string ShiftTakeLabel(PendingRequestDto req) =>
        $"Nhận: {ShiftLabel(req.ToShiftId)} · {req.ToDate}";

    /// <summary>Nhãn ca làm việc (port <c>shiftLabel</c>).</summary>
    private static string ShiftLabel(int? shiftId) => shiftId switch
    {
        1 => "Ca sáng 08-12",
        2 => "Ca chiều 13-17",
        3 => "Ca tối 18-22",
        _ => $"Ca #{shiftId}",
    };

    /// <summary>Định dạng thời gian tương đối (port <c>fmtTime</c>).</summary>
    private static string FormatTime(DateTime? createdAt)
    {
        if (createdAt is null) return string.Empty;

        var d = createdAt.Value;
        var now = DateTime.Now;
        var diff = now - d;

        if (diff.TotalMinutes < 1) return "Vừa xong";
        if (diff.TotalHours < 1) return $"{Math.Floor(diff.TotalMinutes)} phút trước";
        if (diff.TotalDays < 1) return $"{Math.Floor(diff.TotalHours)} giờ trước";
        return d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.GetCultureInfo("vi-VN"));
    }

    private static string NormalizeStatus(string? status) =>
        (status ?? string.Empty).Trim().ToLowerInvariant();

    private void HandleSessionChanged() => InvokeAsync(async () =>
    {
        await LoadAllAsync();

        // LS-11 — khi phiên đăng nhập có user id thì mới mở được kết nối hub.
        await StartRealtimeAsync();

        StateHasChanged();
    });

    public void Dispose()
    {
        UserSession.Changed -= HandleSessionChanged;
    }

    /// <summary>
    /// Giải phóng kết nối SignalR khi component bị huỷ (Blazor Server gọi
    /// <see cref="IAsyncDisposable.DisposeAsync"/> khi circuit kết thúc).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await DisposeHubAsync();
    }
}
