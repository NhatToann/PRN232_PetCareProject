namespace PetShop.Models;

/// <summary>
/// Nội dung gửi qua SignalR cho sự kiện thông báo (event <c>"NotificationReceived"</c>).
///
/// <para>
/// Được dùng chung giữa server (<c>Services/RealtimeNotificationService.cs</c>) và
/// client Blazor (<c>Components/Staff/NotificationBell.razor.cs</c>) — cùng kiểu
/// record để JSON khớp ở cả hai chiều.
/// </para>
/// </summary>
/// <param name="UserId">Id người nhận (nhân viên).</param>
/// <param name="Title">Tiêu đề thông báo.</param>
/// <param name="Message">Nội dung thông báo.</param>
/// <param name="RelatedRequestId">Id yêu cầu liên quan (nếu có) — dùng để mở đúng panel.</param>
/// <param name="SentAt">Thời điểm server phát thông báo.</param>
public sealed record RealtimeNotificationDto(
    int UserId,
    string Title,
    string Message,
    int? RelatedRequestId,
    DateTime SentAt);

/// <summary>
/// Nội dung gửi qua SignalR cho sự kiện chat (event <c>"ChatReceived"</c>).
/// </summary>
/// <param name="UserId">Id người nhận.</param>
/// <param name="FromUserId">Id người gửi.</param>
/// <param name="Message">Nội dung tin nhắn.</param>
/// <param name="SentAt">Thời điểm gửi.</param>
/// <param name="IsPersisted">
/// <c>true</c> nếu tin nhắn đã được ghi vào bảng <c>ChatMessages</c>;
/// <c>false</c> ở chế độ demo (chưa xác thực) nên không ghi log.
/// </param>
public sealed record ChatEnvelope(
    int UserId,
    int FromUserId,
    string Message,
    DateTime SentAt,
    bool IsPersisted);
