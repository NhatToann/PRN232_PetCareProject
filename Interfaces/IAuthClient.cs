using PetShop.Models;

namespace PetShop.Interfaces;

public interface IAuthClient
{
    Task<FrontendApiResult<AuthResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);

    Task<FrontendApiResult<AuthResponseDto>> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default);

    Task<FrontendApiResult<SimpleMessageDto>> SendOtpAsync(SendEmailOtpRequestDto request, CancellationToken cancellationToken = default);

    Task<FrontendApiResult<SimpleMessageDto>> VerifyOtpAsync(VerifyEmailOtpRequestDto request, CancellationToken cancellationToken = default);

    Task<FrontendApiResult<SimpleMessageDto>> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken = default);

    Task<FrontendApiResult<AuthResponseDto>> GoogleLoginAsync(GoogleLoginRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đăng nhập Google bằng authorization code (fallback khi GSI bị chặn do origin).
    /// </summary>
    Task<FrontendApiResult<AuthResponseDto>> GoogleCodeLoginAsync(GoogleCodeLoginRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy thông tin client Google (Client ID + cờ GSI có dùng được ở origin hiện tại).
    /// </summary>
    Task<FrontendApiResult<GoogleClientInfoDto>> GetGoogleClientInfoAsync(string? origin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy URL đăng nhập Google (Authorization Code flow) — fallback khi GSI bị chặn do origin.
    /// </summary>
    Task<FrontendApiResult<GoogleAuthorizeUrlDto>> GetGoogleAuthorizeUrlAsync(string? origin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đổi one-time token (do <c>google-callback</c> sinh) lấy thông tin user.
    /// </summary>
    Task<FrontendApiResult<AuthResponseDto>> GoogleExchangeAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc ảnh đại diện của khách hàng từ server (cột <c>Customer.avatar_url</c>).
    /// </summary>
    Task<FrontendApiResult<string?>> GetAvatarAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật ảnh đại diện của khách hàng. <paramref name="avatarUrl"/> = null ⇒ xoá avatar.
    /// </summary>
    Task<FrontendApiResult<SimpleMessageDto>> UpdateAvatarAsync(string? avatarUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc ngày sinh của khách hàng từ server (cột <c>Customer.birth_date</c>).
    /// </summary>
    Task<FrontendApiResult<DateTime?>> GetBirthDateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật ngày sinh của khách hàng. <paramref name="birthDate"/> = null ⇒ xoá ngày sinh.
    /// </summary>
    Task<FrontendApiResult<SimpleMessageDto>> UpdateBirthDateAsync(DateTime? birthDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc địa chỉ giao hàng của khách hàng từ server (cột <c>Customer.address_Customer</c>).
    /// </summary>
    Task<FrontendApiResult<string?>> GetAddressAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật địa chỉ giao hàng của khách hàng. <paramref name="address"/> = null/rỗng ⇒ xoá địa chỉ.
    /// </summary>
    Task<FrontendApiResult<SimpleMessageDto>> UpdateAddressAsync(string? address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật địa chỉ giao hàng kèm số điện thoại của khách hàng.
    ///
    /// <para>
    /// <b>VÌ SAO CẦN:</b> cột địa chỉ trong DB không chứa số điện thoại. Nếu chỉ lưu
    /// chuỗi địa chỉ thì khi đọc lại để điền sẵn form thanh toán, trường "Số điện
    /// thoại" sẽ trống ⇒ form không hợp lệ ⇒ khách phải nhập lại từ đầu.
    /// </para>
    /// </summary>
    Task<FrontendApiResult<SimpleMessageDto>> UpdateAddressWithPhoneAsync(
        string? address,
        string? phone,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc số điện thoại của khách hàng từ server (cột <c>Customer.phone</c>).
    /// </summary>
    Task<FrontendApiResult<string?>> GetPhoneAsync(CancellationToken cancellationToken = default);
}
