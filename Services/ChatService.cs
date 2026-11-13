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

        // ⚠️ HỘP THƯ CHUNG (13/11/2026):
        //   Khách hàng KHÔNG cần danh sách nhân viên nữa (chỉ chọn "AI" hoặc
        //   "Nhân viên") ⇒ trả về danh sách RỖNG cho khách (widget tự dựng 2 nút).
        //   Nhân viên ⇒ danh sách KHÁCH có hộp thư (mới nhất lên đầu).
        return kind == ChatRepositoryKind.Customer
            ? Array.Empty<ChatPeerDto>()
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

        var currentKind = IChatService.NormalizeKind(currentRole);
        var receiverKind = ResolvePeerKind(request.ReceiverKind, currentKind);

        // Khách gửi vào HỘP THƯ CHUNG của nhân viên (không chọn người nhận).
        var isCustomer = currentKind == ChatRepositoryKind.Customer;
        var goesToPool = isCustomer && IsPoolTarget(request.ReceiverId, request.ReceiverKind);

        // Khách hàng không được gửi vào hộp thư của khách khác.
        if (!isCustomer && !goesToPool && !ChatRepository.IsCustomerKind(receiverKind))
        {
            throw new ArgumentException("Người nhận không hợp lệ.", nameof(request));
        }

        if (goesToPool)
        {
            // Khách gửi ⇒ luồng thuộc về chính khách, không gắn StaffID.
            return await SendToPoolAsync(currentUserId, currentKind, message, ct);
        }

        // Nhân viên gửi cho 1 khách ⇒ hộp thư chung của khách đó.
        var customerId = ChatRepository.ResolvePoolCustomerId(currentUserId, currentKind, request.ReceiverId, receiverKind);

        if (customerId <= 0)
        {
            throw new ArgumentException("Người nhận không hợp lệ.", nameof(request));
        }

        // Tra cứu thông tin khách (tên + avatar) để dựng bubble cho khách.
        var peer = await GetPeerAsync(customerId, ChatRepositoryKind.Customer, ct)
            ?? new ChatPeerDto(customerId, $"Khách #{customerId}", ChatRepositoryKind.Customer, ChatRepositoryKind.Customer);

        var senderName = await ResolveSenderNameAsync(currentUserId, currentKind, ct);

        // Nhãn LOẠI người gửi thật (sửa bug 12/11/2026).
        //
        // ⚠️ Trước đây ghi "staff" cho MỌI người dùng không phải khách ⇒ admin
        // và doctor đều bị ghi chung là "staff" ⇒ không tách được các đối tác
        // có cùng id (staff_id = admin_id = doctor_id = 1). Nay ghi đúng loại
        // (`staff` / `admin` / `doctor` / `customer`) ⇒ lọc lịch sử theo cặp
        // nhãn là duy nhất.
        //
        // ⚠️ NHƯNG cột `SenderType` bị CHECK constraint giới hạn CHỈ cho phép
        // 'Staff' / 'Customer' ⇒ nếu ghi "admin"/"doctor" sẽ bị DB từ chối.
        // ⇒ Cột này chỉ ghi "staff" | "customer"; nhãn đầy đủ nằm ở cột
        // `SenderKind` (thêm mới) — xem `AddMessageAsync`.
        var senderType = currentKind == ChatRepositoryKind.Customer
            ? ChatRepositoryKind.Customer
            : ChatRepositoryKind.Staff;

        // 1) Ghi DB trước ⇒ tin chắc chắn được lưu kể cả khi realtime lỗi.
        var row = await _repository.AddMessageAsync(
            customerId,
            currentUserId,
            senderType,
            message,
            senderName,
            currentKind,
            ct);

        // 2) Đẩy realtime tới người nhận (lỗi bị nuốt ở tầng realtime).
        //
        // ⚠️ Truyền kèm `fromKind` + `toKind` (12/11/2026): id các bảng người
        // dùng TRÙNG NHAU (staff_id = 1, admin_id = 1, doctor_id = 1 đều tồn
        // tại) ⇒ client chỉ nhận id sẽ không biết tin đến từ ai ⇒ mở nhầm
        // hộp thoại. Kèm loại người dùng ⇒ client ghép đúng (id + kind).
        try
        {
            await _realtime.SendChatAsync(
                customerId,
                currentUserId,
                message,
                ct,
                row.MessageId,
                currentKind,
                ChatRepositoryKind.Customer,
                customerId,
                senderName,
                currentKind);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineWarning($"[ChatService] Không đẩy được realtime chat tới {customerId}: {ex.GetType().Name} — {ex.Message}");
        }

        var dto = new ChatMessageDto(
            row.MessageId,
            currentUserId,
            customerId,
            message,
            row.SentAt ?? DateTime.Now,
            IsMine: true,
            senderName,
            currentKind,
            currentKind);

        return new ChatSendResultDto(dto, peer);
    }

    /// <summary>
    /// Khách gửi tin vào HỘP THƯ CHUNG của nhân viên.
    ///
    /// <para>
    /// <b>VÌ SAO Tách riêng (13/11/2026):</b> khách không chọn người nhận ⇒
    /// không có "peer" cụ thể ⇒ tin được ghi với <c>CustomerID</c> = chính khách
    /// và <c>StaffID</c> = <c>null</c> ⇒ MỌI nhân viên đều thấy tin này khi mở
    /// hộp thư của khách.
    /// </para>
    /// </summary>
    private async Task<ChatSendResultDto> SendToPoolAsync(
        int currentUserId,
        string currentKind,
        string message,
        CancellationToken ct)
    {
        var customerId = currentUserId;
        var senderName = await ResolveSenderNameAsync(currentUserId, currentKind, ct);

        var row = await _repository.AddMessageAsync(
            customerId,
            null,
            ChatRepositoryKind.Customer,
            message,
            senderName,
            currentKind,
            ct);

        // Đẩy realtime tới KHÁCH (để khách thấy tin của mình qua SignalR)
        // + tới NHÓM NHÂN VIÊN (để mọi nhân viên thấy ngay).
        try
        {
            await _realtime.SendChatAsync(
                customerId,
                currentUserId,
                message,
                ct,
                row.MessageId,
                currentKind,
                ChatRepositoryKind.Customer,
                customerId,
                senderName,
                currentKind);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineWarning($"[ChatService] Không đẩy được realtime chat của khách {customerId}: {ex.GetType().Name} — {ex.Message}");
        }

        var peer = await GetPeerAsync(customerId, ChatRepositoryKind.Customer, ct)
            ?? new ChatPeerDto(customerId, $"Khách #{customerId}", ChatRepositoryKind.Customer, ChatRepositoryKind.Customer);

        var dto = new ChatMessageDto(
            row.MessageId,
            currentUserId,
            0,
            message,
            row.SentAt ?? DateTime.Now,
            IsMine: true,
            senderName,
            currentKind,
            currentKind);

        return new ChatSendResultDto(dto, peer);
    }

    /// <summary>
    /// Tra tên hiển thị của người gửi theo loại tài khoản.
    ///
    /// <para>
    /// Cần cho hộp thư chung: tin của mỗi nhân viên phải hiện TÊN người gửi
    /// (khách không biết "Nguyễn Văn A" là staff hay admin).
    /// </para>
    /// </summary>
    private async Task<string> ResolveSenderNameAsync(int userId, string kind, CancellationToken ct)
    {
        try
        {
            return kind switch
            {
                ChatRepositoryKind.Admin => await _repository.GetAdminNameAsync(userId, ct) ?? "Nhân viên",
                ChatRepositoryKind.Doctor => await _repository.GetDoctorNameAsync(userId, ct) ?? "Bác sĩ",
                ChatRepositoryKind.Staff => await _repository.GetStaffNameAsync(userId, ct) ?? "Nhân viên",
                _ => await _repository.GetCustomerNameAsync(userId, ct) ?? "Khách hàng"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineWarning($"[ChatService] Không lấy được tên người gửi (id={userId}, kind={kind}): {ex.GetType().Name}");
            return kind == ChatRepositoryKind.Customer ? "Khách hàng" : "Nhân viên";
        }
    }

    /// <summary>
    /// Kiểm tra khách có đang gửi vào hộp thư chung của nhân viên hay không.
    ///
    /// <para>
    /// Nhận diện qua <c>receiverKind = "pool"</c> ⇒ không cần chọn người nhận.
    /// </para>
    /// </summary>
    private static bool IsPoolTarget(int receiverId, string? receiverKind)
    {
        var kind = (receiverKind ?? string.Empty).Trim().ToLowerInvariant();
        return kind == ChatRepositoryKind.Pool;
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
    ///
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
                or ChatRepositoryKind.Customer
                or ChatRepositoryKind.Pool)
        {
            return requested;
        }

        // Khách hàng chat với nhân viên; nhân viên chat với khách hàng.
        return currentKind == ChatRepositoryKind.Customer
            ? ChatRepositoryKind.Staff
            : ChatRepositoryKind.Customer;
    }
}
