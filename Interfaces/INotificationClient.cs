using PetShop.Models;

namespace PetShop.Interfaces;

/// <summary>
/// Client HTTP gọi chính API thông báo của ứng dụng (self-HTTP).
/// Phục vụ <c>Components/Staff/NotificationBell.razor</c> — port từ
/// <c>Projectojt/frontend/src/api/notificationApi.js</c> (nguồn OJT).
/// </summary>
public interface INotificationClient
{
    /// <summary>Lấy danh sách thông báo của nhân viên (port <c>getNotificationsApi</c>).</summary>
    Task<FrontendApiResult<IReadOnlyList<NotificationItemDto>>> GetNotificationsAsync(int staffId, bool unreadOnly = false, CancellationToken cancellationToken = default);

    /// <summary>Lấy số thông báo chưa đọc (port <c>getUnreadCountApi</c>).</summary>
    Task<FrontendApiResult<UnreadCountResultDto>> GetUnreadCountAsync(int staffId, CancellationToken cancellationToken = default);

    /// <summary>Đánh dấu một thông báo đã đọc (port <c>markAsReadApi</c>).</summary>
    Task<FrontendApiResult<bool>> MarkAsReadAsync(int requestId, int staffId, CancellationToken cancellationToken = default);

    /// <summary>Đánh dấu toàn bộ thông báo đã đọc (port <c>markAllAsReadApi</c>).</summary>
    Task<FrontendApiResult<bool>> MarkAllAsReadAsync(int staffId, CancellationToken cancellationToken = default);

    /// <summary>Lấy các yêu cầu đổi ca đang chờ mình duyệt (port <c>getPendingRequestsApi</c>).</summary>
    Task<FrontendApiResult<IReadOnlyList<PendingRequestDto>>> GetPendingRequestsAsync(int staffId, CancellationToken cancellationToken = default);

    /// <summary>Chấp nhận yêu cầu đổi ca (port <c>acceptRequestApi</c>).</summary>
    Task<FrontendApiResult<ShiftExchangeActionResultDto>> AcceptRequestAsync(int requestId, int responderStaffId, CancellationToken cancellationToken = default);

    /// <summary>Từ chối yêu cầu đổi ca (port <c>declineRequestApi</c>).</summary>
    Task<FrontendApiResult<ShiftExchangeActionResultDto>> DeclineRequestAsync(int requestId, int responderStaffId, CancellationToken cancellationToken = default);

    /// <summary>Lấy các yêu cầu đổi ca do mình gửi (port <c>getMyRequestsApi</c>).</summary>
    Task<FrontendApiResult<IReadOnlyList<MyRequestDto>>> GetMyRequestsAsync(int staffId, CancellationToken cancellationToken = default);
}
