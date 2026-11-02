using PetShop.Models;

namespace PetShop.Interfaces
{
    /// <summary>
    /// Quản lý hồ sơ của người đang đăng nhập (trang "Hồ Sơ Của Tôi" của nhân viên).
    /// Dữ liệu được định vị bằng email trước, sau đó mới tới id — vì bảng Customer
    /// và bảng Staff có thể trùng id với nhau.
    /// </summary>
    public interface IStaffProfileService
    {
        /// <summary>Lấy hồ sơ theo email + id đăng nhập. Trả về null nếu không tìm thấy.</summary>
        Task<StaffProfileDto?> GetProfileAsync(int userId, string? email, CancellationToken ct = default);

        /// <summary>
        /// Xác định staff_id thật của người đang đăng nhập.
        /// Lý do cần hàm này: hệ thống đăng nhập có thể trả về UserId theo bảng Customer
        /// (vài tài khoản có role "staff" nhưng nằm trong bảng Customer), trong khi
        /// WorkSchedule/AttendanceRecord lại tham chiếu staff_id của bảng Staff.
        /// Trả về 0 nếu không xác định được.
        /// </summary>
        Task<int> ResolveStaffIdAsync(int userId, string? email, CancellationToken ct = default);

        /// <summary>Cập nhật hồ sơ. Trả về false nếu không tìm thấy bản ghi tương ứng.</summary>
        Task<bool> UpdateProfileAsync(StaffProfileDto profile, CancellationToken ct = default);
    }
}
