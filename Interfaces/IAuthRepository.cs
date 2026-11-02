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
        /// Nâng cấp mật khẩu của mọi tài khoản có email tương ứng sang hash BCrypt mới.
        /// Dùng cho migrate-on-login: tài khoản seed cũ đang lưu mật khẩu dạng chữ đơn giản
        /// sẽ được hash lại ngay lần đăng nhập đầu tiên.
        /// </summary>
        Task<int> UpgradePasswordHashAsync(string email, string newPasswordHash, CancellationToken ct = default);
    }
}
