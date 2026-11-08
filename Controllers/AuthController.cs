using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public sealed class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly IJwtTokenService _jwtTokenService;

        public AuthController(IAuthService authService, IJwtTokenService jwtTokenService)
        {
            _authService = authService;
            _jwtTokenService = jwtTokenService;
        }

        [HttpPost("register")]
        public async Task<ActionResult<AuthResponseDto>> Register([FromBody] RegisterRequestDto request, CancellationToken ct)
        {
            try
            {
                var result = await _authService.RegisterAsync(request, ct);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponseDto>> Login([FromBody] LoginRequestDto request, CancellationToken ct)
        {
            try
            {
                var result = await _authService.LoginAsync(request, ct);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
        }

        [HttpPost("google-login")]
        public async Task<ActionResult<AuthResponseDto>> GoogleLogin([FromBody] GoogleLoginRequestDto request, CancellationToken ct)
        {
            try
            {
                var result = await _authService.LoginWithGoogleAsync(request, ct);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
        }

        [HttpPost("send-email-otp")]
        public async Task<ActionResult<SimpleMessageDto>> SendEmailOtp([FromBody] SendEmailOtpRequestDto request, CancellationToken ct)
        {
            try
            {
                await _authService.SendEmailOtpAsync(request, ct);
                return Ok(new SimpleMessageDto("Đã gửi OTP đến email."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = $"Gửi OTP thất bại: {ex.Message}" });
            }
        }

        [HttpPost("verify-email-otp")]
        public async Task<ActionResult<SimpleMessageDto>> VerifyEmailOtp([FromBody] VerifyEmailOtpRequestDto request, CancellationToken ct)
        {
            try
            {
                var result = await _authService.VerifyEmailOtpAsync(request, ct);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("reset-password")]
        public async Task<ActionResult<SimpleMessageDto>> ResetPassword([FromBody] ResetPasswordRequestDto request, CancellationToken ct)
        {
            try
            {
                await _authService.ResetPasswordAsync(request, ct);
                return Ok(new SimpleMessageDto("Đặt lại mật khẩu thành công."));
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("google-client-id")]
        public ActionResult<object> GetGoogleClientId([FromServices] IConfiguration configuration)
        {
            var clientId = (configuration["GoogleAuth:ClientId"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return NotFound(new { message = "Google Client ID chưa được cấu hình." });
            }

            return Ok(new { clientId });
        }

        /// <summary>
        /// Đăng nhập và nhận JWT Bearer token cho client API bên ngoài.
        ///
        /// Endpoint này bổ sung cho luồng nội bộ: Blazor Server dùng session,
        /// còn React/Postman/mobile app dùng token trả về ở đây rồi gửi
        /// <c>Authorization: Bearer &lt;token&gt;</c> cho các request sau.
        /// </summary>
        [HttpPost("token")]
        [AllowAnonymous]
        public async Task<ActionResult<TokenResponseDto>> IssueToken([FromBody] LoginRequestDto request, CancellationToken ct)
        {
            try
            {
                var result = await _authService.LoginAsync(request, ct);
                var token = _jwtTokenService.CreateToken(
                    result.UserId, result.Name, result.Email, result.Role);

                return Ok(new TokenResponseDto(
                    token,
                    "Bearer",
                    result.UserId,
                    result.Name,
                    result.Email,
                    result.Role));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Trả về thông tin người dùng đang xác thực — dùng để client kiểm tra
        /// token còn hiệu lực hay không.
        /// </summary>
        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
        {
            var user = User.Identity?.Name;
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

            var parsedUserId = string.IsNullOrWhiteSpace(userId) ? 0 : int.Parse(userId);

            // Avatar nằm trên bảng Customer — chỉ đọc được khi người dùng là khách hàng.
            var avatarUrl = parsedUserId > 0
                ? await _authService.GetAvatarAsync(parsedUserId, ct)
                : null;

            return Ok(new CurrentUserDto(
                parsedUserId,
                user ?? string.Empty,
                email ?? string.Empty,
                role ?? string.Empty,
                avatarUrl));
        }

        /// <summary>
        /// Cập nhật ảnh đại diện của khách hàng (cột <c>Customer.avatar_url</c>).
        ///
        /// customerId lấy từ claim của request đang xác thực, KHÔNG tin body —
        /// nhờ vậy mỗi khách hàng chỉ sửa được avatar của chính mình.
        ///
        /// Giá trị <c>avatarUrl</c> hợp lệ: <c>null</c> (xoá avatar),
        /// chuỗi <c>data:image/...</c> hoặc <c>http(s)://...</c>, tối đa 500 ký tự.
        /// </summary>
        [HttpPut("avatar")]
        [HttpPost("avatar")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> UpdateAvatar([FromBody] UpdateAvatarRequestDto request, CancellationToken ct)
        {
            var customerId = ResolveCustomerId();
            if (customerId <= 0)
            {
                return Unauthorized(new { message = "Không xác định được khách hàng đang đăng nhập." });
            }

            try
            {
                var updated = await _authService.UpdateAvatarAsync(customerId, request?.AvatarUrl, ct);
                if (!updated)
                {
                    return NotFound(new { message = "Không tìm thấy tài khoản khách hàng." });
                }

                return Ok(new { message = "Đã cập nhật ảnh đại diện." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Lấy customer id từ claim của principal đang xác thực.
        /// Ưu tiên claim <c>customer_id</c> (header <c>X-Customer-Id</c>), fallback sang
        /// <c>ClaimTypes.NameIdentifier</c> (header <c>X-User-Id</c> / sub của JWT).
        /// </summary>
        private int ResolveCustomerId()
        {
            var customerClaim = User.FindFirst("customer_id")?.Value;
            if (int.TryParse(customerClaim, out var customerId) && customerId > 0)
            {
                return customerId;
            }

            var nameIdentifier = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(nameIdentifier, out var userId) && userId > 0)
            {
                return userId;
            }

            return 0;
        }
    }
}
