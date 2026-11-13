namespace PetShop.Models;

/// <summary>
/// Vai trò người dùng trong hệ thống — nguồn sự thật duy nhất (single source of truth).
///
/// <para>
/// Vì sao cần file này:
/// Trước đây logic vai trò bị rải rác khắp các component với các chuỗi so sánh
/// lặp đi lặp lại (<c>"staff"</c>, <c>"admin"</c>, <c>"user"</c>, <c>"customer"</c>,
/// <c>"doctor"</c>) và mỗi chỗ tự quyết định so sánh <c>==</c> hay <c>!=</c>.
/// Kết quả là:
///   - <c>AuthGuard</c> và <c>CustomerOnlyGuard</c> trở thành hai bản sao của nhau.
///   - <c>AuthGuard</c> chặn staff khỏi trang khách nhưng lại KHÔNG chặn khách
///     khỏi trang staff ⇒ "vai trò bị phá ranh giới".
///   - Role <c>"customer"</c> (do <c>SessionHeaderAuthenticationHandler</c> gán mặc định)
///     và role <c>"user"</c> (do DB lưu) là hai tên khác nhau cho cùng một khách hàng.
/// </para>
///
/// <para>
/// Cách dùng: <see cref="Normalize"/> trả về chuẩn hoá về nhóm vai trò
/// (<see cref="Customer"/>, <see cref="Staff"/>, <see cref="Admin"/>, <see cref="Doctor"/>),
/// sau đó dùng <see cref="IsStaff"/> / <see cref="IsCustomer"/> để kiểm tra.
/// </para>
/// </summary>
public static class AppRoles
{
    /// <summary>Khách hàng — role ghi trong DB là <c>"user"</c>, header fallback là <c>"customer"</c>.</summary>
    public const string Customer = "customer";

    /// <summary>Nhân viên.</summary>
    public const string Staff = "staff";

    /// <summary>Quản trị viên.</summary>
    public const string Admin = "admin";

    /// <summary>Bác sĩ (role có trong DB nhưng chưa có workspace riêng).</summary>
    public const string Doctor = "doctor";

    /// <summary>
    /// Chuẩn hoá role bất kỳ về nhóm nhất quán.
    /// </summary>
    /// <remarks>
    /// - <c>null</c>/rỗng ⇒ <see cref="Customer"/> (fail-closed: coi như khách, an toàn hơn
    ///   là coi như staff rồi mở chung workspace).
    /// - <c>"user"</c>, <c>"customer"</c>, <c>"guest"</c> ⇒ <see cref="Customer"/>.
    /// - <c>"staff"</c>, <c>"employee"</c>, <c>"nv"</c> ⇒ <see cref="Staff"/>.
    /// - <c>"admin"</c>, <c>"administrator"</c>, <c>"manager"</c> ⇒ <see cref="Admin"/>.
    /// - <c>"doctor"</c>, <c>"bác sĩ"</c> ⇒ <see cref="Doctor"/>.
    /// </remarks>
    public static string Normalize(string? role)
    {
        var r = (role ?? string.Empty).Trim().ToLowerInvariant();

        return r switch
        {
            "" => Customer,
            "user" => Customer,
            "customer" => Customer,
            "guest" => Customer,
            "khách hàng" => Customer,

            "staff" => Staff,
            "employee" => Staff,
            "nv" => Staff,
            "nhân viên" => Staff,

            "admin" => Admin,
            "administrator" => Admin,
            "manager" => Admin,
            "quản trị" => Admin,

            "doctor" => Doctor,
            "bác sĩ" => Doctor,
            "bac si" => Doctor,

            _ => r,
        };
    }

    /// <summary>True nếu role thuộc nhóm nhân viên (nhân viên hoặc quản trị).</summary>
    public static bool IsStaff(string? role) => Normalize(role) is Staff or Admin;

    /// <summary>True nếu role là khách hàng.</summary>
    public static bool IsCustomer(string? role) => Normalize(role) == Customer;

    /// <summary>True nếu role có quyền vào workspace nhân viên (<c>/system/*</c>, <c>/revenue</c>, <c>/products/*</c> quản trị).</summary>
    public static bool CanAccessStaffWorkspace(string? role) => Normalize(role) is Staff or Admin or Doctor;

    /// <summary>True nếu role có quyền quản trị sản phẩm / kho.</summary>
    public static bool CanManageProducts(string? role) => Normalize(role) is Staff or Admin;

    /// <summary>
    /// Danh sách role được phép vào một trang (dùng cho guard).
    /// </summary>
    public static bool IsInAllowedRoles(string? role, params string[] allowedRoles)
    {
        var normalized = Normalize(role);
        foreach (var allowed in allowedRoles)
        {
            if (Normalize(allowed) == normalized)
            {
                return true;
            }
        }

        return false;
    }
}
