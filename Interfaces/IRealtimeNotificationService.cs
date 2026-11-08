namespace PetShop.Interfaces;

/// <summary>
/// Dịch vụ đẩy dữ liệu realtime (SignalR) tới client.
///
/// <para>
/// Đây là tầng "service" của luồng 3-layer: Razor/Controller → <b>IRealtimeNotificationService</b>
/// → <c>IHubContext&lt;NotificationHub&gt;</c>. Component Razor <b>không</b> được gọi
/// thẳng vào <c>IHubContext</c> và <b>không</b> được inject
/// <c>ShopPetDatabaseContext</c> (xem AGENTS.md §2.3 / kiến trúc 3-layer).
/// </para>
///
/// <para>
/// Mọi lỗi push đều được nuốt kèm log — lỗi realtime KHÔNG được làm hỏng luồng
/// nghiệp vụ chính (ví dụ: gửi yêu cầu đổi ca vẫn phải trả 200 dù SignalR hỏng).
/// </para>
/// </summary>
public interface IRealtimeNotificationService
{
    /// <summary>
    /// Đẩy một thông báo tới nhân viên <paramref name="userId"/>.
    /// </summary>
    /// <param name="userId">Id nhân viên nhận thông báo.</param>
    /// <param name="title">Tiêu đề (ví dụ "Yêu cầu đổi ca").</param>
    /// <param name="message">Nội dung thông báo.</param>
    /// <param name="relatedRequestId">Id yêu cầu liên quan (nếu có).</param>
    /// <param name="cancellationToken">Token huỷ.</param>
    Task NotifyAsync(
        int userId,
        string title,
        string message,
        int? relatedRequestId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Đẩy một tin nhắn chat 1-1 tới <paramref name="toUserId"/>.
    /// </summary>
    /// <param name="toUserId">Id người nhận.</param>
    /// <param name="fromUserId">Id người gửi.</param>
    /// <param name="message">Nội dung tin nhắn.</param>
    /// <param name="cancellationToken">Token huỷ.</param>
    /// <param name="messageId">
    /// Id bản ghi trong bảng <c>ChatMessages</c> (Q21) — client dùng để chống
    /// hiển thị trùng khi nhận tin qua cả realtime lẫn HTTP. Bằng <c>0</c> nếu
    /// tin chưa được ghi log.
    /// </param>
    Task SendChatAsync(
        int toUserId,
        int fromUserId,
        string message,
        CancellationToken cancellationToken = default,
        int messageId = 0);
}
