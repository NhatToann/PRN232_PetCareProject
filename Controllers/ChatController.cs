using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Controllers;

/// <summary>
/// API chat 1-1 khách ↔ nhân viên (Q21).
///
/// <para>
/// Route gốc: <c>api/chat</c>. Tất cả endpoint đều có <c>[Authorize]</c> và
/// lấy danh tính người dùng từ <b>claims</b> (<c>ClaimTypes.NameIdentifier</c> /
/// <c>ClaimTypes.Role</c>) — <b>không</b> tin giá trị trong query string hay
/// body để xác định người gửi.
/// </para>
///
/// <para>
/// Đi qua tầng service <see cref="IChatService"/> (3-layer):
/// Controller → Service → Repository → DbContext.
/// </para>
/// </summary>
[ApiController]
[Route("api/chat")]
[Authorize]
public sealed class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IChatService chatService, ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    /// <summary>
    /// Lấy lịch sử chat trong hộp thư.
    ///
    /// <para>
    /// <b>HỘP THƯ CHUNG (13/11/2026):</b> khách gọi với <c>peerKind = "pool"</c>
    /// ⇒ lấy hộp thư nhân viên của chính khách (mọi nhân viên đều ghi vào đây).
    /// Nhân viên gọi với <c>peerKind = "customer"</c> ⇒ lấy hộp thư của khách đó.
    /// </para>
    /// </summary>
    /// <param name="peerId">Id của đối tác (theo không gian id của role đối tác).</param>
    /// <param name="peerKind">
    /// Loại đối tác: <c>staff</c> / <c>admin</c> / <c>doctor</c> / <c>customer</c> /
    /// <c>pool</c> (hộp thư chung của nhân viên).
    /// </param>
    /// <param name="ct">Token huỷ.</param>
    [HttpGet("history/{peerId:int}")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetHistory(
        int peerId,
        [FromQuery] string? peerKind,
        CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId <= 0)
        {
            return Unauthorized();
        }

        try
        {
            var history = await _chatService.GetHistoryAsync(
                userId,
                ResolveRole(),
                peerId,
                peerKind,
                ct);

            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, $"[ChatController] Không lấy được lịch sử chat với peer {peerId}");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Không lấy được lịch sử chat." });
        }
    }

    /// <summary>
    /// Lấy danh sách đối tác có thể chat.
    ///
    /// <para>
    /// <b>HỘP THƯ CHUNG (13/11/2026):</b> khách KHÔNG cần danh sách nhân viên
    /// nữa (chỉ chọn "AI" hoặc "Nhân viên") ⇒ trả về mảng rỗng. Nhân viên ⇒
    /// danh sách khách đang có hộp thư (mới nhất lên đầu).
    /// </para>
    /// </summary>
    /// <param name="ct">Token huỷ.</param>
    [HttpGet("peers")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatPeerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<ChatPeerDto>>> GetPeers(CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId <= 0)
        {
            return Unauthorized();
        }

        try
        {
            var peers = await _chatService.GetPeersAsync(userId, ResolveRole(), ct);
            return Ok(peers);
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, "[ChatController] Không lấy được danh sách đối tác chat");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Không lấy được danh sách đối tác chat." });
        }
    }

    /// <summary>
    /// Gửi tin nhắn 1-1: ghi vào bảng <c>ChatMessages</c> rồi đẩy realtime qua SignalR.
    /// </summary>
    /// <param name="request">Body gồm <c>receiverId</c> + <c>message</c> (+ <c>receiverKind</c> tuỳ chọn).</param>
    /// <param name="ct">Token huỷ.</param>
    [HttpPost("send")]
    [ProducesResponseType(typeof(ChatSendResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ChatSendResultDto>> Send(
        [FromBody] ChatSendRequestDto request,
        CancellationToken ct)
    {
        var userId = ResolveUserId();
        if (userId <= 0)
        {
            return Unauthorized();
        }

        try
        {
            var result = await _chatService.SendMessageAsync(userId, ResolveRole(), request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, "[ChatController] Không gửi được tin nhắn chat");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Không gửi được tin nhắn." });
        }
    }

    /// <summary>Lấy id người dùng hiện tại từ claim.</summary>
    private int ResolveUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out var userId) && userId > 0 ? userId : 0;
    }

    /// <summary>Lấy role người dùng hiện tại từ claim.</summary>
    private string? ResolveRole() => User.FindFirst(ClaimTypes.Role)?.Value;
}
