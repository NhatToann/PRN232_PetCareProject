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
        // TÁCH 2 ràng buộc thành 2 attribute riêng:
        //   [StringLength(100, MinimumLength = 6, ErrorMessage = "...")] dùng CHUNG 1
        //   message cho cả min & max ⇒ mật khẩu 150 ký tự cũng báo nhầm
        //   "phải có ít nhất 6 ký tự" ⇒ thông báo lỗi SAI ⇒ user tưởng mình gõ sai.
        [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự.")]
        [StringLength(100, ErrorMessage = "Mật khẩu không được vượt quá 100 ký tự.")]
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

    public sealed record GoogleAuthorizeUrlDto(
        string AuthorizeUrl,
        string RedirectUri,
        string State,
        string? Origin);

    /// <summary>
    /// Thông tin client của Google OAuth client.
    /// </summary>
    /// <param name="ClientId">Client ID (dùng để init Google Identity Services).</param>
    /// <param name="GsiEnabled">
    /// <c>true</c> nếu origin hiện tại nằm trong <c>javascript_origins</c> của OAuth client
    /// ⇒ GSI (One Tap) dùng được. <c>false</c> ⇒ GSI chắc chắn trả 403
    /// <c>"The given origin is not allowed for the given client ID."</c>
    /// ⇒ phải dùng luồng Authorization Code flow.
    /// </param>
    /// <param name="AllowedOrigins">Danh sách origin đã đăng ký trong Google Cloud Console.</param>
    public sealed record GoogleClientInfoDto(
        string ClientId,
        bool GsiEnabled,
        string[] AllowedOrigins);

    /// <summary>
    /// Đổi one-time token (do <c>/api/auth/google-callback</c> sinh) lấy thông tin user.
    /// </summary>
    public sealed record GoogleExchangeRequestDto(
        [Required(ErrorMessage = "Token không được để trống.")]
        [StringLength(200, ErrorMessage = "Token không hợp lệ.")]
        string? Token);

    public sealed record GoogleLoginRequestDto(
        [StringLength(4000, ErrorMessage = "IdToken không hợp lệ.")]
        string? IdToken,

        [StringLength(4000, ErrorMessage = "AccessToken không hợp lệ.")]
        string? AccessToken
    );

    /// <summary>
    /// Đăng nhập Google bằng authorization code (OAuth 2.0 Authorization Code flow).
    ///
    /// <para>
    /// Dùng khi Google Identity Services (GSI) bị chặn vì origin hiện tại chưa được
    /// đăng ký trong <c>javascript_origins</c> của OAuth client trên Google Cloud Console
    /// (ví dụ mở app ở <c>https://localhost:7083</c> trong khi client chỉ đăng ký
    /// <c>http://localhost:5286</c>). Khi đó GSI trả 403
    /// <c>"The given origin is not allowed for the given client ID."</c>.
    /// </para>
    ///
    /// <para>
    /// Flow này chỉ phụ thuộc <c>redirect_uris</c> (đã đăng ký sẵn) nên hoạt động từ
    /// mọi origin/port của ứng dụng.
    /// </para>
    /// </summary>
    public sealed record GoogleCodeLoginRequestDto(
        [Required(ErrorMessage = "Authorization code không được để trống.")]
        [StringLength(2000, ErrorMessage = "Authorization code không hợp lệ.")]
        string Code,

        /// <summary>
        /// Origin của trang đang gọi (vd <c>http://localhost:5286</c>). Dùng làm
        /// <c>redirect_uri</c> khi đổi code lấy token và làm <c>state</c> để chống CSRF.
        /// </summary>
        [StringLength(200, ErrorMessage = "Origin không hợp lệ.")]
        string? Origin,

        /// <summary>
        /// <c>state</c> do client sinh ra, gửi lại để server đối chiếu với cookie.
        /// </summary>
        [StringLength(200, ErrorMessage = "State không hợp lệ.")]
        string? State
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

    /// <summary>
    /// Yêu cầu đặt lại mật khẩu.
    ///
    /// <para>
    /// <b>LƯU Ý QUAN TRỌNG — vì sao KHÔNG dùng <c>[Compare("NewPassword")]</c>:</b>
    /// <c>[Compare]</c> phải được gắn vào <b>property</b>, không phải vào tham số
    /// của primary constructor của record. Khi gắn sai chỗ, ASP.NET Core ném
    /// <c>InvalidOperationException</c> ("Record type ... has validation metadata
    /// defined on property 'ConfirmPassword' that will be ignored") ⇒ endpoint
    /// <c>POST /api/auth/reset-password</c> trả <b>409</b> với body rác thay vì
    /// xử lý yêu cầu ⇒ <b>luồng quên mật khẩu không bao giờ chạy được</b>.
    /// </para>
    ///
    /// <para>
    /// Vì vậy: so khớp <c>NewPassword</c> / <c>ConfirmPassword</c> được kiểm tra
    /// thủ công trong <c>AuthService.ResetPasswordAsync</c> (đã có sẵn) và trả về
    /// lỗi 400 với message tiếng Việt cho client.
    /// </para>
    /// </summary>
    public sealed record ResetPasswordRequestDto(
        [Required(ErrorMessage = "Email không được để trống.")]
        [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
        string Email,

        [StringLength(100, ErrorMessage = "Reset token không hợp lệ.")]
        string? ResetToken,

        [StringLength(10, MinimumLength = 6, ErrorMessage = "Mã OTP phải có 6 ký tự.")]
        string? OtpCode,

        [Required(ErrorMessage = "Mật khẩu mới không được để trống.")]
        [MinLength(6, ErrorMessage = "Mật khẩu mới phải có ít nhất 6 ký tự.")]
        [StringLength(100, ErrorMessage = "Mật khẩu mới không được vượt quá 100 ký tự.")]
        string NewPassword,

        [Required(ErrorMessage = "Xác nhận mật khẩu không được để trống.")]
        [MinLength(6, ErrorMessage = "Xác nhận mật khẩu phải có ít nhất 6 ký tự.")]
        [StringLength(100, ErrorMessage = "Xác nhận mật khẩu không được vượt quá 100 ký tự.")]
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
        DateTime? BirthDate = null,
        string? Address = null,
        string? Phone = null);

    /// <summary>
    /// Địa chỉ giao hàng + số điện thoại liên hệ của khách hàng.
    ///
    /// <para>
    /// <b>VÌ SAO CẦN CẢ PHONE:</b> cột <c>Customer.address_Customer</c> chỉ chứa
    /// chuỗi địa chỉ, không chứa số điện thoại. Nếu chỉ đọc địa chỉ từ DB thì
    /// form thanh toán sẽ thiếu số điện thoại ⇒ <c>hasAddress</c> = false ⇒
    /// giao diện hiện "Chưa có địa chỉ giao hàng" dù DB có địa chỉ.
    /// </para>
    /// </summary>
    public sealed record CustomerAddressDto(
        string? Address,
        string? Phone);

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

    /// <summary>
    /// Body cho endpoint cập nhật địa chỉ giao hàng (cột <c>Customer.address_Customer</c>).
    /// <see cref="Address"/> = null/rỗng ⇒ xoá địa chỉ.
    /// </summary>
    public sealed record UpdateAddressRequestDto(
        string? Address);

    /// <summary>
    /// Body cho endpoint cập nhật địa chỉ giao hàng kèm số điện thoại.
    ///
    /// <para>
    /// <b>VÌ SAO CẦN:</b> địa chỉ trong DB (<c>Customer.address_Customer</c>) không
    /// chứa số điện thoại. Nếu chỉ lưu chuỗi địa chỉ thì khi đọc lại để điền sẵn
    /// form, trường "Số điện thoại" sẽ trống ⇒ form không hợp lệ ⇒ checkout bắt
    /// khách nhập lại từ đầu. Vì vậy số điện thoại được lưu kèm vào cột
    /// <c>Customer.phone</c> (cột này đã có sẵn trong schema).
    /// </para>
    /// </summary>
    public sealed record UpdateAddressWithPhoneRequestDto(
        string? Address,
        string? Phone);
}
