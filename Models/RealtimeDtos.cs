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
/// <param name="MessageId">
/// Id bản ghi trong bảng <c>ChatMessages</c> (Q21 — dùng để chống trùng khi
/// client nhận tin qua realtime và đồng thời qua HTTP). Bằng <c>0</c> nếu tin
/// chưa được ghi log.
/// </param>
/// <param name="FromKind">
/// Loại người gửi: <c>staff</c> / <c>admin</c> / <c>doctor</c> / <c>customer</c>.
///
/// <para>
/// <b>VÌ SAO CẦN (12/11/2026):</b> các bảng người dùng có <b>không gian id
/// riêng biệt</b> — <c>staff_id = 1</c> và <c>admin_id = 1</c> và
/// <c>doctor_id = 1</c> đều tồn tại. Chỉ dựa vào id thì client không biết
/// tin nhắn đến từ ai ⇒ hiển thị nhầm hộp thoại. Kèm <c>FromKind</c> để client
/// ghép đúng (id + kind) với đối tác đang mở.
/// </para>
/// </param>
/// <param name="ToKind">
/// Loại người nhận (dùng để server đẩy tin vào đúng group chat).
/// </param>
/// <param name="ToCustomerId">
/// Id KHÁCH HÀNG mà tin nhắn này thuộc về (luồng hộp thư chung).
///
/// <para>
/// <b>VÌ SAO CẦN (13/11/2026):</b> luồng chat khách ↔ nhân viên nay là
/// <b>HỘP THƯ CHUNG</b> — mọi nhân viên đều thấy tin của cùng 1 khách.
/// Client nhân viên nhận tin từ group <c>chat:staff</c> (mọi khách lẫn lẫn)
/// ⇒ phải biết tin đó thuộc khách nào mới lọc đúng vào khung chat đang mở.
/// </para>
/// </param>
/// <param name="SenderName">
/// Tên hiển thị của người gửi (để dựng bubble khi nhận realtime — không
/// cần gọi lại API lịch sử).
/// </param>
/// <param name="SenderRole">Vai trò của người gửi: <c>staff</c> / <c>admin</c> / <c>doctor</c> / <c>customer</c>.</param>
public sealed record ChatEnvelope(
    int UserId,
    int FromUserId,
    string Message,
    DateTime SentAt,
    bool IsPersisted,
    int MessageId = 0,
    string FromKind = "",
    string ToKind = "",
    int ToCustomerId = 0,
    string SenderName = "",
    string SenderRole = "");
