using PetShop.Models;

namespace PetShop.Interfaces;

/// <summary>
/// Điều phối luồng chat 1-1 (Q21).
///
/// <para>
/// Tầng "service" của mô hình 3-layer: Razor / Controller →
/// <b>IChatService</b> → <see cref="IChatRepository"/> → DbContext.
/// </para>
///
/// <para>
/// Nhiệm vụ: lấy lịch sử chat, gửi tin (ghi DB + đẩy realtime qua
/// <see cref="IRealtimeNotificationService"/>), và dựng danh sách đối tác.
/// Lỗi realtime <b>không</b> được làm hỏng luồng gửi tin — người dùng vẫn
/// nhận được tin đã lưu.
/// </para>
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Lấy lịch sử chat với một đối tác.
    /// </summary>
    /// <param name="currentUserId">Id người đang đăng nhập (lấy từ claims).</param>
    /// <param name="currentRole">Role của người đang đăng nhập (lấy từ claims).</param>
    /// <param name="peerId">Id đối tác.</param>
    /// <param name="peerKind">Loại đối tác (mặc định suy ra từ role của người gửi).</param>
    /// <param name="ct">Token huỷ.</param>
    Task<IReadOnlyList<ChatMessageDto>> GetHistoryAsync(
        int currentUserId,
        string? currentRole,
        int peerId,
        string? peerKind = null,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy danh sách đối tác có thể chat: nhân viên (cho khách) hoặc khách hàng (cho nhân viên).
    /// </summary>
    /// <param name="currentUserId">Id người đang đăng nhập (lấy từ claims).</param>
    /// <param name="currentRole">Role của người đang đăng nhập (lấy từ claims).</param>
    /// <param name="ct">Token huỷ.</param>
    Task<IReadOnlyList<ChatPeerDto>> GetPeersAsync(
        int currentUserId,
        string? currentRole,
        CancellationToken ct = default);

    /// <summary>
    /// Gửi tin nhắn: ghi vào bảng <c>ChatMessages</c> rồi đẩy realtime tới người nhận.
    /// </summary>
    /// <param name="currentUserId">Id người gửi (lấy từ claims — không tin body).</param>
    /// <param name="currentRole">Role người gửi (lấy từ claims).</param>
    /// <param name="request">Nội dung tin nhắn + id người nhận.</param>
    /// <param name="ct">Token huỷ.</param>
    Task<ChatSendResultDto> SendMessageAsync(
        int currentUserId,
        string? currentRole,
        ChatSendRequestDto request,
        CancellationToken ct = default);

    /// <summary>
    /// Tra cứu thông tin đối tác theo id + loại.
    /// </summary>
    /// <param name="peerId">Id đối tác.</param>
    /// <param name="peerKind">Loại đối tác.</param>
    /// <param name="ct">Token huỷ.</param>
    /// <returns>Thông tin đối tác hoặc <c>null</c> nếu không tồn tại.</returns>
    Task<ChatPeerDto?> GetPeerAsync(int peerId, string peerKind, CancellationToken ct = default);

    /// <summary>
    /// Chuẩn hoá role của người dùng về loại người dùng dùng trong chat.
    /// </summary>
    /// <param name="role">Role thô (claim <c>ClaimTypes.Role</c>).</param>
    /// <returns>
    /// <c>customer</c> nếu là khách hàng; <c>staff</c> / <c>admin</c> / <c>doctor</c> nếu là nhân viên.
    /// </returns>
    static string NormalizeKind(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        "admin" => ChatRepositoryKind.Admin,
        "doctor" => ChatRepositoryKind.Doctor,
        "staff" or "manager" or "employee" => ChatRepositoryKind.Staff,
        _ => ChatRepositoryKind.Customer,
    };
}

/// <summary>
/// Hằng số loại người dùng dùng chung cho tầng service (tránh phụ thuộc vào
/// tầng repository từ interface).
/// </summary>
public static class ChatRepositoryKind
{
    /// <summary>Nhân viên (bảng <c>Staff</c>).</summary>
    public const string Staff = "staff";

    /// <summary>Quản trị viên (bảng <c>Admin</c>).</summary>
    public const string Admin = "admin";

    /// <summary>Bác sĩ (bảng <c>Doctor</c>).</summary>
    public const string Doctor = "doctor";

    /// <summary>Khách hàng (bảng <c>Customer</c>).</summary>
    public const string Customer = "customer";
}
