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
    /// Lấy lịch sử chat giữa người dùng hiện tại và một đối tác.
    /// </summary>
    /// <param name="currentUserId">Id của người đang đăng nhập.</param>
    /// <param name="currentUserKind">Loại người gửi: <c>customer</c> / <c>staff</c> / <c>admin</c> / <c>doctor</c>.</param>
    /// <param name="peerId">Id của đối tác.</param>
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
    /// <param name="customerId">Id khách hàng của luồng chat.</param>
    /// <param name="staffId">Id nhân viên của luồng chat (nullable).</param>
    /// <param name="senderType"><c>customer</c> hoặc <c>staff</c>.</param>
    /// <param name="message">Nội dung tin nhắn.</param>
    /// <param name="ct">Token huỷ.</param>
    /// <returns>Bản ghi vừa tạo (đã có <c>MessageID</c>).</returns>
    Task<ChatMessage> AddMessageAsync(
        int customerId,
        int? staffId,
        string senderType,
        string message,
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
}
