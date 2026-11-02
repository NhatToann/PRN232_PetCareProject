using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services
{
    public class StaffDashboardService : IStaffDashboardService
    {
        private readonly ShopPetDatabaseContext _context;
        private readonly ILogger<StaffDashboardService> _logger;

        public StaffDashboardService(ShopPetDatabaseContext context, ILogger<StaffDashboardService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<StaffDashboardSummaryDto> GetDashboardSummaryAsync(int staffId)
        {
            var currentTime = DateTime.Now;
            var todayDateOnly = DateOnly.FromDateTime(currentTime);

            var todayBookings = await _context.Bookings
                .Include(b => b.Customer).Include(b => b.Pet).ThenInclude(p => p.Breed)
                .Where(b => b.AppointmentStart.Date == currentTime.Date).OrderBy(b => b.AppointmentStart).ToListAsync();

            // Bơm lịch hẹn mẫu khi hôm nay chưa có dữ liệu — phục vụ demo/bảo vệ môn học.
            if (todayBookings.Count < 5)
            {
                try
                {
                    var b1 = new Booking { AppointmentStart = currentTime.AddMinutes(30), Status = "Đang xử lý", Note = "Chị Lan - Chó Poodle (Tắm sấy)", CreatedAt = DateTime.Now };
                    var b2 = new Booking { AppointmentStart = currentTime.AddHours(1), Status = "Chờ xác nhận", Note = "Anh Hoàng - Mèo Anh (Cắt tỉa lông)", CreatedAt = DateTime.Now };
                    var b3 = new Booking { AppointmentStart = currentTime.AddHours(2), Status = "Chưa thanh toán", Note = "Cô Mai - Chó Corgi (Spa tổng quát)", CreatedAt = DateTime.Now };
                    var b4 = new Booking { AppointmentStart = currentTime.AddHours(3), Status = "Chờ xác nhận", Note = "Chú Tuấn - Mèo Ba Tư (Vệ sinh tai)", CreatedAt = DateTime.Now };
                    var b5 = new Booking { AppointmentStart = currentTime.AddHours(4), Status = "Đang xử lý", Note = "Em Hương - Chó Pug (Grooming trọn gói)", CreatedAt = DateTime.Now };

                    _context.Bookings.AddRange(b1, b2, b3, b4, b5);
                    await _context.SaveChangesAsync();

                    todayBookings = await _context.Bookings.Include(b => b.Customer)
                        .Where(b => b.AppointmentStart.Date == currentTime.Date).OrderBy(b => b.AppointmentStart).ToListAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Không seed được lịch hẹn mẫu cho dashboard.");
                }
            }

            int servicesToday = todayBookings.Count;
            int pendingBookings = await _context.Bookings.CountAsync(b => b.Status == "Chờ xác nhận" || b.Status == "Chưa thanh toán" || b.Status == "pending" || b.Status == "Đang xử lý");

            var nextTasks = todayBookings
                .Where(b => b.Status != "Hoàn thành" && b.Status != "Đã hủy")
                .Take(6).Select(b => new NextTaskDto
                {
                    Time = b.AppointmentStart.ToString("HH:mm"),
                    CustomerName = b.Customer?.Name ?? (b.Note != null && b.Note.Contains("-") ? b.Note.Split('-')[0].Trim() : "Khách Demo"),
                    PetName = b.Pet?.PetName ?? "Thú cưng",
                    PetBreed = b.Pet?.Breed?.BreedName ?? "Chó/Mèo",
                    ServiceName = "Dịch vụ Spa Thú Cưng",
                    Status = b.Status ?? "Đang xử lý"
                }).ToList();

            // Ca trực hiện tại lấy từ lịch thật trong DB; nếu hôm nay không có ca thì báo "Chưa có ca".
            var todaySchedule = await GetCurrentShiftAsync(staffId, todayDateOnly);

            return new StaffDashboardSummaryDto
            {
                ServicesToday = servicesToday,
                PendingBookings = pendingBookings,
                CurrentShiftName = todaySchedule?.ShiftName ?? "Chưa có ca",
                CurrentShiftTime = todaySchedule is null
                    ? "--:-- - --:--"
                    : $"{todaySchedule.StartTime} - {todaySchedule.EndTime}",
                NextTasks = nextTasks
            };
        }

        public async Task<List<StaffBookingDto>> GetStaffBookingsAsync()
        {
            var rawBookings = await _context.Bookings.Include(b => b.Customer).Include(b => b.Pet).ThenInclude(p => p.Breed)
                .OrderByDescending(b => b.AppointmentStart).ToListAsync();

            return rawBookings.Select(b => new StaffBookingDto
            {
                BookingId = b.BookingId,
                StartTime = b.AppointmentStart,
                PetName = b.Pet != null ? b.Pet.PetName : "Thú cưng",
                PetBreed = (b.Pet != null && b.Pet.Breed != null) ? b.Pet.Breed.BreedName : "Giống chuẩn",
                CustomerName = b.Customer?.Name ?? "Khách hàng",
                Status = b.Status ?? "Chờ xử lý",
                Note = b.Note ?? string.Empty
            }).ToList();
        }

        public async Task<bool> UpdateBookingStatusAsync(int bookingId, string newStatus)
        {
            var booking = await _context.Bookings.FindAsync(bookingId);
            if (booking is null)
            {
                _logger.LogWarning("Không tìm thấy booking {BookingId} để cập nhật trạng thái.", bookingId);
                return false;
            }

            booking.Status = newStatus;

            // CreatedAt/UpdatedAt có thể không tồn tại trên entity (tuỳ schema), nên bọc riêng
            // để một cột thiếu không làm hỏng cả lần cập nhật.
            TrySetTimestamp(booking, "UpdatedAt");
            TrySetCreatedAtIfMissing(booking);

            try
            {
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lưu trạng thái booking {BookingId}.", bookingId);
                return false;
            }
        }

        public async Task<List<StaffScheduleDto>> GetStaffSchedulesAsync(int staffId)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);

            var rows = await _context.WorkSchedules
                .AsNoTracking()
                .Where(w => w.StaffId == staffId)
                .OrderBy(w => w.WorkDate)
                .ThenBy(w => w.StartTime)
                .ToListAsync();

            // Lịch hôm nay + các ca sắp tới (bỏ qua ca đã qua) để nhân viên thấy đủ lịch.
            var upcoming = rows
                .Where(w => w.WorkDate >= today)
                .ToList();

            if (upcoming.Count == 0)
            {
                // Không có ca tương lai → lùi về ca hôm nay (có thể đã qua) để vẫn hiển thị lịch.
                upcoming = rows.Where(w => w.WorkDate == today).ToList();
            }

            var deduped = WorkScheduleDedup.ByStaffDateTimeSlot(upcoming);

            return deduped.Select(w => new StaffScheduleDto
            {
                ScheduleId = w.ScheduleId,
                WorkDate = w.WorkDate.ToDateTime(w.StartTime),
                StartTime = w.StartTime.ToString(@"HH\:mm"),
                EndTime = w.EndTime.ToString(@"HH\:mm"),
                ShiftName = DescribeShiftName(w),
                Status = string.IsNullOrWhiteSpace(w.Status) ? "Chưa bắt đầu" : w.Status,
                Note = w.Note ?? string.Empty
            }).ToList();
        }

        public async Task<bool> CheckInAsync(int scheduleId)
        {
            if (scheduleId <= 0)
            {
                _logger.LogWarning("Check-in với scheduleId không hợp lệ: {ScheduleId}.", scheduleId);
                return false;
            }

            // Update đúng dòng lịch được yêu cầu (không dùng "dòng cuối cùng" trong bảng).
            var schedule = await _context.WorkSchedules.FirstOrDefaultAsync(w => w.ScheduleId == scheduleId);
            if (schedule is null)
            {
                _logger.LogWarning("Check-in thất bại: không tìm thấy ca với scheduleId={ScheduleId}.", scheduleId);
                return false;
            }

            schedule.Status = "Đã Check-in";

            try
            {
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi check-in ca scheduleId={ScheduleId}.", scheduleId);
                return false;
            }
        }

        // ── helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Ca đang diễn ra (hoặc sắp tới) của nhân viên trong ngày, đọc từ DB.
        /// </summary>
        private async Task<StaffScheduleDto?> GetCurrentShiftAsync(int staffId, DateOnly workDate)
        {
            var rows = await _context.WorkSchedules
                .AsNoTracking()
                .Where(w => w.StaffId == staffId && w.WorkDate == workDate)
                .OrderBy(w => w.StartTime)
                .ToListAsync();

            var shifts = WorkScheduleDedup.ByStaffDateTimeSlot(rows);
            if (shifts.Count == 0)
            {
                return null;
            }

            var now = TimeOnly.FromDateTime(DateTime.Now);

            // Ưu tiên ca đang diễn ra; nếu chưa tới ca nào thì lấy ca sắp tới.
            var current = shifts.FirstOrDefault(s => now >= s.StartTime && now <= s.EndTime)
                          ?? shifts.FirstOrDefault(s => s.StartTime > now)
                          ?? shifts[0];

            return new StaffScheduleDto
            {
                ScheduleId = current.ScheduleId,
                WorkDate = current.WorkDate.ToDateTime(current.StartTime),
                StartTime = current.StartTime.ToString(@"HH\:mm"),
                EndTime = current.EndTime.ToString(@"HH\:mm"),
                ShiftName = DescribeShiftName(current),
                Status = string.IsNullOrWhiteSpace(current.Status) ? "Chưa bắt đầu" : current.Status,
                Note = current.Note ?? string.Empty
            };
        }

        /// <summary>Tên ca hiển thị: dùng ghi chú sẵn có, nếu không thì phân loại theo khung giờ.</summary>
        private static string DescribeShiftName(WorkSchedule schedule)
        {
            if (!string.IsNullOrWhiteSpace(schedule.Note))
            {
                return schedule.Note.Trim();
            }

            var start = schedule.StartTime;
            if (start >= new TimeOnly(19, 0))
            {
                return "Ca tối";
            }

            if (start >= new TimeOnly(13, 0))
            {
                return "Ca chiều";
            }

            return "Ca sáng";
        }

        private void TrySetTimestamp(object entity, string propertyName)
        {
            try
            {
                var property = _context.Entry(entity).Property(propertyName);
                property.CurrentValue = DateTime.Now;
            }
            catch (InvalidOperationException)
            {
                // Entity không có cột này trong schema → bỏ qua, không phải lỗi nghiệp vụ.
            }
        }

        private void TrySetCreatedAtIfMissing(object entity)
        {
            try
            {
                var property = _context.Entry(entity).Property("CreatedAt");
                var created = property.CurrentValue;
                if (created is null || (created is DateTime dt && dt < new DateTime(1753, 1, 1)))
                {
                    property.CurrentValue = DateTime.Now;
                }
            }
            catch (InvalidOperationException)
            {
                // Entity không có cột CreatedAt → bỏ qua.
            }
        }
    }
}
