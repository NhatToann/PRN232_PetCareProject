using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Repositories;
using PetShop.Services;

namespace PetShop_PRN222.Components.Chat;

/// <summary>
/// Code-behind của <c>ChatWidget.razor</c> — hộp thoại HỢP NHẤT (AI + nhân viên).
///
/// <para>
/// <b>REDESIGN 12/11/2026:</b> trước đây có 2 widget riêng biệt
/// (<c>ChatWidget</c> + <c>AiChatWidget</c>) với thanh tab chia "Tin nhắn" /
/// "Trợ lý AI". Nay gộp thành MỘT panel, MỘT luồng tin nhắn; AI và nhân viên
/// cùng xuất hiện, phân biệt bằng avatar + nhãn tên. Chọn đích đến nằm ở
/// chip trong ô nhập (<see cref="ChatPopupState.Target"/>).
/// </para>
///
/// <para>
/// <b>Kiến trúc:</b> Component → <see cref="IChatClient"/> (self-HTTP tới
/// <c>api/chat/*</c>) cho tin nhắn nhân viên + <see cref="IChatbotClient"/>
/// (self-HTTP tới <c>api/chatbot/*</c>) cho trợ lý AI. Component <b>không</b>
/// inject <c>ShopPetDatabaseContext</c> (3-layer).
/// </para>
///
/// <para>
/// <b>TỐI ƯU RAM / ĐỘ TRỄ:</b> không gọi API lúc khởi tạo; chỉ tải dữ liệu khi
/// user MỞ panel; lịch sử AI giới hạn 40 lượt trong RAM của circuit.
/// </para>
/// </summary>
public partial class ChatWidget : ComponentBase, IAsyncDisposable
{
    /// <summary>Số lượt hội thoại AI tối đa giữ trong RAM của circuit.</summary>
    private const int MaxAiHistoryItems = 40;

    /// <summary>Số tin nhắn nhân viên tối đa giữ trong RAM của circuit.</summary>
    private const int MaxStaffMessages = 100;

    [Inject] private IChatClient ChatClient { get; set; } = default!;

    [Inject] private IChatbotClient ChatbotClient { get; set; } = default!;

    [Inject] private UserSessionService UserSession { get; set; } = default!;

    [Inject] private ChatPopupState Popup { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Inject] private RealtimeHubService Realtime { get; set; } = default!;

    [Inject] private ILogger<ChatWidget> Logger { get; set; } = default!;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Id của khung chứa danh sách tin nhắn (dùng cho JS interop).</summary>
    private const string MessagesBoxId = "chatMessagesBox";

    /// <summary>Lượt hội thoại AI (RAM của circuit).</summary>
    private readonly List<ChatTurn> aiMessages = new();

    /// <summary>Tin nhắn nhân viên (RAM của circuit).</summary>
    private readonly List<ChatTurn> staffMessages = new();

    private bool loading;
    private bool sending;
    private bool realtimeConnected;
    private bool aiHealthChecked;
    private int unreadCount;
    private string? error;
    private string draft = string.Empty;
    private string aiStatusText = "Trợ lý AI";

    private IReadOnlyList<ChatPeerDto> peers = Array.Empty<ChatPeerDto>();

    /// <summary>
    /// Khoá của đối tác đang chọn — dạng composite <c>"{PeerKind}:{PeerId}"</c>.
    ///
    /// <para>
    /// ⚠️ BẮT BUỘC dùng composite key (sửa bug 12/11/2026): các bảng
    /// <c>Staff</c> / <c>Admin</c> / <c>Doctor</c> có <b>id TRÙNG NHAU</b>
    /// (đã kiểm tra DB: <c>staff_id</c> và <c>admin_id</c> đều có 1, 2, 6, 7, 8;
    /// <c>doctor_id</c> có 1–6). Nếu chỉ so sánh <c>PeerId</c> thì bấm avatar
    /// của "Nguyễn Văn A" (admin, id=1) sẽ bị coi là **đã chọn** "Nguyễn Văn A"
    /// (staff, id=1) ⇒ hộp thoại KHÔNG chuyển sang người vừa bấm.
    /// </para>
    /// </summary>
    private string selectedPeerKey = string.Empty;

    /// <summary>
    /// Id khách hàng đang mở hộp thư (chỉ dùng khi bên NHÂN VIÊN).
    ///
    /// <para>
    /// <b>HỘP THƯ CHUNG (13/11/2026):</b> nhân viên chọn 1 KHÁCH trong thanh
    /// icon ⇒ mở hộp thư của khách đó (mọi nhân viên đều thấy, không riêng ai).
    /// </para>
    /// </summary>
    private int selectedCustomerId;

    /// <summary>Chỉ hiển thị khi người dùng đã đăng nhập.</summary>
    private bool IsVisible => UserSession.CurrentUser is not null && UserSession.CurrentUser.UserId > 0;

    /// <summary>Cửa sổ chat đang mở hay không.</summary>
    private bool IsOpen => Popup.IsOpen;

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

    /// <summary>Danh sách đối tác có thể chat (khách hàng có hộp thư — bên nhân viên).</summary>
    private IReadOnlyList<ChatPeerDto> Peers => peers;

    /// <summary>Đối tác đang chọn (null nếu chưa chọn).</summary>
    private ChatPeerDto? SelectedPeer => peers.FirstOrDefault(p => PeerKey(p) == selectedPeerKey);

    /// <summary>
    /// Khoá composite của một đối tác — <c>"{PeerKind}:{PeerId}"</c>.
    ///
    /// <para>
    /// Vì id các bảng người dùng TRÙNG NHAU (xem <see cref="selectedPeerKey"/>)
    /// nên không thể dùng mỗi <c>PeerId</c> để định danh đối tác.
    /// </para>
    /// </summary>
    private static string PeerKey(ChatPeerDto peer) => $"{peer.PeerKind}:{peer.PeerId}";

    /// <summary>Nội dung ô nhập hiện tại.</summary>
    private string Draft => draft;

    /// <summary>Trạng thái AI hiển thị trên header.</summary>
    private string AiStatusText => aiStatusText;

    /// <summary>
    /// Tiêu đề header — đổi theo người đang chat để luôn biết mình đang
    /// nói chuyện với ai (AI hay hộp thư nhân viên / khách nào).
    /// </summary>
    private string HeaderTitle
    {
        get
        {
            if (!TargetIsStaff)
            {
                return "Trợ lý PetCare";
            }

            // Nhân viên: đang mở hộp thư của 1 khách.
            if (IsStaffSide)
            {
                return SelectedPeer?.DisplayName ?? "Hộp thư khách hàng";
            }

            // Khách: hộp thư chung của nhân viên.
            return "Nhân viên Pet Toy Shop";
        }
    }

    /// <summary>
    /// Toàn bộ lượt hội thoại hiển thị trên UI.
    ///
    /// <para>
    /// ⚠️ CHỈ hiển thị lượt của đích đến đang chọn (sửa lỗi 13/11/2026):
    /// trước đây gộp CHUNG <c>aiMessages</c> + <c>staffMessages</c> ⇒ khi bấm
    /// avatar nhân viên thì khung chat VẪN HIỆN NỘI DUNG CHATBOT (vì lượt AI
    /// có <c>Sequence</c> nhỏ hơn nên nằm lên trên) ⇒ người dùng thấy như
    /// chat không chuyển. Nay chỉ hiện lượt của đích đến đang chọn.
    /// </para>
    /// </summary>
    private IReadOnlyList<ChatTurn> Messages =>
        TargetIsStaff
            ? staffMessages.OrderBy(m => m.Sequence).ToList()
            : aiMessages.OrderBy(m => m.Sequence).ToList();

    /// <summary>Có thể chọn nhân viên làm đích đến không (cần đăng nhập).</summary>
    private bool CanChatWithStaff => IsVisible;

    /// <summary>Đích đến hiện tại có phải nhân viên không.</summary>
    private bool TargetIsStaff => Popup.Target == ChatPopupState.StaffTarget;

    /// <summary>Placeholder của ô nhập — đổi theo đích đến.</summary>
    private string InputPlaceholder =>
        TargetIsStaff
            ? "Nhắn tin cho nhân viên…"
            : "Nhập câu hỏi của bạn…";

    /// <summary>
    /// Điều kiện cho phép gửi tin.
    ///
    /// <para>
    /// <b>HỘP THƯ CHUNG (13/11/2026):</b> khách gửi vào hộp thư chung (không
    /// cần chọn người) ⇒ luôn gửi được. Nhân viên phải chọn 1 KHÁCH trước.
    /// </para>
    /// </summary>
    private bool CanSend => !TargetIsStaff || !IsStaffSide || SelectedPeer is not null;

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

    /// <summary>Gợi ý câu hỏi mẫu (chỉ hiện khi chưa có lượt nào).</summary>
    private static readonly string[] Suggestions =
    {
        "Cửa hàng có những dịch vụ gì?",
        "Giá thức ăn cho chó khoảng bao nhiêu?",
        "Tôi muốn đặt lịch chăm sóc",
        "Bạn là ai?"
    };

    protected override async Task OnInitializedAsync()
    {
        UserSession.Changed += HandleSessionChanged;
        Popup.Changed += HandlePopupChanged;
        Realtime.ChatReceived += HandleChatReceived;

        try { await UserSession.LoadAsync(); }
        catch (Exception ex) { Logger.LineWarning($"[ChatWidget] UserSession.LoadAsync threw: {ex.GetType().Name}"); }

        if (IsVisible)
        {
            // KHÔNG tải danh sách đối tác / lịch sử chat ngay khi khởi tạo.
            //
            // TỐI ƯU ĐỘ TRỄ + RAM: ChatWidget nằm ở MỌI trang. Gọi 2 self-HTTP
            // endpoint (peers + history) ngay khi init ⇒ mỗi lần điều hướng là
            // 2 round-trip (TLS + MVC + EF query), dù user có mở widget hay không.
            //
            // Nay: chỉ kết nối SignalR (1 WebSocket, dùng chung) lúc init;
            // dữ liệu chat chỉ tải khi user MỞ panel (xem TogglePanel).
            await StartRealtimeAsync();
        }
    }

    // ---------------------------------------------------------------------
    // Realtime (SignalR) — dùng chung qua RealtimeHubService
    // ---------------------------------------------------------------------

    /// <summary>
    /// Bảo đảm kết nối SignalR (dùng chung với NotificationBell).
    /// </summary>
    private async Task StartRealtimeAsync()
    {
        var userId = CurrentUserId;
        if (userId <= 0)
        {
            return;
        }

        try
        {
            await Realtime.EnsureConnectedAsync(BuildHubUrl(userId), userId);
            realtimeConnected = Realtime.IsConnected;
        }
        catch (Exception ex)
        {
            realtimeConnected = false;
            Logger.LineWarning($"[ChatWidget] Không kết nối được SignalR (fallback sang HTTP): {ex.GetType().Name}");
        }
    }

    /// <summary>Dựng URL hub trên cùng host (dùng query string <c>userId</c> như LOCAL DEMO).</summary>
    private string BuildHubUrl(int userId)
    {
        var baseUri = Navigation.BaseUri;
        var separator = baseUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';

        return $"{baseUri.TrimEnd('/')}{RealtimeHubService.HubPath}{separator}userId={userId}";
    }

    /// <summary>
    /// Xử lý tin nhắn realtime đến: nếu đang chat với người gửi thì thêm vào
    /// khung chat; ngược lại tăng badge chưa đọc.
    ///
    /// <para>
    /// ⚠️ So khớp theo composite key (id + kind) — sửa bug 12/11/2026: id các
    /// bảng người dùng trùng nhau nên chỉ so <c>FromUserId</c> sẽ nhận nhầm
    /// tin của người khác (ví dụ staff id=1 vs admin id=1).
    /// </para>
    /// </summary>
    private void HandleChatReceived(ChatEnvelope chat)
    {
        try
        {
            // ⚠️ Chặn ECHO của chính mình (sửa lỗi gửi trùng 2 lần 13/11/2026).
            //
            // VÌ SAO:
            //   Server đẩy realtime theo group `user:{id}` — mà id các bảng
            //   người dùng TRÙNG NHAU (customer 1 = staff 1 = admin 1 = doctor 1).
            //   ⇒ khi khách id=1 nhắn cho staff id=1, tin quay lại chính circuit
            //   của người gửi ⇒ hiện thêm 1 bubble nữa ⇒ "1 tin gửi 2 lần".
            //
            //   So khớp BẰNG CẢ id + loại người gửi ⇒ không bỏ nhầm tin của đối
            //   tác có trùng id (ví dụ staff id=1 nhắn cho khách id=1).
            var ownKind = IChatService.NormalizeKind(CurrentUserRole);
            var isOwnEcho = chat.FromUserId == CurrentUserId
                && (string.IsNullOrWhiteSpace(chat.FromKind)
                    || string.Equals(chat.FromKind, ownKind, StringComparison.OrdinalIgnoreCase));

            if (isOwnEcho)
            {
                return;
            }

            // ⚠️ HỘP THƬ CHUNG (13/11/2026): tin nhắn không còn thuộc về "1 đối
            // tác" mà thuộc về "1 hộp thư của khách X" ⇒ lọc theo KHÁCH.
            var customerId = chat.ToCustomerId > 0 ? chat.ToCustomerId : chat.UserId;

            // Bên KHÁCH: hộp thư nhân viên của chính mình ⇒ mọi tin từ nhân viên
            // đều thuộc hộp thư này.
            // Bên NHÂN VIÊN: chỉ hiện tin của khách đang mở.
            var isCurrentThread = IsStaffSide
                ? customerId > 0 && customerId == selectedCustomerId
                : customerId > 0 && customerId == CurrentUserId;

            if (isCurrentThread)
            {
                // Đang mở đúng hộp thư ⇒ thêm tin vào danh sách.
                if (!staffMessages.Any(m => m.ExternalId == chat.MessageId))
                {
                    var senderName = string.IsNullOrWhiteSpace(chat.SenderName)
                        ? "Nhân viên"
                        : chat.SenderName;

                    staffMessages.Add(new ChatTurn(
                        chat.Message,
                        ChatAuthor.Staff,
                        senderName,
                        string.IsNullOrWhiteSpace(chat.SenderRole) ? "staff" : chat.SenderRole,
                        null,
                        chat.SentAt,
                        Sequence: NextSequence(),
                        ExternalId: chat.MessageId));

                    TrimStaffMessages();
                }
            }
            else
            {
                unreadCount++;
            }

            InvokeAsync(StateHasChanged);
            _ = InvokeAsync(ScrollToBottomAsync);
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[ChatWidget] Không xử lý được sự kiện chat realtime: {ex.GetType().Name}");
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

            // ⚠️ HỘP THƯ CHUNG (13/11/2026):
            //   Bên KHÁCH: danh sách rỗng (chỉ chọn "AI" hoặc "Nhân viên").
            //   Bên NHÂN VIÊN: danh sách KHÁCH có hộp thư ⇒ tự chọn khách đầu
            //   tiên (mới nhất) và tải lịch sử hộp thư của khách đó.
            if (IsStaffSide && selectedPeerKey.Length == 0 && peers.Count > 0)
            {
                selectedPeerKey = PeerKey(peers[0]);
                selectedCustomerId = peers[0].PeerId;
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
        // ⚠️ HỘP THƬ CHUNG (13/11/2026):
        //   Bên KHÁCH: lịch sử hộp thư NHÂN VIÊN của chính mình ⇒ gọi API với
        //   peerKind = "pool" (không cần chọn người nhận).
        //   Bên NHÂN VIÊN: lịch sử hộp thư của KHÁCH đang chọn.
        var peer = SelectedPeer;

        var peerId = peer?.PeerId ?? 0;
        var peerKind = IsStaffSide ? ChatRepositoryKind.Customer : ChatRepositoryKind.Pool;

        if (!IsStaffSide)
        {
            // Khách: dùng chính id của mình làm khoá hộp thư.
            peerId = CurrentUserId;
        }
        else if (peer is null)
        {
            staffMessages.Clear();
            StateHasChanged();
            return;
        }

        loading = true;
        error = null;
        StateHasChanged();

        try
        {
            var result = await ChatClient.GetHistoryAsync(peerId, peerKind);

            if (!result.IsSuccess)
            {
                error = "Không tải được lịch sử chat.";
                Logger.LineWarning($"[ChatWidget] GetHistoryAsync thất bại: {result.Error}");
                return;
            }

            var history = result.Data ?? Array.Empty<ChatMessageDto>();

            staffMessages.Clear();

            foreach (var message in history)
            {
                var isMine = message.IsMine;

                // Tên + vai trò người gửi (hộp thư chung: nhiều người gửi).
                var senderName = string.IsNullOrWhiteSpace(message.SenderName)
                    ? (isMine ? CurrentUserDisplayName : "Nhân viên")
                    : message.SenderName;

                var senderRole = string.IsNullOrWhiteSpace(message.SenderRole)
                    ? (isMine ? CurrentUserRole : "staff")
                    : message.SenderRole;

                staffMessages.Add(new ChatTurn(
                    message.Message,
                    isMine ? ChatAuthor.User : ChatAuthor.Staff,
                    senderName,
                    senderRole,
                    isMine ? CurrentUserAvatarUrl : null,
                    message.CreatedAt,
                    Sequence: NextSequence(),
                    ExternalId: message.Id));
            }

            TrimStaffMessages();
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

    /// <summary>Mở / đóng panel chat — tải dữ liệu lazily khi mở.</summary>
    private async Task TogglePanel()
    {
        Popup.Toggle();

        if (IsOpen)
        {
            if (!aiHealthChecked)
            {
                aiHealthChecked = true;
                await CheckHealthAsync();
            }

            if (IsVisible && peers.Count == 0 && IsStaffSide)
            {
                await LoadPeersAsync();
            }
            else if (IsVisible && staffMessages.Count == 0 && (TargetIsStaff || IsStaffSide))
            {
                await LoadHistoryAsync();
            }

            unreadCount = 0;
            await ScrollToBottomAsync();
        }

        StateHasChanged();
    }

    /// <summary>Chuyển đích đến sang trợ lý AI.</summary>
    private void SelectAiTarget()
    {
        Popup.SelectTarget(ChatPopupState.AiTarget);
        StateHasChanged();
    }

    /// <summary>
    /// Chuyển đích đến sang nhân viên.
    ///
    /// <para>
    /// <b>HỘP THƯ CHUNG (13/11/2026) — 2 trường hợp:</b>
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Khách</b>: bấm nút "Nhân viên" ⇒ mở hộp thư chung của nhân viên
    ///     (không chọn người nhận) ⇒ nạp lịch sử hộp thư của chính khách.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Nhân viên</b>: bấm 1 KHÁCH trong thanh icon ⇒ mở hộp thư của khách đó.
    ///   </description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="peer">Khách được chọn (chỉ bên nhân viên).</param>
    private async Task SelectStaffTargetAsync(ChatPeerDto? peer = null)
    {
        Popup.SelectTarget(ChatPopupState.StaffTarget);

        if (!IsStaffSide)
        {
            // Khách: hộp thư chung — luôn nạp lại lịch sử hộp thư của mình.
            if (staffMessages.Count == 0)
            {
                await LoadHistoryAsync();
            }

            await ScrollToBottomAsync();
            StateHasChanged();
            return;
        }

        // Nhân viên: cần chọn khách.
        if (peer is null)
        {
            error = "Hãy chọn khách hàng trước khi gửi.";
            StateHasChanged();
            return;
        }

        var key = PeerKey(peer);

        if (string.Equals(key, selectedPeerKey, StringComparison.Ordinal))
        {
            StateHasChanged();
            return;
        }

        selectedPeerKey = key;
        selectedCustomerId = peer.PeerId;
        await LoadHistoryAsync();
        await ScrollToBottomAsync();
    }

    /// <summary>Kiểm tra đối tác có phải đang được chọn không (theo id + kind).</summary>
    private bool IsSelectedPeer(ChatPeerDto peer) =>
        string.Equals(PeerKey(peer), selectedPeerKey, StringComparison.Ordinal);

    private void HandlePopupChanged() => _ = InvokeAsync(StateHasChanged);

    /// <summary>
    /// Cuộn khung tin nhắn xuống đáy (mượt) — gọi sau khi có tin nhắn mới.
    ///
    /// <para>
    /// Chỉ cuộn khi người dùng đang ở gần đáy (hoặc mới mở panel) ⇒ không
    /// cướp thao tác cuộn của họ khi họ đang đọc tin cũ.
    /// </para>
    /// </summary>
    private async Task ScrollToBottomAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("chatWidget.scrollToBottom", MessagesBoxId);
        }
        catch (JSException)
        {
            // Prerender / circuit chưa sẵn sàng ⇒ bỏ qua, không làm hỏng UI.
        }
        catch (InvalidOperationException)
        {
            // JS interop chưa sẵn sàng (render mode) ⇒ bỏ qua.
        }
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

    /// <summary>Dùng gợi ý mẫu ⇒ điền vào ô nhập và gửi luôn (gửi cho AI).</summary>
    private async Task UseSuggestionAsync(string suggestion)
    {
        draft = suggestion;

        // Gợi ý mẫu luôn hỏi AI (kể cả khi đang chọn nhân viên).
        var previousTarget = Popup.Target;
        Popup.SelectTarget(ChatPopupState.AiTarget);

        try
        {
            await SendAsync();
        }
        finally
        {
            if (previousTarget != ChatPopupState.AiTarget)
            {
                Popup.SelectTarget(previousTarget);
            }
        }
    }

    /// <summary>
    /// Gửi tin nhắn theo đích đến hiện tại: AI (chatbot) hoặc nhân viên.
    /// </summary>
    private async Task SendAsync()
    {
        var text = (draft ?? string.Empty).Trim();

        if (text.Length == 0 || sending)
        {
            return;
        }

        if (TargetIsStaff)
        {
            await SendToStaffAsync(text);
        }
        else
        {
            await SendToAiAsync(text);
        }
    }

    /// <summary>Gửi câu hỏi cho trợ lý AI.</summary>
    private async Task SendToAiAsync(string text)
    {
        sending = true;
        error = null;
        draft = string.Empty;

            aiMessages.Add(new ChatTurn(
                text,
                ChatAuthor.User,
                CurrentUserDisplayName,
                CurrentUserRole,
                CurrentUserAvatarUrl,
                DateTime.Now,
                Sequence: NextSequence()));

            TrimAiHistory();
            StateHasChanged();
            await ScrollToBottomAsync();

        try
        {
            // Lịch sử hội thoại gửi lên API.
            //
            // ⚠️ Sửa lỗi (11/11/2026) — mất ngữ cảnh câu hỏi nối tiếp:
            //   Trước đây chỉ gửi các lượt AI (`.Where(m => !m.IsUser)`) ⇒ backend
            //   KHÔNG biết khách vừa hỏi gì ⇒ câu nối tiếp ("chỉ 1 món?",
            //   "còn món nào nữa?") bị trả lời chung chung hoặc sai ngữ cảnh.
            //   Nay gửi CẢ lượt user lẫn lượt AI (đúng thứ tự hội thoại) để
            //   backend giữ được ngữ cảnh.
            var history = aiMessages
                .Take(MaxAiHistoryItems - 1)
                .Select(m => new ChatHistoryItemDto
                {
                    Role = m.Author == ChatAuthor.User ? "user" : "assistant",
                    Content = m.Content
                })
                .ToList();

            var request = new ChatMessageRequestDto
            {
                Message = text,
                CustomerId = CurrentCustomerId,
                History = history
            };

            var result = await ChatbotClient.SendMessageAsync(request);

            if (!result.IsSuccess)
            {
                error = result.Error ?? "Không gửi được câu hỏi.";
                Logger.LineWarning($"[ChatWidget] Chatbot SendMessageAsync thất bại: {result.Error}");
                return;
            }

            var reply = result.Data?.Reply;

            aiMessages.Add(new ChatTurn(
                string.IsNullOrWhiteSpace(reply) ? "…" : reply!,
                ChatAuthor.Ai,
                "Trợ lý PetCare",
                null,
                null,
                DateTime.Now,
                Sequence: NextSequence()));

            TrimAiHistory();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LineError(ex, $"[ChatWidget] Lỗi khi gọi chatbot: {ex.Message}");
            error = "Không gửi được câu hỏi. Vui lòng thử lại.";
        }
        finally
        {
            sending = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Gửi tin nhắn cho nhân viên: gọi <c>POST api/chat/send</c> (ghi DB + đẩy realtime).
    ///
    /// <para>
    /// Nếu SignalR đang kết nối thì vẫn gửi qua HTTP để bảo đảm tin được
    /// lưu vào DB và trả về DTO chuẩn — realtime chỉ là kênh nhận.
    /// </para>
    /// </summary>
    private async Task SendToStaffAsync(string text)
    {
        // ⚠️ HỘP THƯ CHUNG (13/11/2026):
        //   Khách ⇒ gửi vào hộp thư chung (receiverKind = "pool", không chọn người).
        //   Nhân viên ⇒ gửi vào hộp thư của KHÁCH đang chọn.
        var peer = SelectedPeer;

        var receiverId = IsStaffSide ? peer?.PeerId ?? 0 : CurrentUserId;
        var receiverKind = IsStaffSide ? ChatRepositoryKind.Customer : ChatRepositoryKind.Pool;

        if (receiverId <= 0)
        {
            error = "Hãy chọn khách hàng trước khi gửi.";
            StateHasChanged();
            return;
        }

        sending = true;
        error = null;
        draft = string.Empty;

        // Tin "optimistic" — hiện ngay để UX mượt, chưa có id trong DB.
        var optimistic = new ChatTurn(
            text,
            ChatAuthor.User,
            CurrentUserDisplayName,
            CurrentUserRole,
            CurrentUserAvatarUrl,
            DateTime.Now,
            Sequence: NextSequence());

        staffMessages.Add(optimistic);

        TrimStaffMessages();
        StateHasChanged();
        await ScrollToBottomAsync();

        try
        {
            var result = await ChatClient.SendMessageAsync(new ChatSendRequestDto(
                receiverId,
                text,
                receiverKind));

            if (!result.IsSuccess)
            {
                error = "Không gửi được tin nhắn.";
                Logger.LineWarning($"[ChatWidget] SendMessageAsync thất bại: {result.Error}");
                return;
            }

            var sent = result.Data?.Message;

            // ⚠️ Sửa lỗi gửi trùng 2 lần (13/11/2026):
            //   Trước đây thêm tin optimistic (ExternalId = 0) RỒI thêm tiếp tin
            //   từ response HTTP ⇒ 1 câu hiện 2 bubble.
            //   Nay: thay thế bản optimistic bằng bản có id thật (tránh trùng),
            //   và chỉ thêm mới khi bản optimistic đã bị mất (đồng bộ lịch sử).
            if (sent is not null)
            {
                var index = staffMessages.IndexOf(optimistic);

                if (index >= 0)
                {
                    staffMessages[index] = optimistic with
                    {
                        Content = sent.Message,
                        Timestamp = sent.CreatedAt,
                        ExternalId = sent.Id,
                    };
                }
                else if (!staffMessages.Any(m => m.ExternalId == sent.Id))
                {
                    staffMessages.Add(new ChatTurn(
                        sent.Message,
                        ChatAuthor.User,
                        CurrentUserDisplayName,
                        CurrentUserRole,
                        CurrentUserAvatarUrl,
                        sent.CreatedAt,
                        Sequence: NextSequence(),
                        ExternalId: sent.Id));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LineError(ex, $"[ChatWidget] Lỗi khi gửi tin nhắn: {ex.Message}");
            error = "Không gửi được tin nhắn.";
        }
        finally
        {
            sending = false;
            StateHasChanged();
        }
    }

    /// <summary>Kiểm tra trạng thái chatbot (gọi 1 lần duy nhất).</summary>
    private async Task CheckHealthAsync()
    {
        try
        {
            var result = await ChatbotClient.GetHealthAsync();
            aiStatusText = result.IsSuccess && result.Data is not null
                ? (result.Data.Status == "healthy" ? "Sẵn sàng" : "Hạn chế")
                : "Không kết nối được AI";
        }
        catch (Exception ex)
        {
            Logger.LineWarning($"[ChatWidget] Health check chatbot thất bại: {ex.GetType().Name}");
            aiStatusText = "Không kết nối được AI";
        }
    }

    // ---------------------------------------------------------------------
    // Thuộc tính phục vụ hiển thị
    // ---------------------------------------------------------------------

    /// <summary>
    /// Id khách hàng hiện tại (chỉ dùng để lấy ngữ cảnh cá nhân cho AI).
    /// Nhân viên / admin không có customer id ⇒ <c>null</c>.
    /// </summary>
    private int? CurrentCustomerId
    {
        get
        {
            var user = UserSession.CurrentUser;
            if (user is null || user.UserId <= 0)
            {
                return null;
            }

            var role = (user.Role ?? string.Empty).Trim().ToLowerInvariant();
            return role is "customer" or "user" ? user.UserId : null;
        }
    }

    /// <summary>Tên hiển thị của người đang đăng nhập.</summary>
    private string CurrentUserDisplayName =>
        UserSession.CurrentUser?.Name ?? "Bạn";

    /// <summary>Vai trò của người đang đăng nhập.</summary>
    private string? CurrentUserRole => UserSession.CurrentUser?.Role;

    /// <summary>Ảnh đại diện của người đang đăng nhập (chỉ khách hàng có).</summary>
    private string? CurrentUserAvatarUrl => UserSession.CurrentUser?.AvatarUrl;

    /// <summary>Sinh số thứ tự để sắp xếp tin nhắn AI và nhân viên về cùng một luồng.</summary>
    private long sequenceCounter;

    private long NextSequence() => ++sequenceCounter;

    /// <summary>Giới hạn số lượt hội thoại AI giữ trong RAM.</summary>
    private void TrimAiHistory()
    {
        if (aiMessages.Count > MaxAiHistoryItems)
        {
            aiMessages.RemoveRange(0, aiMessages.Count - MaxAiHistoryItems);
        }
    }

    /// <summary>Giới hạn số tin nhắn nhân viên giữ trong RAM.</summary>
    private void TrimStaffMessages()
    {
        if (staffMessages.Count > MaxStaffMessages)
        {
            staffMessages.RemoveRange(0, staffMessages.Count - MaxStaffMessages);
        }
    }

    /// <summary>Kiểu avatar cho một lượt hội thoại.</summary>
    private static string AvatarKind(ChatTurn turn) => turn.Author switch
    {
        ChatAuthor.User => "user",
        ChatAuthor.Staff => "staff",
        _ => "ai"
    };

    /// <summary>Rút gọn tên hiển thị dưới avatar trong thanh chọn đích đến.</summary>
    private static string ShortName(string? name)
    {
        var source = (name ?? string.Empty).Trim();

        if (source.Length == 0)
        {
            return "?";
        }

        var parts = source.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
        {
            return $"{parts[0]}{parts[^1][..1]}";
        }

        return source.Length <= 6 ? source : source[..6];
    }

    /// <summary>Nhãn vai trò hiển thị dưới tên tác giả.</summary>
    private static string AuthorRoleLabel(ChatTurn turn) => turn.Author switch
    {
        ChatAuthor.Ai => "Trợ lý AI",
        _ => RoleLabel(turn.AuthorRole)
    };

    /// <summary>Nhãn vai trò của đối tác trong thanh chọn đích đến.</summary>
    private static string RoleLabel(string? role) => (role ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "doctor" => "Bác sĩ",
        "admin" => "Quản trị",
        "staff" => "Nhân viên",
        "customer" or "user" => "Khách hàng",
        _ => "Nhân viên"
    };

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
            peers = Array.Empty<ChatPeerDto>();
            staffMessages.Clear();
            selectedPeerKey = string.Empty;
            selectedCustomerId = 0;
            StateHasChanged();
            return;
        }

        // Đổi phiên ⇒ dữ liệu cũ không còn hợp lệ.
        peers = Array.Empty<ChatPeerDto>();
        staffMessages.Clear();
        selectedPeerKey = string.Empty;
        selectedCustomerId = 0;

        // Chỉ tải dữ liệu chat nếu panel đang mở (lazy-load).
        if (IsOpen)
        {
            if (IsStaffSide)
            {
                await LoadPeersAsync();
            }
            else if (TargetIsStaff)
            {
                await LoadHistoryAsync();
            }
        }

        await StartRealtimeAsync();
        StateHasChanged();
    });

    public void Dispose()
    {
        UserSession.Changed -= HandleSessionChanged;
        Popup.Changed -= HandlePopupChanged;
        Realtime.ChatReceived -= HandleChatReceived;
    }

    /// <summary>
    /// Giải phóng kết nối SignalR khi component bị huỷ.
    ///
    /// <para>
    /// KHÔNG ngắt kết nối ở đây vì <c>RealtimeHubService</c> được dùng CHUNG —
    /// nếu ChatWidget dispose mà NotificationBell còn dùng thì sẽ đứt realtime.
    /// Service tự dispose khi circuit kết thúc (Blazor Server gọi
    /// <see cref="IAsyncDisposable.DisposeAsync"/> của các scoped service).
    /// </para>
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>Loại tác giả của một lượt hội thoại.</summary>
    private enum ChatAuthor
    {
        /// <summary>Người dùng đang đăng nhập.</summary>
        User,

        /// <summary>Trợ lý AI.</summary>
        Ai,

        /// <summary>Nhân viên / bác sĩ / quản trị viên.</summary>
        Staff
    }

    /// <summary>
    /// Một lượt hội thoại trong UI — dùng chung cho cả AI và nhân viên để
    /// chúng nằm chung MỘT danh sách.
    /// </summary>
    private sealed record ChatTurn(
        string Content,
        ChatAuthor Author,
        string AuthorName,
        string? AuthorRole,
        string? AuthorAvatarUrl,
        DateTime? Timestamp,
        long Sequence,
        int ExternalId = 0);
}
