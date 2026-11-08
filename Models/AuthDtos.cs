using System.ComponentModel.DataAnnotations;

namespace PetShop.Models
{
    /// <summary>
    /// DTO cho đăng ký tài khoản khách hàng.
    /// Validate bằng DataAnnotations — [ApiController] tự trả 400 + ValidationProblemDetails
    /// khi model không hợp lệ (không cần if-check thủ công trong controller).
    /// </summary>
    public sealed record RegisterRequestDto(
        [Required(ErrorMessage = "Họ tên không được để trống.")]
        [StringLength(100, ErrorMessage = "Họ tên không được vượt quá 100 ký tự.")]
        string Name,

        [Required(ErrorMessage = "Email không được để trống.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        [StringLength(150, ErrorMessage = "Email không được vượt quá 150 ký tự.")]
        string Email,

        [Required(ErrorMessage = "Mật khẩu không được để trống.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
        string Password,

        [Phone(ErrorMessage = "Số điện thoại không hợp lệ.")]
        [StringLength(20, ErrorMessage = "Số điện thoại không được vượt quá 20 ký tự.")]
        string? Phone,

        [StringLength(255, ErrorMessage = "Địa chỉ không được vượt quá 255 ký tự.")]
        string? Address
    );

    public sealed record LoginRequestDto(
        [Required(ErrorMessage = "Email không được để trống.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        string Email,

        [Required(ErrorMessage = "Mật khẩu không được để trống.")]
        string Password
    );

    public sealed record GoogleLoginRequestDto(
        [StringLength(4000, ErrorMessage = "IdToken không hợp lệ.")]
        string? IdToken,

        [StringLength(4000, ErrorMessage = "AccessToken không hợp lệ.")]
        string? AccessToken
    );

    public sealed record SendEmailOtpRequestDto(
        [Required(ErrorMessage = "Email không được để trống.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        string Email
    );

    public sealed record VerifyEmailOtpRequestDto(
        [Required(ErrorMessage = "Email không được để trống.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        string Email,

        [Required(ErrorMessage = "Mã OTP không được để trống.")]
        [StringLength(10, MinimumLength = 6, ErrorMessage = "Mã OTP phải có 6 ký tự.")]
        string OtpCode
    );

    public sealed record ResetPasswordRequestDto(
        [Required(ErrorMessage = "Email không được để trống.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        string Email,

        [StringLength(100, ErrorMessage = "Reset token không hợp lệ.")]
        string? ResetToken,

        [StringLength(10, MinimumLength = 6, ErrorMessage = "Mã OTP phải có 6 ký tự.")]
        string? OtpCode,

        [Required(ErrorMessage = "Mật khẩu mới không được để trống.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
        string NewPassword,

        [Required(ErrorMessage = "Xác nhận mật khẩu không được để trống.")]
        [property: Compare("NewPassword", ErrorMessage = "Xác nhận mật khẩu không khớp.")]
        string ConfirmPassword
    );

    /// <summary>
    /// Kết quả đăng nhập. Không có attribute validate vì đây là response DTO.
    /// <see cref="AvatarUrl"/> là ảnh đại diện khách hàng (data-URI hoặc http/https),
    /// đồng bộ với cột <c>Customer.avatar_url</c>.
    /// </summary>
    public sealed record AuthResponseDto(
        int UserId,
        string Name,
        string Email,
        string Role,
        string Status,
        string? AvatarUrl = null);

    public sealed record SimpleMessageDto(string Message);

    /// <summary>
    /// Kết quả đăng nhập kèm JWT Bearer token — dành cho client API bên ngoài.
    /// </summary>
    public sealed record TokenResponseDto(
        string AccessToken,
        string TokenType,
        int UserId,
        string Name,
        string Email,
        string Role);

    /// <summary>Thông tin người dùng đang xác thực (từ token hoặc header nội bộ).</summary>
    public sealed record CurrentUserDto(
        int UserId,
        string Name,
        string Email,
        string Role,
        string? AvatarUrl = null);

    /// <summary>
    /// Body cho endpoint cập nhật ảnh đại diện.
    /// <see cref="AvatarUrl"/> = null ⇒ xoá ảnh đại diện.
    /// </summary>
    public sealed record UpdateAvatarRequestDto(
        string? AvatarUrl);
}
