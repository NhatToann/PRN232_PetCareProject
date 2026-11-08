using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Repositories;

/// <summary>
/// Truy vấn / ghi lịch sử chat 1-1 trên bảng <c>ChatMessages</c> (Q21).
///
/// <para>
/// Tuân thủ 3-layer: Component Razor → <see cref="IChatService"/> → interface
/// này → <c>ShopPetDatabaseContext</c>. Component <b>không</b> được inject
/// DbContext.
/// </para>
///
/// <para>
/// Xem <see cref="IChatRepository"/> để mô tả chi tiết cách ánh xạ giữa bảng
/// <c>ChatMessages</c> và các bảng người dùng (<c>Staff</c> / <c>Admin</c> /
/// <c>Doctor</c> / <c>Customer</c>).
/// </para>
/// </summary>
public sealed class ChatRepository : IChatRepository
{
    /// <summary>Loại người dùng: nhân viên (bảng <c>Staff</c>).</summary>
    public const string KindStaff = "staff";

    /// <summary>Loại người dùng: quản trị viên (bảng <c>Admin</c>).</summary>
    public const string KindAdmin = "admin";

    /// <summary>Loại người dùng: bác sĩ (bảng <c>Doctor</c>).</summary>
    public const string KindDoctor = "doctor";

    /// <summary>Loại người dùng: khách hàng (bảng <c>Customer</c>).</summary>
    public const string KindCustomer = "customer";

    private readonly ShopPetDatabaseContext _db;

    public ChatRepository(ShopPetDatabaseContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatMessageDto>> GetHistoryAsync(
        int currentUserId,
        string currentUserKind,
        int peerId,
        string peerKind,
        int take = 100,
        CancellationToken ct = default)
    {
        if (currentUserId <= 0 || peerId <= 0)
        {
            return Array.Empty<ChatMessageDto>();
        }

        var limit = Math.Clamp(take, 1, 200);

        // Chuẩn hoá về cặp (customerId, staffId) của luồng chat để hai chiều
        // của cùng một cuộc hội thoại đọc được lịch sử giống nhau.
        var (customerId, staffId) = ResolveThread(currentUserId, currentUserKind, peerId, peerKind);

        var query = _db.ChatMessages
            .AsNoTracking()
            .Where(m => m.CustomerId == customerId);

        if (staffId.HasValue)
        {
            query = query.Where(m => m.StaffId == staffId.Value);
        }
        else
        {
            // Không có id nhân viên xác định (ví dụ đối tác là admin/doctor)
            // ⇒ chỉ lấy các tin của khách + các tin do nhân viên gửi trong
            // luồng mà chưa gắn id nhân viên.
            query = query.Where(m => m.StaffId == null);
        }

        var rows = await query
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.MessageId)
            .Take(limit)
            .ToListAsync(ct);

        // Đảo lại để khung chat hiển thị theo thứ tự thời gian tăng dần.
        rows.Reverse();

        return rows
            .Select(m => MapMessage(m, currentUserId, currentUserKind))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ChatMessage> AddMessageAsync(
        int customerId,
        int? staffId,
        string senderType,
        string message,
        CancellationToken ct = default)
    {
        var entity = new ChatMessage
        {
            CustomerId = customerId,
            StaffId = staffId,
            SenderType = senderType,
            Message = message,
            SentAt = DateTime.Now,
            IsRead = false,
        };

        _db.ChatMessages.Add(entity);
        await _db.SaveChangesAsync(ct);

        return entity;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatPeerDto>> GetStaffPeersAsync(CancellationToken ct = default)
    {
        var staff = await _db.Staff
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new ChatPeerDto(
                s.StaffId,
                string.IsNullOrWhiteSpace(s.Name) ? $"Nhân viên #{s.StaffId}" : s.Name,
                KindStaff,
                KindStaff))
            .ToListAsync(ct);

        var admins = await _db.Admins
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new ChatPeerDto(
                a.AdminId,
                string.IsNullOrWhiteSpace(a.Name) ? a.Username : a.Name!,
                KindAdmin,
                KindAdmin))
            .ToListAsync(ct);

        var doctors = await _db.Doctors
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new ChatPeerDto(
                d.DoctorId,
                string.IsNullOrWhiteSpace(d.Name) ? $"Bác sĩ #{d.DoctorId}" : d.Name,
                KindDoctor,
                KindDoctor))
            .ToListAsync(ct);

        return staff.Concat(admins).Concat(doctors).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatPeerDto>> GetCustomerPeersAsync(CancellationToken ct = default)
    {
        return await _db.Customers
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new ChatPeerDto(
                c.CustomerId,
                string.IsNullOrWhiteSpace(c.Name) ? $"Khách #{c.CustomerId}" : c.Name!,
                KindCustomer,
                KindCustomer))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Chuẩn hoá một cặp (người dùng, đối tác) về khoá luồng chat
    /// <c>(CustomerID, StaffID)</c> trong bảng <c>ChatMessages</c>.
    /// </summary>
    /// <param name="userId">Id người dùng hiện tại.</param>
    /// <param name="userKind">Loại người dùng hiện tại.</param>
    /// <param name="peerId">Id đối tác.</param>
    /// <param name="peerKind">Loại đối tác.</param>
    /// <returns>Tuple <c>(customerId, staffId)</c>.</returns>
    public static (int CustomerId, int? StaffId) ResolveThread(
        int userId,
        string userKind,
        int peerId,
        string peerKind)
    {
        var userIsCustomer = IsCustomerKind(userKind);
        var peerIsCustomer = IsCustomerKind(peerKind);

        if (userIsCustomer && peerIsCustomer)
        {
            // Hai khách hàng — không có luồng staff ⇒ gắn theo khách gửi.
            return (userId, null);
        }

        if (userIsCustomer)
        {
            // Khách ↔ nhân viên: CustomerID = khách, StaffID = nhân viên.
            return (userId, peerId);
        }

        if (peerIsCustomer)
        {
            // Nhân viên ↔ khách: CustomerID = khách, StaffID = nhân viên.
            return (peerId, userId);
        }

        // Nhân viên ↔ nhân viên: gắn theo người gửi (không có khách trong luồng).
        return (userId, null);
    }

    /// <summary>Kiểm tra loại người dùng có phải khách hàng hay không.</summary>
    public static bool IsCustomerKind(string? kind) =>
        string.Equals(kind, KindCustomer, StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, "user", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ánh xạ một dòng bảng <c>ChatMessages</c> sang DTO trả về cho client.
    /// </summary>
    /// <param name="row">Dòng dữ liệu gốc.</param>
    /// <param name="currentUserId">Id người đang đăng nhập.</param>
    /// <param name="currentUserKind">Loại người đang đăng nhập.</param>
    private static ChatMessageDto MapMessage(ChatMessage row, int currentUserId, string currentUserKind)
    {
        var senderIsCustomer = string.Equals(row.SenderType, KindCustomer, StringComparison.OrdinalIgnoreCase);

        // Id người gửi / người nhận theo không gian id của vai trò tương ứng.
        var (senderId, receiverId) = senderIsCustomer
            ? (row.CustomerId, row.StaffId ?? 0)
            : (row.StaffId ?? 0, row.CustomerId);

        var isMine = senderIsCustomer
            ? IsCustomerKind(currentUserKind) && row.CustomerId == currentUserId
            : !IsCustomerKind(currentUserKind) && (row.StaffId ?? 0) == currentUserId;

        return new ChatMessageDto(
            row.MessageId,
            senderId,
            receiverId,
            row.Message ?? string.Empty,
            row.SentAt ?? DateTime.MinValue,
            isMine);
    }
}
