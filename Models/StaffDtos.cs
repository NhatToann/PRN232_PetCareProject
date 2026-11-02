namespace PetShop.Models
{
    // ========================================================
    // DTO CHO STAFF SERVICE
    // ========================================================
    public class StaffDto
    {
        public int StaffId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty; // FIX: Sửa thành Position
        public string Status { get; set; } = string.Empty;
    }

    public class StaffCreateUpdateDto
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty; // FIX: Bổ sung Password
        public string Position { get; set; } = string.Empty; // FIX: Sửa thành Position
        public string Status { get; set; } = string.Empty;
    }

    // ========================================================
    // DTO CHO DASHBOARD VÀ REVENUE (Giữ nguyên)
    // ========================================================
    public class StaffDashboardSummaryDto
    {
        public int ServicesToday { get; set; }
        public int PendingBookings { get; set; }
        public string CurrentShiftName { get; set; } = "Chưa có ca";
        public string CurrentShiftTime { get; set; } = "--:-- - --:--";
        public List<NextTaskDto> NextTasks { get; set; } = new();
    }

    public class NextTaskDto
    {
        public string Time { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string PetName { get; set; } = string.Empty;
        public string PetBreed { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class StaffBookingDto
    {
        public int BookingId { get; set; }
        public DateTime StartTime { get; set; }
        public string PetName { get; set; } = "Chưa rõ";
        public string PetBreed { get; set; } = "Thú cưng";
        public string CustomerName { get; set; } = "Khách lẻ";
        public string Status { get; set; } = "Chờ xử lý";
        public string Note { get; set; } = string.Empty;
    }

    public class StaffScheduleDto
    {
        public int ScheduleId { get; set; }
        public DateTime WorkDate { get; set; }
        public string ShiftName { get; set; } = string.Empty;
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>
    /// Hồ sơ hiển thị của người đang đăng nhập (nhân viên hoặc khách hàng).
    /// Nguồn dữ liệu được xác định bằng email trước, sau đó mới tới id — tránh nhầm giữa
    /// bảng Customer và bảng Staff (hai bảng có thể trùng id).
    /// </summary>
    public class StaffProfileDto
    {
        /// <summary>"customer" nếu lấy từ bảng Customer, "staff" nếu lấy từ bảng Staff.</summary>
        public string Source { get; set; } = "customer";

        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        /// <summary>Vai trò hiển thị: staff / admin / user.</summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>Chức vụ (chỉ có ở bảng Staff).</summary>
        public string Position { get; set; } = string.Empty;
    }
}