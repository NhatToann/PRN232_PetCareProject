using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
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
/// </summary>
public partial class NotificationBell : ComponentBase
{
    [Inject] private INotificationClient NotificationClient { get; set; } = default!;

    [Inject] private UserSessionService UserSession { get; set; } = default!;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Inject] private ILogger<NotificationBell> Logger { get; set; } = default!;

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
        StateHasChanged();
    });

    public void Dispose()
    {
        UserSession.Changed -= HandleSessionChanged;
    }
}
