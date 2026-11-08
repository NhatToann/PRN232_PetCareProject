using PetShop.Models;

namespace PetShop.Interfaces;

/// <summary>
/// Client HTTP gọi API chat 1-1 (<c>api/chat/*</c>) từ tầng UI (Q21).
///
/// <para>
/// Theo đúng convention của dự án: các trang Blazor không gọi service trực
/// tiếp mà gọi qua HTTP client tới chính endpoint trong app. Client đã được
/// gắn header phiên đăng nhập (<c>X-User-Id</c> / <c>X-User-Role</c> /
/// <c>X-Customer-Id</c>) bởi <c>SpaAuthHeaderHandler</c>.
/// </para>
/// </summary>
public interface IChatClient
{
    /// <summary>Lấy danh sách đối tác có thể chat.</summary>
    /// <param name="ct">Token huỷ.</param>
    Task<FrontendApiResult<IReadOnlyList<ChatPeerDto>>> GetPeersAsync(CancellationToken ct = default);

    /// <summary>Lấy lịch sử chat với đối tác.</summary>
    /// <param name="peerId">Id đối tác.</param>
    /// <param name="peerKind">Loại đối tác (tuỳ chọn).</param>
    /// <param name="ct">Token huỷ.</param>
    Task<FrontendApiResult<IReadOnlyList<ChatMessageDto>>> GetHistoryAsync(
        int peerId,
        string? peerKind = null,
        CancellationToken ct = default);

    /// <summary>Gửi tin nhắn (ghi DB + đẩy realtime).</summary>
    /// <param name="request">Nội dung tin nhắn + id người nhận.</param>
    /// <param name="ct">Token huỷ.</param>
    Task<FrontendApiResult<ChatSendResultDto>> SendMessageAsync(
        ChatSendRequestDto request,
        CancellationToken ct = default);
}
