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

    /// <summary>
    /// Hộp thư CHUNG của nhân viên — mọi nhân viên đều thấy tin của cùng 1 khách.
    ///
    /// <para>
    /// <b>VÌ SAO (13/11/2026):</b> trước đây mỗi nhân viên là 1 luồng riêng với
    /// khách ⇒ khách phải chọn "gửi cho ai" ⇒ rối rắm (18+ nhân viên trong
    /// 1 thanh icon). Nay mỗi khách có <b>1 hộp thư</b> ⇒ khách chỉ cần chọn
    /// "AI" hoặc "Nhân viên"; nhân viên mở hộp thư của khách là thấy <b>toàn bộ</b>
    /// lịch sử (kể cả tin nhân viên khác gửi).
    /// </para>
    /// </summary>
    public const string KindPool = "pool";

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

        // ⚠️ HỘP THƯ CHUNG (13/11/2026) — KHÔNG lọc theo StaffID nữa.
        //
        // VÌ SAO:
        //   Trước đây khoá luồng = (CustomerID, StaffID) ⇒ mỗi nhân viên có 1
        //   luồng riêng với khách ⇒ nhân viên A KHÔNG thấy tin nhân viên B đã
        //   trả lời ⇒ khách phải "chọn đúng người" mới không bị bỏ rơi.
        //   Nay: 1 khách = 1 hộp thư ⇒ mọi nhân viên đều đọc/ghi cùng 1 tập tin
        //   ⇒ lọc DUY NHẤT theo CustomerID.
        var customerId = ResolvePoolCustomerId(currentUserId, currentUserKind, peerId, peerKind);

        if (customerId <= 0)
        {
            return Array.Empty<ChatMessageDto>();
        }

        var query = _db.ChatMessages
            .AsNoTracking()
            .Where(m => m.CustomerId == customerId);

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

    /// <summary>
    /// Xác định id khách hàng mà luồng chat hiện tại thuộc về.
    ///
    /// <para>
    /// Hộp thư chung ⇒ khoá luồng chỉ còn <b>CustomerID</b>:
    /// khách xem hộp thư của chính mình; nhân viên xem hộp thư của khách đang mở.
    /// </para>
    /// </summary>
    /// <param name="currentUserId">Id người đang đăng nhập.</param>
    /// <param name="currentUserKind">Loại người đang đăng nhập.</param>
    /// <param name="peerId">Id đối tác (khách khi nhân viên mở hộp thư).</param>
    /// <param name="peerKind">Loại đối tác.</param>
    /// <returns>Id khách của hộp thư, hoặc <c>0</c> nếu không xác định được.</returns>
    public static int ResolvePoolCustomerId(
        int currentUserId,
        string currentUserKind,
        int peerId,
        string peerKind)
    {
        var userIsCustomer = IsCustomerKind(currentUserKind);
        var peerIsCustomer = IsCustomerKind(peerKind) || string.Equals(peerKind, KindPool, StringComparison.OrdinalIgnoreCase);

        if (userIsCustomer)
        {
            // Khách xem hộp thư của chính mình.
            return currentUserId;
        }

        if (peerIsCustomer)
        {
            // Nhân viên xem hộp thư của khách đang mở.
            return peerId;
        }

        // Nhân viên ↔ nhân viên: không có khách trong luồng ⇒ không hợp lệ.
        return 0;
    }

    /// <summary>
    /// Chuẩn hoá loại người dùng về nhãn lưu trong cột <c>SenderType</c> /
    /// <c>SenderKind</c>.
    ///
    /// <para>
    /// Trả <c>null</c> nếu không nhận diện được (khác với bản cũ trả chuỗi rỗng)
    /// ⇒ gọi chồng 2 nguồn (cột mới + cột cũ) mà vẫn fallback đúng.
    /// </para>
    /// </summary>
    private static string? NormalizeSenderType(string? kind) => (kind ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        KindCustomer or "user" => KindCustomer,
        KindAdmin => KindAdmin,
        KindDoctor => KindDoctor,
        KindStaff => KindStaff,
        _ => null
    };

    /// <inheritdoc />
    public async Task<ChatMessage> AddMessageAsync(
        int customerId,
        int? staffId,
        string senderType,
        string message,
        string? senderName = null,
        string? senderKind = null,
        CancellationToken ct = default)
    {
        // ⚠️ Cột `SenderType` bị CHECK constraint giới hạn CHỈ cho phép
        // 'Staff' / 'Customer' ⇒ chuẩn hoá mọi nhãn nhân viên (staff / admin /
        // doctor) về "staff" để không bị DB từ chối. Nhãn đầy đủ nằm ở cột
        // `SenderKind` (thêm mới cho hộp thư chung).
        var normalizedSenderType = string.Equals(
            senderType?.Trim().ToLowerInvariant(),
            KindCustomer,
            StringComparison.Ordinal)
            ? KindCustomer
            : KindStaff;

        var entity = new ChatMessage
        {
            CustomerId = customerId,
            StaffId = staffId,
            SenderType = normalizedSenderType,
            SenderName = senderName,
            SenderKind = senderKind,
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
        // ⚠️ Không dùng projection có tham số optional của `ChatPeerDto` trong
        // expression tree (CS0854) ⇒ truyền ĐỦ tham số.
        var staff = await _db.Staff
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new ChatPeerDto(
                s.StaffId,
                string.IsNullOrWhiteSpace(s.Name) ? $"Nhân viên #{s.StaffId}" : s.Name,
                KindStaff,
                KindStaff,
                null,
                null))
            .ToListAsync(ct);

        var admins = await _db.Admins
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new ChatPeerDto(
                a.AdminId,
                string.IsNullOrWhiteSpace(a.Name) ? a.Username : a.Name!,
                KindAdmin,
                KindAdmin,
                null,
                null))
            .ToListAsync(ct);

        var doctors = await _db.Doctors
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new ChatPeerDto(
                d.DoctorId,
                string.IsNullOrWhiteSpace(d.Name) ? $"Bác sĩ #{d.DoctorId}" : d.Name,
                KindDoctor,
                KindDoctor,
                null,
                null))
            .ToListAsync(ct);

        return staff.Concat(admins).Concat(doctors).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatPeerDto>> GetCustomerPeersAsync(CancellationToken ct = default)
    {
        // HỘP THƯ CHUNG (13/11/2026): nhân viên thấy DANH SÁCH KHÁCH đang có
        // tin nhắn, mới nhất lên đầu. Khách chưa từng nhắn thì chưa có hộp thư
        // ⇒ không hiện (tránh 60+ khách vô nghĩa trong thanh chọn).
        var lastMessageAt = await _db.ChatMessages
            .AsNoTracking()
            .GroupBy(m => m.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                LastMessageAt = g.Max(m => m.SentAt)
            })
            .ToDictionaryAsync(x => x.CustomerId, x => x.LastMessageAt, ct);

        var customers = await _db.Customers
            .AsNoTracking()
            .OrderBy(c => c.Name)
            // Lấy kèm avatar_url (cột duy nhất trong các bảng người dùng đang có
            // ảnh đại diện) ⇒ widget chat hiển thị được ảnh thật của khách.
            .Select(c => new ChatPeerDto(
                c.CustomerId,
                string.IsNullOrWhiteSpace(c.Name) ? $"Khách #{c.CustomerId}" : c.Name!,
                KindCustomer,
                KindCustomer,
                c.AvatarUrl,
                null))
            .ToListAsync(ct);

        return customers
            .Where(c => lastMessageAt.ContainsKey(c.PeerId))
            .OrderByDescending(c => lastMessageAt[c.PeerId])
            .Select(c => c with { LastMessageAt = lastMessageAt[c.PeerId] })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<string?> GetStaffNameAsync(int staffId, CancellationToken ct = default)
    {
        if (staffId <= 0)
        {
            return null;
        }

        return await _db.Staff
            .AsNoTracking()
            .Where(s => s.StaffId == staffId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<string?> GetAdminNameAsync(int adminId, CancellationToken ct = default)
    {
        if (adminId <= 0)
        {
            return null;
        }

        return await _db.Admins
            .AsNoTracking()
            .Where(a => a.AdminId == adminId)
            .Select(a => a.Name ?? a.Username)
            .FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<string?> GetDoctorNameAsync(int doctorId, CancellationToken ct = default)
    {
        if (doctorId <= 0)
        {
            return null;
        }

        return await _db.Doctors
            .AsNoTracking()
            .Where(d => d.DoctorId == doctorId)
            .Select(d => d.Name)
            .FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<string?> GetCustomerNameAsync(int customerId, CancellationToken ct = default)
    {
        if (customerId <= 0)
        {
            return null;
        }

        return await _db.Customers
            .AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .Select(c => c.Name)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Chuẩn hoá một cặp (người dùng, đối tác) về khoá luồng chat
    /// <c>(CustomerID, StaffID)</c> trong bảng <c>ChatMessages</c>.
    ///
    /// <para>
    /// <b>⚠️ LƯU Ý (13/11/2026):</b> hộp thư CHUNG đã thay luồng 1-1 ⇒
    /// <see cref="GetHistoryAsync"/> KHÔNG còn dùng hàm này nữa (dùng
    /// <see cref="ResolvePoolCustomerId"/>). Hàm được giữ lại cho các test
    /// cũ + để thống nhất cách hiểu khoá luồng.
    /// </para>
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
    ///
    /// <param name="row">Dòng dữ liệu gốc.</param>
    /// <param name="currentUserId">Id người đang đăng nhập.</param>
    /// <param name="currentUserKind">Loại người đang đăng nhập.</param>
    private static ChatMessageDto MapMessage(ChatMessage row, int currentUserId, string currentUserKind)
    {
        // ⚠️ HỘP THƯ CHUNG (13/11/2026): dùng `SenderKind` (nhãn loại người gửi
        // thật, đầy đủ: staff / admin / doctor / customer) thay vì suy đoán từ
        // `StaffId` — vì id các bảng người dùng trùng nhau (`staff_id = 1` ≡
        // `admin_id = 1` ≡ `doctor_id = 1`) nên chỉ nhìn id thì không phân biệt
        // được tin của ai.
        //
        // Tin cũ (trước 13/11/2026) chưa có `SenderKind` ⇒ fallback về
        // `SenderType` (chỉ có `staff` / `customer`).
        var senderKind = NormalizeSenderType(row.SenderKind)
            ?? NormalizeSenderType(row.SenderType)
            ?? string.Empty;

        var currentKind = NormalizeSenderType(currentUserKind) ?? string.Empty;

        // Id người gửi / người nhận theo không gian id của vai trò tương ứng.
        var (senderId, receiverId) = string.Equals(senderKind, KindCustomer, StringComparison.Ordinal)
            ? (row.CustomerId, row.StaffId ?? 0)
            : (row.StaffId ?? 0, row.CustomerId);

        // Tin của chính mình: cùng LOẠI người dùng + cùng id.
        var isMine = string.Equals(senderKind, currentKind, StringComparison.Ordinal)
            && (string.Equals(senderKind, KindCustomer, StringComparison.Ordinal)
                ? row.CustomerId == currentUserId
                : (row.StaffId ?? 0) == currentUserId);

        return new ChatMessageDto(
            row.MessageId,
            senderId,
            receiverId,
            row.Message ?? string.Empty,
            row.SentAt ?? DateTime.MinValue,
            isMine,
            row.SenderName ?? string.Empty,
            senderKind,
            senderKind);
    }
}
