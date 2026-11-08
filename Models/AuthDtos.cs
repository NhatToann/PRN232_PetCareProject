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
    /// <see cref="BirthDate"/> là ngày sinh, đồng bộ với cột <c>Customer.birth_date</c>.
    /// </summary>
    public sealed record AuthResponseDto(
        int UserId,
        string Name,
        string Email,
        string Role,
        string Status,
        string? AvatarUrl = null,
        DateTime? BirthDate = null);

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

    /// <summary>
    /// Body cho endpoint làm mới access token bằng refresh token (slide Ch06 S42).
    ///
    /// Slide mô tả body gồm cả <c>accessToken</c> lẫn <c>refreshToken</c>.
    /// <see cref="AccessToken"/> là **tuỳ chọn** — client có thể gửi kèm access token
    /// cũ (đúng format slide) hoặc chỉ gửi refresh token.
    /// </summary>
    public sealed record RefreshTokenRequestDto(
        [Required(ErrorMessage = "Refresh token không được để trống.")]
        [StringLength(4000, ErrorMessage = "Refresh token không hợp lệ.")]
        string RefreshToken,

        [StringLength(4000, ErrorMessage = "Access token không hợp lệ.")]
        string? AccessToken = null);

    /// <summary>
    /// Kết quả làm mới token — trả về access token mới và refresh token mới (rotate).
    /// </summary>
    public sealed record RefreshTokenResponseDto(
        string AccessToken,
        string RefreshToken,
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
        string? AvatarUrl = null,
        DateTime? BirthDate = null);

    /// <summary>
    /// Body cho endpoint cập nhật ảnh đại diện.
    /// <see cref="AvatarUrl"/> = null ⇒ xoá ảnh đại diện.
    /// </summary>
    public sealed record UpdateAvatarRequestDto(
        string? AvatarUrl);

    /// <summary>
    /// Body cho endpoint cập nhật ngày sinh (cột <c>Customer.birth_date</c>).
    /// <see cref="BirthDate"/> = null ⇒ xoá ngày sinh.
    /// </summary>
    public sealed record UpdateBirthDateRequestDto(
        DateTime? BirthDate);
}
