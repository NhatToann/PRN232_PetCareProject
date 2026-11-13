using System.Security.Claims;
using PetShop.Models;

namespace PetShop.Services;

/// <summary>
/// Tên các authorization policy dùng chung.
///
/// <para>
/// Đặt tên tập trung ở một chỗ để tránh sai chính tải chuỗi policy
/// (một lỗi rất dễ mắc: <c>RequireRole("Staff")</c> vs role lưu trong DB là
/// <c>"staff"</c> ⇒ mọi request bị 403 vô lý).
/// </para>
/// </summary>
public static class AuthPolicies
{
    /// <summary>Chỉ nhân viên / quản trị viên.</summary>
    public const string StaffOnly = "StaffOnly";

    /// <summary>Nhân viên hoặc quản trị viên (tương đương <c>StaffOnly</c> nhưng tên rõ nghĩa hơn).</summary>
    public const string StaffOrAdmin = "StaffOrAdmin";

    /// <summary>Chỉ khách hàng (role <c>customer</c> / <c>user</c>).</summary>
    public const string CustomerOnly = "CustomerOnly";
}

/// <summary>
/// Tiện ích đọc danh tính người dùng từ <see cref="ClaimsPrincipal"/>.
///
/// <para>
/// Vì sao cần: các endpoint API nhận <c>customerId</c> từ query string (được
/// client gửi lên) ⇒ dễ bị IDOR. Nay controller gọi <see cref="ResolveCustomerId"/>
/// để lấy id từ CLAIM (do <c>SessionHeaderAuthenticationHandler</c> đặt từ header
/// <c>X-Customer-Id</c> / <c>X-User-Id</c>), rồi so khớp với id trong DB.
/// </para>
/// </summary>
public static class ClaimExtensions
{
    /// <summary>
    /// Lấy CustomerId từ claim <c>customer_id</c>, fallback <c>NameIdentifier</c>.
    /// Trả về null nếu không xác định được.
    /// </summary>
    public static int? ResolveCustomerId(this ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        var raw = principal.FindFirst("customer_id")?.Value
                  ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }

    /// <summary>Lấy UserId từ claim <c>NameIdentifier</c>. Trả về null nếu không xác định được.</summary>
    public static int? ResolveUserId(this ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return null;
        }

        var raw = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, out var id) && id > 0 ? id : null;
    }

    /// <summary>Lấy role đã chuẩn hoá qua <see cref="AppRoles.Normalize"/>.</summary>
    public static string ResolveRole(this ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirst(ClaimTypes.Role)?.Value;
        return AppRoles.Normalize(raw);
    }
}
