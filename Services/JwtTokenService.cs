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

        /// <summary>Thời gian sống của token (phút).</summary>
        public int ExpiresInMinutes { get; set; } = 120;
    }

    /// <summary>
    /// Phát hành và kiểm tra JWT Bearer token cho các client API bên ngoài.
    ///
    /// Hệ thống xác thực của dự án là **dual-mode**:
    ///   1. Blazor Server (nội bộ) — dùng session + header <c>X-User-Id</c>/<c>X-User-Role</c>.
    ///   2. Client API bên ngoài — dùng <c>Authorization: Bearer &lt;token&gt;</c>.
    ///
    /// Cả hai cùng tồn tại; endpoint nào cần bảo vệ thì thêm <c>[Authorize]</c>.
    /// </summary>
    public interface IJwtTokenService
    {
        /// <summary>Sinh token cho một người dùng đã xác thực.</summary>
        string CreateToken(int userId, string name, string email, string role);

        /// <summary>Sinh token kèm thời hạn tuỳ biến (dùng cho luồng refresh token).</summary>
        string CreateToken(int userId, string name, string email, string role, TimeSpan lifetime);

        /// <summary>Đọc các claim từ token (không xác thực chữ ký) — dùng để hiển thị/log.</summary>
        ClaimsPrincipal? GetPrincipalFromToken(string token);

        /// <summary>Kiểm tra chữ ký + hạn của token.</summary>
        bool ValidateToken(string token, out ClaimsPrincipal? principal);
    }

    public sealed class JwtTokenService : IJwtTokenService
    {
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
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Name, name ?? string.Empty),
                new(ClaimTypes.Email, email ?? string.Empty),
                new(ClaimTypes.Role, role ?? string.Empty),
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

                principal = handler.ValidateToken(token, validationParameters, out _);
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
