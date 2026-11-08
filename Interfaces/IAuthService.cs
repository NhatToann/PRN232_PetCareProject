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
    }
}
