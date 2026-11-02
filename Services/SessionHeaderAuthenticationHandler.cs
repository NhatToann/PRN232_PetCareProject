using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PetShop.Services
{
    /// <summary>
    /// Tên scheme xác thực "nội bộ" dựa trên session của Blazor Server.
    /// Cùng cơ chế với <c>JwtBearer</c> để <c>[Authorize]</c> không quan tâm
    /// client đến từ trong app hay từ bên ngoài.
    /// </summary>
    public static class AuthSchemes
    {
        /// <summary>Session nội bộ (Blazor Server) + header X-User-Id / X-User-Role.</summary>
        public const string Session = "Session";

        /// <summary>JWT Bearer cho client API bên ngoài.</summary>
        public const string Bearer = "Bearer";

        /// <summary>
        /// Scheme tổng hợp: tự chuyển tiếp sang Bearer nếu request có header
        /// <c>Authorization: Bearer</c>, ngược lại dùng Session.
        /// </summary>
        public const string Default = "Default";
    }

    /// <summary>
    /// Xác thực dựa trên header nội bộ do Blazor tự gắn:
    /// <c>X-User-Id</c> và <c>X-User-Role</c>.
    ///
    /// Vì sao có handler này:
    /// App gọi **chính nó** qua HTTP (Blazor page → Service → HttpClient → Controller).
    /// Request đó không có Bearer token, nhưng vẫn phải được xác thực để
    /// các endpoint có <c>[Authorize]</c> chạy được trong chế độ nội bộ.
    ///
    /// Lưu ý: đây là cơ chế đơn giản, phù hợp với dự án chạy hoàn toàn LOCAL
    /// (xem AGENTS.md §0). Không phải là cơ chế bảo mật production.
    /// </summary>
    public sealed class SessionHeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string UserIdHeader = "X-User-Id";
        public const string UserRoleHeader = "X-User-Role";
        public const string CustomerIdHeader = "X-Customer-Id";

        private readonly ILogger<SessionHeaderAuthenticationHandler> _logger;

        public SessionHeaderAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory loggerFactory,
            UrlEncoder encoder,
            ILogger<SessionHeaderAuthenticationHandler> logger)
            : base(options, loggerFactory, encoder)
        {
            _logger = logger;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Ưu tiên Bearer token nếu client gửi kèm — JwtBearer handler sẽ xử lý,
            // nhưng nếu cả hai cùng hợp lệ thì session header vẫn là nguồn chính
            // cho các request nội bộ (self-HTTP).
            if (!Request.Headers.TryGetValue(UserIdHeader, out var userIdValues))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var rawUserId = userIdValues.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(rawUserId) ||
                !int.TryParse(rawUserId, out var userId) || userId <= 0)
            {
                return Task.FromResult(AuthenticateResult.Fail("Header X-User-Id không hợp lệ."));
            }

            var role = Request.Headers.TryGetValue(UserRoleHeader, out var roleValues)
                ? roleValues.FirstOrDefault()
                : null;

            if (string.IsNullOrWhiteSpace(role))
            {
                role = "customer";
            }

            var effectiveRole = role!;

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Name, effectiveRole),
                new(ClaimTypes.Role, effectiveRole)
            };

            // Ghi lại customer id để controller đọc được thông qua ClaimsPrincipal
            if (Request.Headers.TryGetValue(CustomerIdHeader, out var customerIdValues))
            {
                var rawCustomerId = customerIdValues.FirstOrDefault();
                if (int.TryParse(rawCustomerId, out var customerId) && customerId > 0)
                {
                    claims.Add(new Claim("customer_id", customerId.ToString()));
                }
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
