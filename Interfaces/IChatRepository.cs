using PetShop.Models;

namespace PetShop.Interfaces;

/// <summary>
/// Truy vấn / ghi lịch sử chat 1-1 trên bảng <c>ChatMessages</c> (Q21).
///
/// <para>
/// <b>Bố cục bảng <c>ChatMessages</c> (đọc từ schema thật, KHÔNG đổi schema):</b>
/// <list type="bullet">
///   <item><description><c>MessageID</c> — khóa chính, identity.</description></item>
///   <item><description><c>CustomerID</c> — id khách hàng trong luồng chat.</description></item>
///   <item><description><c>StaffID</c> — id nhân viên trong luồng chat (nullable).</description></item>
///   <item><description><c>SenderType</c> — <c>customer</c> hoặc <c>staff</c> (CHECK constraint).</description></item>
///   <item><description><c>Message</c> — nội dung tin nhắn.</description></item>
///   <item><description><c>SentAt</c> — thời điểm gửi.</description></item>
///   <item><description><c>IsRead</c> — cờ đã đọc.</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Khoá của một luồng chat 1-1 = (CustomerID, StaffID).</b> Hai constraint
/// FK của bảng đều ở trạng thái <c>is_disabled = 1</c> trong DB ⇒ không bị
/// ép buộc, nên <c>StaffID</c> có thể mang đúng <c>Staff.staff_id</c> (không
/// gian id của nhân viên trong ứng dụng) mà không vi phạm ràng buộc.
/// </para>
///
/// <para>
/// <b>Quy ước:</b> với luồng khách ↔ nhân viên thì <c>CustomerID</c> = id khách,
/// <c>StaffID</c> = id nhân viên. Với luồng nhân viên ↔ khách thì <c>CustomerID</c>
/// = id khách, <c>StaffID</c> = id nhân viên — <b>giống nhau</b> ⇒ hai chiều
/// của cùng một cuộc hội thoại luôn đọc được lịch sử giống nhau.
/// </para>
/// </summary>
public interface IChatRepository
{
    /// <summary>
    /// Lấy lịch sử chat trong HỘP THƯ CHUNG của một khách hàng.
    ///
    /// <para>
    /// <b>Thay đổi 13/11/2026:</b> trước đây lịch sử gắn với cặp 1-1
    /// (khách ↔ 1 nhân viên). Nay là <b>hộp thư chung</b>: mọi nhân viên đều
    /// đọc được (và ghi vào) cùng một luồng theo <c>CustomerID</c>.
    /// </para>
    /// </summary>
    /// <param name="currentUserId">Id của người đang đăng nhập.</param>
    /// <param name="currentUserKind">Loại người gửi: <c>customer</c> / <c>staff</c> / <c>admin</c> / <c>doctor</c>.</param>
    /// <param name="peerId">Id của đối tác (khách hàng khi nhân viên mở hộp thư).</param>
    /// <param name="peerKind">Loại đối tác.</param>
    /// <param name="take">Số tin gần nhất tối đa lấy (50–100).</param>
    /// <param name="ct">Token huỷ.</param>
    /// <returns>Danh sách tin nhắn <b>tăng dần theo thời gian</b> để render khung chat.</returns>
    Task<IReadOnlyList<ChatMessageDto>> GetHistoryAsync(
        int currentUserId,
        string currentUserKind,
        int peerId,
        string peerKind,
        int take = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Ghi một tin nhắn mới vào bảng <c>ChatMessages</c>.
    /// </summary>
    /// <param name="customerId">Id khách hàng của luồng chat (khoá hộp thư).</param>
    /// <param name="staffId">Id nhân viên gửi tin (nullable — khách gửi thì <c>null</c>).</param>
    /// <param name="senderType"><c>staff</c> hoặc <c>customer</c> (cột bị CHECK constraint giới hạn).</param>
    /// <param name="message">Nội dung tin nhắn.</param>
    /// <param name="senderName">Tên hiển thị của người gửi (hộp thư chung — nhiều người gửi).</param>
    /// <param name="senderKind">Loại thật của người gửi: <c>staff</c> / <c>admin</c> / <c>doctor</c> / <c>customer</c>.</param>
    /// <param name="ct">Token huỷ.</param>
    /// <returns>Bản ghi vừa tạo (đã có <c>MessageID</c>).</returns>
    Task<ChatMessage> AddMessageAsync(
        int customerId,
        int? staffId,
        string senderType,
        string message,
        string? senderName = null,
        string? senderKind = null,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy danh sách nhân viên (role staff / admin / doctor) có thể chat với khách.
    /// </summary>
    /// <param name="ct">Token huỷ.</param>
    Task<IReadOnlyList<ChatPeerDto>> GetStaffPeersAsync(CancellationToken ct = default);

    /// <summary>
    /// Lấy danh sách khách hàng có thể chat với nhân viên.
    /// </summary>
    /// <param name="ct">Token huỷ.</param>
    Task<IReadOnlyList<ChatPeerDto>> GetCustomerPeersAsync(CancellationToken ct = default);

    /// <summary>Tra tên hiển thị của một nhân viên (bảng <c>Staff</c>).</summary>
    /// <param name="staffId">Id nhân viên.</param>
    /// <param name="ct">Token huỷ.</param>
    Task<string?> GetStaffNameAsync(int staffId, CancellationToken ct = default);

    /// <summary>Tra tên hiển thị của một quản trị viên (bảng <c>Admin</c>).</summary>
    /// <param name="adminId">Id quản trị.</param>
    /// <param name="ct">Token huỷ.</param>
    Task<string?> GetAdminNameAsync(int adminId, CancellationToken ct = default);

    /// <summary>Tra tên hiển thị của một bác sĩ (bảng <c>Doctor</c>).</summary>
    /// <param name="doctorId">Id bác sĩ.</param>
    /// <param name="ct">Token huỷ.</param>
    Task<string?> GetDoctorNameAsync(int doctorId, CancellationToken ct = default);

    /// <summary>Tra tên hiển thị của một khách hàng (bảng <c>Customer</c>).</summary>
    /// <param name="customerId">Id khách hàng.</param>
    /// <param name="ct">Token huỷ.</param>
    Task<string?> GetCustomerNameAsync(int customerId, CancellationToken ct = default);
}
