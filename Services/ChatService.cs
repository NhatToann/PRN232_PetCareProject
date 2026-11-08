using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Repositories;

namespace PetShop.Services;

/// <summary>
/// Điều phối luồng chat 1-1 khách ↔ nhân viên (Q21).
///
/// <para>
/// 3-layer: Controller / Razor → <see cref="IChatService"/> → <see cref="IChatRepository"/>
/// → DbContext. Service <b>không</b> inject DbContext.
/// </para>
///
/// <para>
/// Gửi tin: ghi DB trước (để tin chắc chắn lưu), sau đó đẩy realtime qua
/// <see cref="IRealtimeNotificationService"/>. Lỗi realtime bị nuốt kèm log ở
/// tầng realtime ⇒ người dùng vẫn thấy tin đã gửi (fallback HTTP).
/// </para>
/// </summary>
public sealed class ChatService : IChatService
{
    private readonly IChatRepository _repository;
    private readonly IRealtimeNotificationService _realtime;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IChatRepository repository,
        IRealtimeNotificationService realtime,
        ILogger<ChatService> logger)
    {
        _repository = repository;
        _realtime = realtime;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatMessageDto>> GetHistoryAsync(
        int currentUserId,
        string? currentRole,
        int peerId,
        string? peerKind = null,
        CancellationToken ct = default)
    {
        if (currentUserId <= 0)
        {
            throw new ArgumentException("Không xác định được người dùng hiện tại.", nameof(currentUserId));
        }

        if (peerId <= 0)
        {
            return Array.Empty<ChatMessageDto>();
        }

        var currentKind = IChatService.NormalizeKind(currentRole);
        var resolvedKind = ResolvePeerKind(peerKind, currentKind);

        return await _repository.GetHistoryAsync(
            currentUserId,
            currentKind,
            peerId,
            resolvedKind,
            100,
            ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatPeerDto>> GetPeersAsync(
        int currentUserId,
        string? currentRole,
        CancellationToken ct = default)
    {
        if (currentUserId <= 0)
        {
            throw new ArgumentException("Không xác định được người dùng hiện tại.", nameof(currentUserId));
        }

        var kind = IChatService.NormalizeKind(currentRole);

        // Khách hàng ⇒ chat với nhân viên; nhân viên ⇒ chat với khách hàng.
        return kind == ChatRepositoryKind.Customer
            ? await _repository.GetStaffPeersAsync(ct)
            : await _repository.GetCustomerPeersAsync(ct);
    }

    /// <inheritdoc />
    public async Task<ChatSendResultDto> SendMessageAsync(
        int currentUserId,
        string? currentRole,
        ChatSendRequestDto request,
        CancellationToken ct = default)
    {
        if (currentUserId <= 0)
        {
            throw new ArgumentException("Không xác định được người gửi.", nameof(currentUserId));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var message = (request.Message ?? string.Empty).Trim();

        if (message.Length == 0)
        {
            throw new ArgumentException("Nội dung tin nhắn không được để trống.", nameof(request));
        }

        if (message.Length > 2000)
        {
            throw new ArgumentException("Nội dung tin nhắn quá dài (tối đa 2000 ký tự).", nameof(request));
        }

        if (request.ReceiverId <= 0)
        {
            throw new ArgumentException("Người nhận không hợp lệ.", nameof(request));
        }

        var currentKind = IChatService.NormalizeKind(currentRole);
        var receiverKind = ResolvePeerKind(request.ReceiverKind, currentKind);

        // Kiểm tra đối tác có thật không (và lấy tên hiển thị).
        var peer = await GetPeerAsync(request.ReceiverId, receiverKind, ct);
        if (peer is null)
        {
            throw new KeyNotFoundException("Không tìm thấy người nhận.");
        }

        var (customerId, staffId) = ChatRepository.ResolveThread(
            currentUserId,
            currentKind,
            request.ReceiverId,
            receiverKind);

        var senderType = currentKind == ChatRepositoryKind.Customer
            ? ChatRepositoryKind.Customer
            : ChatRepositoryKind.Staff;

        // 1) Ghi DB trước ⇒ tin chắc chắn được lưu kể cả khi realtime lỗi.
        var row = await _repository.AddMessageAsync(
            customerId,
            staffId,
            senderType,
            message,
            ct);

        // 2) Đẩy realtime tới người nhận (lỗi bị nuốt ở tầng realtime).
        try
        {
            await _realtime.SendChatAsync(request.ReceiverId, currentUserId, message, ct, row.MessageId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineWarning($"[ChatService] Không đẩy được realtime chat tới {request.ReceiverId}: {ex.GetType().Name} — {ex.Message}");
        }

        var dto = new ChatMessageDto(
            row.MessageId,
            currentUserId,
            request.ReceiverId,
            message,
            row.SentAt ?? DateTime.Now,
            IsMine: true);

        return new ChatSendResultDto(dto, peer);
    }

    /// <inheritdoc />
    public async Task<ChatPeerDto?> GetPeerAsync(int peerId, string peerKind, CancellationToken ct = default)
    {
        if (peerId <= 0)
        {
            return null;
        }

        var kind = peerKind?.Trim().ToLowerInvariant() ?? ChatRepositoryKind.Staff;

        var peers = kind == ChatRepositoryKind.Customer
            ? await _repository.GetCustomerPeersAsync(ct)
            : await _repository.GetStaffPeersAsync(ct);

        return peers.FirstOrDefault(p => p.PeerId == peerId && p.PeerKind == kind);
    }

    /// <summary>
    /// Suy ra loại của đối tác khi client không chỉ định rõ.
    /// </summary>
    /// <param name="peerKind">Loại do client truyền (có thể rỗng).</param>
    /// <param name="currentKind">Loại của người đang đăng nhập.</param>
    /// <returns>Loại của đối tác.</returns>
    private static string ResolvePeerKind(string? peerKind, string currentKind)
    {
        var requested = peerKind?.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(requested)
            && requested is ChatRepositoryKind.Staff
                or ChatRepositoryKind.Admin
                or ChatRepositoryKind.Doctor
                or ChatRepositoryKind.Customer)
        {
            return requested;
        }

        // Khách hàng chat với nhân viên; nhân viên chat với khách hàng.
        return currentKind == ChatRepositoryKind.Customer
            ? ChatRepositoryKind.Staff
            : ChatRepositoryKind.Customer;
    }
}
