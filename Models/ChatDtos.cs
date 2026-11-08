using System;

namespace PetShop.Models;

/// <summary>
/// Một tin nhắn chat 1-1 trong hộp thoại (Q21).
///
/// <para>
/// Dùng chung cho cả hai chiều: API trả về cho client Blazor và client
/// Blazor dùng để render khung tin nhắn. Không chứa thông tin nhạy cảm nào
/// ngoài nội dung tin nhắn.
/// </para>
/// </summary>
/// <param name="Id">Id bản ghi trong bảng <c>ChatMessages</c>.</param>
/// <param name="SenderId">Id người gửi (id theo không gian id của role tương ứng).</param>
/// <param name="ReceiverId">Id người nhận.</param>
/// <param name="Message">Nội dung tin nhắn.</param>
/// <param name="CreatedAt">Thời điểm gửi tin nhắn.</param>
/// <param name="IsMine"><c>true</c> nếu tin nhắn do chính người đang đăng nhập gửi.</param>
public sealed record ChatMessageDto(
    int Id,
    int SenderId,
    int ReceiverId,
    string Message,
    DateTime CreatedAt,
    bool IsMine);

/// <summary>
/// Một người có thể trò chuyện 1-1 (đối tác chat).
///
/// <para>
/// <b>Vì sao cần <paramref name="PeerKind"/>:</b> các bảng
/// <c>Staff</c> / <c>Admin</c> / <c>Doctor</c> / <c>Customer</c> có <b>không gian id
/// riêng biệt</b> (cùng số 1 có thể vừa là staff vừa là admin vừa là khách).
/// Vì vậy id một mình không đủ để xác định đối tác — kèm theo
/// <paramref name="PeerKind"/> để biết tra cứu ở bảng nào.
/// </para>
/// </summary>
/// <param name="PeerId">Id của đối tác trong bảng tương ứng với <paramref name="PeerKind"/>.</param>
/// <param name="DisplayName">Tên hiển thị.</param>
/// <param name="PeerRole">Vai trò: <c>staff</c> / <c>admin</c> / <c>doctor</c> / <c>customer</c>.</param>
/// <param name="PeerKind">
/// Loại đối tác: <c>staff</c> / <c>admin</c> / <c>doctor</c> / <c>customer</c> —
/// dùng để tra cứu đúng bảng và ghép đúng luồng chat.
/// </param>
public sealed record ChatPeerDto(
    int PeerId,
    string DisplayName,
    string PeerRole,
    string PeerKind);

/// <summary>
/// Body của <c>POST /api/chat/send</c>.
/// </summary>
/// <param name="ReceiverId">Id người nhận (lấy theo không gian id của role người nhận).</param>
/// <param name="Message">Nội dung tin nhắn.</param>
/// <param name="ReceiverKind">
/// Loại người nhận: <c>staff</c> / <c>admin</c> / <c>doctor</c> / <c>customer</c>.
/// Mặc định <c>staff</c> cho người nhận là nhân viên; người nhận là khách thì
/// gửi <c>customer</c>.
/// </param>
public sealed record ChatSendRequestDto(
    int ReceiverId,
    string Message,
    string? ReceiverKind = null);

/// <summary>
/// Kết quả gửi tin nhắn — trả về tin vừa lưu kèm thông tin đối tác để client
/// cập nhật UI ngay mà không cần gọi lại API.
/// </summary>
/// <param name="Message">Tin nhắn vừa lưu (đã gắn cờ <c>IsMine</c>).</param>
/// <param name="Peer">Đối tác nhận tin nhắn.</param>
public sealed record ChatSendResultDto(ChatMessageDto Message, ChatPeerDto Peer);
