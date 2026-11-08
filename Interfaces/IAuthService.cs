using PetShop.Models;

namespace PetShop.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken ct = default);
        Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default);
        Task<AuthResponseDto> LoginWithGoogleAsync(GoogleLoginRequestDto request, CancellationToken ct = default);
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
    }
}
