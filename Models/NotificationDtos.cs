using System.Text.Json.Serialization;

namespace PetShop.Models;

/// <summary>
/// Một thông báo trả về từ <c>api/notifications/{staffId}</c>.
/// Khớp với <c>NotificationDto</c> do <c>NotificationController</c> trả về.
/// Tên field giữ nguyên theo nguồn OJT (React đọc <c>n.notificationId</c>, <c>n.isRead</c>, <c>n.createdAt</c>).
/// </summary>
/// <param name="NotificationId">Id thông báo (trùng <c>RequestId</c> của yêu cầu đổi ca).</param>
/// <param name="StaffId">Id nhân viên sở hữu thông báo.</param>
/// <param name="Title">Tiêu đề thông báo.</param>
/// <param name="Message">Nội dung thông báo.</param>
/// <param name="IsRead">Đã đọc hay chưa.</param>
/// <param name="CreatedAt">Thời điểm tạo thông báo.</param>
/// <param name="RelatedRequestId">Id yêu cầu đổi ca liên quan (nếu có).</param>
/// <param name="IsHandled">Yêu cầu đã được xử lý hay chưa.</param>
public sealed record NotificationItemDto(
    int NotificationId,
    int StaffId,
    string? Title,
    string? Message,
    bool IsRead,
    DateTime CreatedAt,
    int? RelatedRequestId,
    bool IsHandled);

/// <summary>
/// Kết quả đếm thông báo chưa đọc — khớp với <c>UnreadCountDto</c> (<c>{ count }</c>).
/// </summary>
/// <param name="Count">Số thông báo chưa đọc.</param>
public sealed record UnreadCountResultDto(
    [property: JsonPropertyName("count")] int Count);
