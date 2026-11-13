using PetShop.Models;

namespace PetShop.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken ct = default);
        Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default);
        Task<AuthResponseDto> LoginWithGoogleAsync(GoogleLoginRequestDto request, CancellationToken ct = default);

        /// <summary>
        /// Đăng nhập Google bằng authorization code (OAuth 2.0 Authorization Code flow).
        /// Dùng khi GSI bị chặn do origin chưa nằm trong <c>javascript_origins</c>.
        /// </summary>
        Task<AuthResponseDto> LoginWithGoogleCodeAsync(GoogleCodeLoginRequestDto request, CancellationToken ct = default);
        Task SendEmailOtpAsync(SendEmailOtpRequestDto request, CancellationToken ct = default);
        Task<SimpleMessageDto> VerifyEmailOtpAsync(VerifyEmailOtpRequestDto request, CancellationToken ct = default);
        Task ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken ct = default);

        /// <summary>
        /// Cập nhật ảnh đại diện của khách hàng (cột <c>Customer.avatar_url</c>).
        /// <paramref name="avatarUrl"/> = null ⇒ xoá ảnh đại diện.
        /// Trả về <c>false</c> nếu khách hàng không tồn tại.
        /// </summary>
        Task<bool> UpdateAvatarAsync(int customerId, string? avatarUrl, CancellationToken ct = default);

        /// <summary>
        /// Đọc ảnh đại diện của khách hàng từ DB (null nếu chưa đặt).
        /// </summary>
        Task<string?> GetAvatarAsync(int customerId, CancellationToken ct = default);

        /// <summary>
        /// Cập nhật ngày sinh của khách hàng (cột <c>Customer.birth_date</c>).
        /// <paramref name="birthDate"/> = null ⇒ xoá ngày sinh.
        /// Trả về <c>false</c> nếu khách hàng không tồn tại.
        /// </summary>
        Task<bool> UpdateBirthDateAsync(int customerId, DateTime? birthDate, CancellationToken ct = default);

        /// <summary>
        /// Đọc ngày sinh của khách hàng từ DB (null nếu chưa đặt).
        /// </summary>
        Task<DateTime?> GetBirthDateAsync(int customerId, CancellationToken ct = default);

        /// <summary>
        /// Cập nhật địa chỉ giao hàng của khách hàng (cột <c>Customer.address_Customer</c>).
        /// <paramref name="address"/> = null/rỗng ⇒ xoá địa chỉ.
        /// Trả về <c>false</c> nếu khách hàng không tồn tại.
        /// </summary>
        Task<bool> UpdateAddressAsync(int customerId, string? address, CancellationToken ct = default);

        /// <summary>
        /// Đọc địa chỉ giao hàng của khách hàng từ DB (null nếu chưa có).
        /// </summary>
        Task<string?> GetAddressAsync(int customerId, CancellationToken ct = default);

        /// <summary>
        /// Cập nhật địa chỉ giao hàng kèm số điện thoại của khách hàng.
        ///
        /// <para>
        /// <b>VÌ SAO CẦN CẢ PHONE:</b> cột <c>Customer.address_Customer</c> chỉ chứa
        /// chuỗi địa chỉ. Nếu chỉ lưu chuỗi địa chỉ thì khi đọc lại để điền sẵn
        /// form thanh toán, trường "Số điện thoại" sẽ trống ⇒ form không hợp lệ ⇒
        /// khách phải nhập lại địa chỉ từ đầu ở mỗi đơn hàng.
        /// </para>
        /// </summary>
        /// <param name="customerId">Id khách hàng.</param>
        /// <param name="address">Chuỗi địa chỉ đầy đủ; null/rỗng ⇒ xoá địa chỉ.</param>
        /// <param name="phone">Số điện thoại liên hệ; null/rỗng ⇒ giữ nguyên số cũ.</param>
        /// <param name="ct">Token huỷ.</param>
        /// <returns><c>false</c> nếu khách hàng không tồn tại.</returns>
        Task<bool> UpdateAddressWithPhoneAsync(int customerId, string? address, string? phone, CancellationToken ct = default);

        /// <summary>
        /// Đọc số điện thoại của khách hàng từ DB (null nếu chưa đặt).
        /// </summary>
        Task<string?> GetPhoneAsync(int customerId, CancellationToken ct = default);
    }
}
