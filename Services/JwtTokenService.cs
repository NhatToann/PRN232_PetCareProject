using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PetShop.Models;

namespace PetShop.Services
{
    /// <summary>
    /// Cấu hình sinh / kiểm tra JWT Bearer token.
    /// </summary>
    public sealed class JwtSettings
    {
        public const string SectionName = "JwtSettings";

        /// <summary>Issuer của token (tên ứng dụng phát hành).</summary>
        public string Issuer { get; set; } = "PetShop";

        /// <summary>Audience của token (ứng dụng / client được phép nhận).</summary>
        public string Audience { get; set; } = "PetShopClients";

        /// <summary>
        /// Khoá ký HMAC-SHA256. Bắt buộc có ít nhất 32 byte cho thuật toán HS256.
        /// Trong dự án local, đọc từ <c>appsettings.Development.json</c> hoặc User Secrets.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Thời gian sống của access token (phút).</summary>
        public int ExpiresInMinutes { get; set; } = 120;

        /// <summary>
        /// Thời gian sống của refresh token (phút). Mặc định 10 080 phút = 7 ngày.
        ///
        /// Slide PRN232 Ch06 S40 yêu cầu cấu hình trong <c>appsettings.json</c> để tạo
        /// **cả access token và refresh token** — tách 2 mốc hạn riêng biệt.
        /// Key cũ (<c>ExpiresInMinutes</c>) giữ nguyên để không phá config hiện có.
        /// </summary>
        public int RefreshTokenExpiresInMinutes { get; set; } = 10080;
    }

    /// <summary>
    /// Phát hành và kiểm tra JWT Bearer token cho các client API bên ngoài.
    ///
    /// Hệ thống xác thực của dự án là **dual-mode**:
    ///   1. Blazor Server (nội bộ) — dùng session + header <c>X-User-Id</c>/<c>X-User-Role</c>.
    ///   2. Client API bên ngoài — dùng <c>Authorization: Bearer &lt;token&gt;</c>.
    ///
    /// Cả hai cùng tồn tại; endpoint nào cần bảo vệ thì thêm <c>[Authorize]</c>.
    ///
    /// Ngoài access token, service còn phát hành **refresh token** theo slide
    /// PRN232 Ch06 S40–S42 (login trả về cả 2 token; client gửi refresh token
    /// tới endpoint refresh để lấy access token mới).
    /// </summary>
    public interface IJwtTokenService
    {
        /// <summary>Sinh access token cho một người dùng đã xác thực.</summary>
        string CreateToken(int userId, string name, string email, string role);

        /// <summary>Sinh access token kèm thời hạn tuỳ biến.</summary>
        string CreateToken(int userId, string name, string email, string role, TimeSpan lifetime);

        /// <summary>
        /// Sinh **refresh token** (slide Ch06 S40) — cùng key/issuer/audience với access token,
        /// nhưng thời hạn dài hơn và mang claim <c>token_type = "refresh"</c> để phân biệt.
        /// </summary>
        string CreateRefreshToken(int userId, string name, string email, string role);

        /// <summary>Đọc các claim từ token (không xác thực chữ ký) — dùng để hiển thị/log.</summary>
        ClaimsPrincipal? GetPrincipalFromToken(string token);

        /// <summary>Kiểm tra chữ ký + hạn của token.</summary>
        bool ValidateToken(string token, out ClaimsPrincipal? principal);

        /// <summary>
        /// Kiểm tra token có phải **refresh token** hợp lệ không (chữ ký + hạn + claim <c>token_type</c>).
        ///
        /// Access token <b>không</b> được dùng để refresh ⇒ claim <c>token_type</c> phải
        /// đúng là <see cref="JwtTokenService.TokenTypeRefresh"/>.
        /// </summary>
        bool ValidateRefreshToken(string token, out ClaimsPrincipal? principal);

        /// <summary>
        /// Kiểm tra token có phải **access token** hợp lệ không (chữ ký + hạn + claim <c>token_type</c>).
        /// Refresh token không được dùng như access token.
        /// </summary>
        bool ValidateAccessToken(string token, out ClaimsPrincipal? principal);
    }

    public sealed class JwtTokenService : IJwtTokenService
    {
        /// <summary>Giá trị claim phân biệt access token với refresh token.</summary>
        public const string TokenTypeClaim = "token_type";

        /// <summary>Giá trị claim của access token.</summary>
        public const string TokenTypeAccess = "access";

        /// <summary>Giá trị claim của refresh token (slide Ch06 S40).</summary>
        public const string TokenTypeRefresh = "refresh";

        /// <summary>Thời hạn refresh token mặc định khi config không hợp lệ (7 ngày).</summary>
        private const int DefaultRefreshTokenExpiresInMinutes = 10080;

        private readonly JwtSettings _settings;
        private readonly ILogger<JwtTokenService> _logger;

        public JwtTokenService(IOptions<JwtSettings> options, ILogger<JwtTokenService> logger)
        {
            _settings = options.Value;
            _logger = logger;
        }

        /// <summary>Khoá ký đã resolve — tính một lần để tránh lặp trong mỗi request.</summary>
        private SymmetricSecurityKey SigningKey
        {
            get
            {
                var key = _settings.Key ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException(
                        "Chưa cấu hình JwtSettings:Key. Hãy đặt khoá ký (tối thiểu 32 ký tự) trong appsettings.Development.json hoặc User Secrets.");
                }

                var bytes = Encoding.UTF8.GetBytes(key);
                if (bytes.Length < 32)
                {
                    throw new InvalidOperationException(
                        "JwtSettings:Key phải có ít nhất 32 ký tự để dùng thuật toán HMAC-SHA256.");
                }

                return new SymmetricSecurityKey(bytes);
            }
        }

        public string CreateToken(int userId, string name, string email, string role)
        {
            var minutes = _settings.ExpiresInMinutes > 0 ? _settings.ExpiresInMinutes : 120;
            return CreateToken(userId, name, email, role, TimeSpan.FromMinutes(minutes));
        }

        public string CreateToken(int userId, string name, string email, string role, TimeSpan lifetime)
            => BuildToken(userId, name, email, role, lifetime, TokenTypeAccess);

        public string CreateRefreshToken(int userId, string name, string email, string role)
        {
            var minutes = _settings.RefreshTokenExpiresInMinutes > 0
                ? _settings.RefreshTokenExpiresInMinutes
                : DefaultRefreshTokenExpiresInMinutes;

            return BuildToken(userId, name, email, role, TimeSpan.FromMinutes(minutes), TokenTypeRefresh);
        }

        /// <summary>
        /// Dựng JWT với claim <c>token_type</c> để phân biệt access / refresh token.
        /// Cùng key ký, issuer và audience ⇒ client có thể dùng chung 1 hệ thống xác thực.
        /// </summary>
        private string BuildToken(
            int userId,
            string name,
            string email,
            string role,
            TimeSpan lifetime,
            string tokenType)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Name, name ?? string.Empty),
                new(ClaimTypes.Email, email ?? string.Empty),
                new(ClaimTypes.Role, role ?? string.Empty),
                new(TokenTypeClaim, tokenType),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var now = DateTime.UtcNow;
            var expires = now.Add(lifetime);

            // JwtSecurityToken yêu cầu expires > notBefore. Khi phát hành token
            // đã hết hạn (dùng để test vòng đời token), hạ notBefore xuống trước
            // để không ném ArgumentException — token vẫn bị ValidateToken từ chối.
            if (expires <= now)
            {
                now = expires.AddSeconds(-1);
            }

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                notBefore: now,
                expires: expires,
                signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public ClaimsPrincipal? GetPrincipalFromToken(string token)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);
                return new ClaimsPrincipal(new ClaimsIdentity(jwtToken.Claims));
            }
            catch (Exception ex)
            {
                _logger.LineWarning($"[Jwt] Không đọc được token: {ex.GetType().Name}");
                return null;
            }
        }

        public bool ValidateToken(string token, out ClaimsPrincipal? principal)
            => ValidateTokenInternal(token, null, out principal);

        public bool ValidateRefreshToken(string token, out ClaimsPrincipal? principal)
            => ValidateTokenInternal(token, TokenTypeRefresh, out principal);

        public bool ValidateAccessToken(string token, out ClaimsPrincipal? principal)
            => ValidateTokenInternal(token, TokenTypeAccess, out principal);

        /// <summary>
        /// Kiểm tra chữ ký + hạn + (tuỳ chọn) claim <c>token_type</c>.
        ///
        /// Khi <paramref name="requiredTokenType"/> khác null thì token phải mang đúng
        /// claim đó ⇒ **access token không thể dùng để refresh** và ngược lại.
        /// </summary>
        private bool ValidateTokenInternal(string token, string? requiredTokenType, out ClaimsPrincipal? principal)
        {
            principal = null;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = _settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = _settings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = SigningKey,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                var validated = handler.ValidateToken(token, validationParameters, out _);

                if (requiredTokenType is not null)
                {
                    var tokenType = validated.FindFirst(TokenTypeClaim)?.Value;
                    if (!string.Equals(tokenType, requiredTokenType, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LineWarning(
                            $"[Jwt] Token không đúng loại yêu cầu ('{requiredTokenType}') — bị từ chối.");
                        return false;
                    }
                }

                principal = validated;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LineWarning($"[Jwt] Token không hợp lệ: {ex.GetType().Name} — {ex.Message}");
                return false;
            }
        }
    }
}
