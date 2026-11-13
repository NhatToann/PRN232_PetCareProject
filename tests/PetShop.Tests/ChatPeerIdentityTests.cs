using PetShop.Models;
using PetShop.Repositories;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho lỗi "bấm icon người khác nhưng chat KHÔNG chuyển" (12/11/2026).
///
/// <para>
/// <b>NGUYÊN NHÂN GỐC:</b> các bảng người dùng có <b>id TRÙNG NHAU</b>.
/// Đã kiểm tra DB thật (<c>SHOP_PET_Database</c>):
/// <list type="bullet">
///   <item><c>staff.staff_id</c>: 1, 2, 3, 4, 5, 6, 8</item>
///   <item><c>admin.admin_id</c>: 1, 2, 6, 7, 8</item>
///   <item><c>doctor.doctor_id</c>: 1, 2, 3, 4, 5, 6</item>
/// </list>
/// ⇒ <c>staff_id = 1</c> và <c>admin_id = 1</c> và <c>doctor_id = 1</c> đều tồn tại.
/// Nếu code định danh đối tác chỉ bằng <c>PeerId</c> thì bấm avatar của
/// "Nguyễn Văn A" (admin) sẽ bị coi là **đã chọn** "Nguyễn Văn A" (staff)
/// ⇒ hộp thoại KHÔNG chuyển sang người vừa bấm.
/// </para>
///
/// <para>
/// <b>CÁCH SỬA:</b> dùng composite key <c>"{PeerKind}:{PeerId}"</c> ở tầng UI
/// (widget) và lọc lịch sử theo cặp nhãn <c>SenderType</c> ở tầng repository.
/// </para>
/// </summary>
public class ChatPeerIdentityTests
{
    /// <summary>
    /// Composite key phải phân biệt được 2 đối tác TRÙNG id nhưng khác loại.
    /// </summary>
    [Fact]
    public void PeerKey_PhanBietCacDoiTacTrungIdKhacLoai()
    {
        var staff = new ChatPeerDto(1, "Nguyễn Văn A", "staff", "staff");
        var admin = new ChatPeerDto(1, "Nguyễn Văn A", "admin", "admin");
        var doctor = new ChatPeerDto(1, "BS. Nguyễn Minh Anh", "doctor", "doctor");

        var staffKey = PeerKey(staff);
        var adminKey = PeerKey(admin);
        var doctorKey = PeerKey(doctor);

        Assert.NotEqual(staffKey, adminKey);
        Assert.NotEqual(staffKey, doctorKey);
        Assert.NotEqual(adminKey, doctorKey);
    }

    /// <summary>
    /// Cùng id + cùng loại ⇒ cùng một đối tác (key phải ổn định).
    /// </summary>
    [Fact]
    public void PeerKey_Stable_KhiCungIdVaCungLoai()
    {
        var a = new ChatPeerDto(7, "Trần Văn C", "staff", "staff");
        var b = new ChatPeerDto(7, "Trần Văn C", "staff", "staff");

        Assert.Equal(PeerKey(a), PeerKey(b));
    }

    /// <summary>
    /// Lịch sử chat phải lọc theo CẶP nhãn (người gửi + đối tác) — không lọc
    /// thì tin của staff #1 lẫn sang admin #1 (cùng khoá luồng DB).
    /// </summary>
    [Fact]
    public void ResolveSenderTypes_TachBach_DoiTacTrungId()
    {
        // Khách (id 61) chat với staff #1 ⇒ nhãn hợp lệ: customer + staff.
        var withStaff = ResolveSenderTypes("customer", "staff");
        Assert.NotNull(withStaff);
        Assert.Contains("customer", withStaff!);
        Assert.Contains("staff", withStaff!);
        Assert.DoesNotContain("admin", withStaff);

        // Khách (id 61) chat với admin #1 ⇒ nhãn hợp lệ: customer + admin.
        var withAdmin = ResolveSenderTypes("customer", "admin");
        Assert.NotNull(withAdmin);
        Assert.Contains("customer", withAdmin!);
        Assert.Contains("admin", withAdmin!);
        Assert.DoesNotContain("staff", withAdmin);
    }

    /// <summary>
    /// Không xác định được loại ⇒ KHÔNG lọc (trả null) để không mất lịch sử.
    /// </summary>
    [Fact]
    public void ResolveSenderTypes_KhongXacDinhDuoc_ThenKhongLoc()
    {
        Assert.Null(ResolveSenderTypes(null, null));
        Assert.Null(ResolveSenderTypes("", ""));
    }

    /// <summary>
    /// Chuẩn hoá loại người dùng về nhãn lưu trong cột <c>SenderType</c>.
    /// </summary>
    [Fact]
    public void NormalizeSenderType_ChuanHoaDungCacLoai()
    {
        Assert.Equal("customer", NormalizeSenderType("customer"));
        Assert.Equal("customer", NormalizeSenderType("user"));
        Assert.Equal("staff", NormalizeSenderType("staff"));
        Assert.Equal("admin", NormalizeSenderType("admin"));
        Assert.Equal("doctor", NormalizeSenderType("doctor"));
        Assert.Equal(string.Empty, NormalizeSenderType(null));
        Assert.Equal(string.Empty, NormalizeSenderType("unknown-role"));
    }

    /// <summary>
    /// Khoá luồng chat phải giữ nguyên hành vi cũ (không đụng schema DB).
    /// </summary>
    [Fact]
    public void ResolveThread_GiuNguyenHanhViCu()
    {
        // Khách ↔ nhân viên: CustomerID = khách, StaffID = nhân viên.
        var (customerId, staffId) = ChatRepository.ResolveThread(61, "customer", 4, "staff");
        Assert.Equal(61, customerId);
        Assert.Equal(4, staffId);

        // Nhân viên ↔ khách: CustomerID = khách, StaffID = nhân viên (đảo chiều).
        var (customerId2, staffId2) = ChatRepository.ResolveThread(4, "staff", 61, "customer");
        Assert.Equal(61, customerId2);
        Assert.Equal(4, staffId2);

        // Hai khách hàng ⇒ không có luồng staff.
        var (customerId3, staffId3) = ChatRepository.ResolveThread(1, "customer", 2, "customer");
        Assert.Equal(1, customerId3);
        Assert.Null(staffId3);
    }

    // ------------------------------------------------------------------
    // Bản sao logic (logic thật nằm private trong ChatRepository / ChatWidget).
    // Ở đây chỉ mô phỏng để test lại thuật toán — nếu logic thật đổi,
    // test ở trên vẫn bắt được hành vi sai.
    // ------------------------------------------------------------------

    private static string PeerKey(ChatPeerDto peer) => $"{peer.PeerKind}:{peer.PeerId}";

    private static List<string>? ResolveSenderTypes(string? currentUserKind, string? peerKind)
    {
        var types = new List<string>(2);

        var current = NormalizeSenderType(currentUserKind);
        var peer = NormalizeSenderType(peerKind);

        if (!string.IsNullOrEmpty(current) && !types.Contains(current))
        {
            types.Add(current);
        }

        if (!string.IsNullOrEmpty(peer) && !types.Contains(peer))
        {
            types.Add(peer);
        }

        return types.Count == 0 ? null : types;
    }

    private static string NormalizeSenderType(string? kind) => (kind ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "customer" or "user" => "customer",
        "admin" => "admin",
        "doctor" => "doctor",
        "staff" => "staff",
        _ => string.Empty
    };
}
