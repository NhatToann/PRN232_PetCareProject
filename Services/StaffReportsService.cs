using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services
{
    /// <summary>
    /// Nghiệp vụ báo cáo/dashboard cho khu vực nhân viên.
    ///
    /// Hằng số <see cref="SpaUnitPrice"/> giữ nguyên mốc 250.000đ/spa như bản gốc
    /// (mỗi lịch spa "Hoàn thành" tính một đơn vị).
    /// </summary>
    public sealed class StaffReportsService : IStaffReportsService
    {
        /// <summary>Giá trị đơn hàng cho mỗi lịch spa hoàn thành (giữ nguyên bản gốc).</summary>
        private const decimal SpaUnitPrice = 250000m;

        private const string SpaServiceName = "Tắm sấy trọn gói - 250.000đ";

        private readonly IStaffReportsRepository _repository;
        private readonly ILogger<StaffReportsService> _logger;

        public StaffReportsService(
            IStaffReportsRepository repository,
            ILogger<StaffReportsService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task<StaffRevenueSummaryDto> GetRevenueSummaryAsync(CancellationToken ct = default)
        {
            var now = DateTime.Now;
            var today = now.Date;
            var startOfWeek = today.AddDays(-(int)now.DayOfWeek);
            var startOfMonth = new DateTime(now.Year, now.Month, 1);

            var allOrders = await _repository.GetAllOrdersAsync(ct);
            var completedSpas = await _repository.GetCompletedBookingsAsync(ct);

            var validOrders = allOrders
                .Where(o => o.Status == "Hoàn thành" || o.PaymentStatus == "Đã thanh toán")
                .ToList();

            var spaToday = completedSpas.Count(b => b.AppointmentStart.Date == today) * SpaUnitPrice;
            var spaWeek = completedSpas.Count(b => b.AppointmentStart.Date >= startOfWeek) * SpaUnitPrice;
            var spaMonth = completedSpas.Count(b => b.AppointmentStart.Date >= startOfMonth) * SpaUnitPrice;

            var summary = new StaffRevenueSummaryDto
            {
                RevenueToday = validOrders.Where(o => o.OrderDate.Date == today).Sum(o => o.TotalAmount) + spaToday,
                RevenueWeek = validOrders.Where(o => o.OrderDate.Date >= startOfWeek).Sum(o => o.TotalAmount) + spaWeek,
                RevenueMonth = validOrders.Where(o => o.OrderDate.Date >= startOfMonth).Sum(o => o.TotalAmount) + spaMonth,
                TopSpaCount = completedSpas.Count
            };

            for (var i = 6; i >= 0; i--)
            {
                var targetDate = today.AddDays(-i);
                summary.LineLabels.Add(targetDate.ToString("dd/MM"));

                var orderSum = validOrders.Where(o => o.OrderDate.Date == targetDate).Sum(o => o.TotalAmount);
                var spaSum = completedSpas.Count(b => b.AppointmentStart.Date == targetDate) * SpaUnitPrice;
                summary.LineData.Add(orderSum + spaSum);
            }

            var monthOrders = allOrders.Where(o => o.OrderDate.Date >= startOfMonth).ToList();
            summary.CompletedOrders = monthOrders.Count(o => o.Status == "Hoàn thành" || o.Status == "Đã hoàn tất");
            summary.PendingOrders = monthOrders.Count(o => o.Status == "Chờ xác nhận"
                || o.Status == "Đang xử lý"
                || o.Status == "Đang đóng gói"
                || o.Status == "Chờ giao hàng");
            summary.CancelledOrders = monthOrders.Count(o => o.Status == "Đã hủy" || o.Status == "Đã huỷ");

            // Tổng số lượng đã bán theo sản phẩm (gộp nhiều dòng Order_Detail của cùng 1 sản phẩm).
            var soldRows = await _repository.GetSoldQuantitiesAsync(ct);
            var soldDict = soldRows
                .GroupBy(row => row.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

            var products = await _repository.GetAllProductsAsync(ct);
            summary.TopProducts = products
                .Select(p => new StaffTopProductDto
                {
                    Name = p.Name,
                    ImageUrl = string.IsNullOrEmpty(p.ImageUrl)
                        ? "https://ui-avatars.com/api/?name=SP"
                        : p.ImageUrl,
                    Sold = soldDict.TryGetValue(p.ProductId, out var sold) ? sold : 0
                })
                .OrderByDescending(x => x.Sold)
                .Take(3)
                .ToList();

            return summary;
        }

        public async Task<StaffDashboardMetricsDto> GetDashboardMetricsAsync(CancellationToken ct = default)
        {
            var now = DateTime.Now;
            var today = now.Date;
            var startOfWeek = today.AddDays(-(int)now.DayOfWeek);

            var allOrders = await _repository.GetAllOrdersAsync(ct);
            var completedSpas = await _repository.GetCompletedBookingsAsync(ct);

            var paidOrders = allOrders
                .Where(o => o.Status == "Hoàn thành" || o.PaymentStatus == "Đã thanh toán")
                .ToList();

            var spaRevToday = completedSpas.Count(b => b.AppointmentStart.Date == today) * SpaUnitPrice;
            var spaRevWeek = completedSpas.Count(b => b.AppointmentStart.Date >= startOfWeek) * SpaUnitPrice;

            var metrics = new StaffDashboardMetricsDto
            {
                RevenueToday = paidOrders.Where(o => o.OrderDate.Date == today).Sum(o => o.TotalAmount) + spaRevToday,
                RevenueWeek = paidOrders.Where(o => o.OrderDate.Date >= startOfWeek).Sum(o => o.TotalAmount) + spaRevWeek,
                PendingOrders = allOrders.Count(o => o.Status == "Chờ xác nhận"
                    || o.Status == "Chờ giao hàng"
                    || o.Status == "Đang đóng gói"
                    || o.Status == "Đang xử lý"),
                CompletedOrders = allOrders.Count(o => o.Status == "Hoàn thành" || o.Status == "Đã hoàn tất"),
                CancelledOrders = allOrders.Count(o => o.Status == "Đã hủy" || o.Status == "Đã huỷ"),
                LowStockCount = await _repository.CountLowStockProductsAsync(ct),
                TotalCustomers = await _repository.CountCustomersAsync(ct)
            };

            // Biểu đồ đường: doanh thu từng ngày trong 7 ngày (đơn hàng + spa).
            for (var i = 6; i >= 0; i--)
            {
                var targetDate = today.AddDays(-i);
                metrics.LineLabels.Add(targetDate.ToString("dd/MM"));

                var orderSum = paidOrders.Where(o => o.OrderDate.Date == targetDate).Sum(o => o.TotalAmount);
                var spaSum = completedSpas.Count(b => b.AppointmentStart.Date == targetDate) * SpaUnitPrice;
                metrics.LineData.Add(orderSum + spaSum);
            }

            metrics.TodaySpaCount = await CountTodayBookingsAsync(today, ct);

            return metrics;
        }

        public async Task<IReadOnlyList<StaffBookingListItemDto>> GetBookingListAsync(CancellationToken ct = default)
        {
            var rawBookings = await _repository.GetAllBookingsAsync(ct);

            // Tên khách / thú cưng hiển thị cố định theo bản gốc (dữ liệu demo trước đây).
            var randomNames = new[] { "Trần Bích Ngọc", "Lê Minh Khang", "Nguyễn Hoàng Yến", "Phạm Tuấn Anh", "Vũ Mai Phương" };
            var randomPets = new[] { "Lu (Corgi)", "Mập (Mèo Anh)", "Milo (Poodle)", "Bull (Pug)", "Bông (Husky)" };

            return rawBookings.Select((b, index) =>
            {
                var pet = randomPets[index % randomPets.Length];
                var breed = pet.Contains('(') ? pet.Split('(')[1].Trim(')') : string.Empty;

                return new StaffBookingListItemDto
                {
                    BookingId = b.BookingId,
                    CustomerName = randomNames[index % randomNames.Length],
                    PetName = pet.Split(' ')[0],
                    PetBreed = breed,
                    AppointmentStart = b.AppointmentStart,
                    Status = b.Status ?? "Chờ xác nhận",
                    ServiceName = SpaServiceName
                };
            }).ToList();
        }

        public async Task<IReadOnlyList<StaffOrderListItemDto>> GetOrderListAsync(int take, CancellationToken ct = default)
        {
            var rawOrders = await _repository.GetRecentOrdersAsync(take, ct);

            var custIds = rawOrders
                .Where(x => x.CustomerId.HasValue)
                .Select(x => x.CustomerId!.Value)
                .Distinct()
                .ToList();

            var customerRows = await _repository.GetCustomerNamesAsync(custIds, ct);
            var cDict = customerRows.ToDictionary(c => c.CustomerId, c => c.Name);

            return rawOrders
                .Select(o => new StaffOrderListItemDto
                {
                    OrderId = o.OrderId,
                    OrderDate = o.OrderDate,
                    TotalAmount = o.TotalAmount,
                    Status = o.Status ?? "Chờ xác nhận",
                    CustomerName = o.CustomerId.HasValue && cDict.TryGetValue(o.CustomerId.Value, out var name)
                        ? name ?? "Khách lẻ"
                        : "Khách lẻ"
                })
                // Việc cần làm nổi lên trên, việc xong chìm xuống dưới.
                .OrderBy(o => GetSortWeight(o.Status))
                .ThenByDescending(o => o.OrderDate)
                .ToList();
        }

        public async Task<bool> UpdateBookingStatusAsync(int bookingId, string newStatus, CancellationToken ct = default)
        {
            try
            {
                return await _repository.UpdateBookingStatusAsync(bookingId, newStatus, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật trạng thái booking {BookingId}.", bookingId);
                return false;
            }
        }

        public async Task<bool> UpdateOrderStatusAsync(int orderId, string newStatus, CancellationToken ct = default)
        {
            try
            {
                return await _repository.UpdateOrderStatusAsync(orderId, newStatus, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cập nhật trạng thái đơn hàng {OrderId}.", orderId);
                return false;
            }
        }

        // ── helpers ───────────────────────────────────────────────────

        /// <summary>Trọng số sắp xếp: việc cần làm nổi lên trên, việc xong chìm xuống dưới.</summary>
        private static int GetSortWeight(string status)
        {
            if (status == "Chờ xác nhận") return 1;
            if (status == "Đang đóng gói" || status == "Đang xử lý") return 2;
            if (status == "Đang giao hàng") return 3;
            return 4;
        }

        private async Task<int> CountTodayBookingsAsync(DateTime today, CancellationToken ct)
        {
            var bookings = await _repository.GetAllBookingsAsync(ct);
            return bookings.Count(b => b.AppointmentStart.Date == today);
        }
    }
}
