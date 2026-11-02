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
        public ActionResult<CurrentUserDto> Me()
        {
            var user = User.Identity?.Name;
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

            return Ok(new CurrentUserDto(
                string.IsNullOrWhiteSpace(userId) ? 0 : int.Parse(userId),
                user ?? string.Empty,
                email ?? string.Empty,
                role ?? string.Empty));
        }
    }
}
