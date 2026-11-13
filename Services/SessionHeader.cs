using PetShop.Models;

namespace PetShop.Services;

/// <summary>
/// Tiện ích gắn header phiên đăng nhập cho các self-HTTP client gọi chính API này.
///
/// <para>
/// <b>Vì sao không dùng <c>SpaAuthHeaderHandler</c> (DelegatingHandler)?</b><br/>
/// <c>DelegatingHandler</c> do <c>IHttpClientFactory</c> tạo ra nằm trong scope
/// RIÊNG của HttpClient (handler chain được cache 2 phút theo tên client).
/// Vì vậy handler <b>không</b> nhìn thấy <see cref="UserSessionService"/> scoped
/// của circuit Blazor ⇒ <c>CurrentUser</c> luôn là <c>null</c> ⇒ header không
/// được gắn ⇒ các endpoint có <c>[Authorize]</c> trả 401 hoặc rơi vào
/// nhánh "không xác thực" ⇒ dữ liệu sai (rất dễ thành "đăng nhập bằng tài khoản khác"
/// vì controller fallback về customerId trong query string).
/// </para>
///
/// <para>
/// Giải pháp: mỗi client tự inject <see cref="UserSessionService"/> (cùng scope với
/// component) và gắn header trực tiếp trước khi gửi — giống <c>PetsClient</c>
/// và <c>ChatClient</c> đã làm đúng.
/// </para>
/// </summary>
public static class SessionHeader
{
    /// <summary>Gắn <c>X-User-Id</c> / <c>X-Customer-Id</c> / <c>X-User-Role</c> nếu có user.</summary>
    public static HttpRequestMessage Attach(HttpRequestMessage request, AuthResponseDto? user)
    {
        if (user is null || user.UserId <= 0)
        {
            return request;
        }

        var userId = user.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (!request.Headers.Contains("X-User-Id"))
        {
            request.Headers.TryAddWithoutValidation("X-User-Id", userId);
        }

        if (!request.Headers.Contains("X-Customer-Id"))
        {
            request.Headers.TryAddWithoutValidation("X-Customer-Id", userId);
        }

        if (!request.Headers.Contains("X-User-Role") && !string.IsNullOrWhiteSpace(user.Role))
        {
            request.Headers.TryAddWithoutValidation("X-User-Role", user.Role);
        }

        return request;
    }
}
