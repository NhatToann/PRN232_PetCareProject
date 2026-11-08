using PetShop.Models;

namespace PetShop.Interfaces
{
    public interface IAuthRepository
    {
        Task<Customer?> GetCustomerByEmailAsync(string email, CancellationToken ct = default);
        Task<Staff?> GetStaffByEmailAsync(string email, CancellationToken ct = default);
        Task<Doctor?> GetDoctorByEmailAsync(string email, CancellationToken ct = default);
        Task<Admin?> GetAdminByEmailOrUsernameAsync(string input, CancellationToken ct = default);
        Task<bool> EmailExistsInAnyRoleAsync(string email, CancellationToken ct = default);
        Task<Customer> CreateCustomerAsync(Customer customer, CancellationToken ct = default);
        Task UpdateCustomerAsync(Customer customer, CancellationToken ct = default);

        /// <summary>
        /// Cập nhật riêng cột <c>avatar_url</c> của một khách hàng.
        /// Trả về <c>false</c> nếu không tồn tại khách hàng tương ứng.
        /// </summary>
        Task<bool> UpdateCustomerAvatarAsync(int customerId, string? avatarUrl, CancellationToken ct = default);

        /// <summary>
        /// Đọc riêng cột <c>avatar_url</c> của một khách hàng (null nếu chưa đặt avatar).
        /// </summary>
        Task<string?> GetCustomerAvatarAsync(int customerId, CancellationToken ct = default);

        /// <summary>
        /// Cập nhật riêng cột <c>birth_date</c> của một khách hàng.
        /// Trả về <c>false</c> nếu không tồn tại khách hàng tương ứng.
        /// </summary>
        Task<bool> UpdateCustomerBirthDateAsync(int customerId, DateTime? birthDate, CancellationToken ct = default);

        /// <summary>
        /// Đọc riêng cột <c>birth_date</c> của một khách hàng (null nếu chưa đặt ngày sinh).
        /// </summary>
        Task<DateTime?> GetCustomerBirthDateAsync(int customerId, CancellationToken ct = default);

        /// <summary>
        /// Nâng cấp mật khẩu của mọi tài khoản có email tương ứng sang hash BCrypt mới.
        /// Dùng cho migrate-on-login: tài khoản seed cũ đang lưu mật khẩu dạng chữ đơn giản
        /// sẽ được hash lại ngay lần đăng nhập đầu tiên.
        /// </summary>
        Task<int> UpgradePasswordHashAsync(string email, string newPasswordHash, CancellationToken ct = default);
    }
}
