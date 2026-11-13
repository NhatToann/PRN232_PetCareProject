using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services
{
    /// <summary>
    /// Nghiệp vụ báo cáo/dashboard cho khu vực nhân viên.
    ///
    /// <para>
    /// Doanh thu spa được tính từ DỮ LIỆU THẬT: tổng <c>unit_price × quantity</c>
    /// của các dòng <c>BookingService</c> (fallback: số tiền đã thanh toán trong
    /// bảng <c>Payments</c>). Trước đây mọi lịch spa đều được tính cứng
    /// 250.000đ ⇒ báo cáo sai lệch so với thực tế.
    /// </para>
    /// </summary>
    public sealed class StaffReportsService : IStaffReportsService
    {
        /// <summary>
        /// Giá dự phòng khi lịch spa không có dòng dịch vụ lẫn giao dịch thanh toán
        /// (dữ liệu cũ tạo booking trước khi có bảng Booking_Service).
        /// </summary>
        private const decimal SpaFallbackUnitPrice = 250000m;

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
            var spaPayments = await _repository.GetPaidSpaPaymentsAsync(ct);

            // Doanh thu spa theo giá THẬT của từng booking.
            var spaAmounts = BuildSpaAmountMap(completedSpas, spaPayments);

            var validOrders = allOrders
                .Where(o => o.Status == "Hoàn thành" || o.PaymentStatus == "Đã thanh toán")
                .ToList();

            var spaToday = SumSpa(completedSpas, spaAmounts, b => b.AppointmentStart.Date == today);
            var spaWeek = SumSpa(completedSpas, spaAmounts, b => b.AppointmentStart.Date >= startOfWeek);
            var spaMonth = SumSpa(completedSpas, spaAmounts, b => b.AppointmentStart.Date >= startOfMonth);

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
                var spaSum = SumSpa(completedSpas, spaAmounts, b => b.AppointmentStart.Date == targetDate);
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

            // Tỷ lệ từng dịch vụ spa — tính từ dữ liệu thật, KHÔNG cộng số bùa
            // (trước đây là "topSpaCount + 5" và "topSpaCount + 2").
            summary.SpaServiceShares = BuildSpaServiceShares(completedSpas);

            return summary;
        }

        /// <summary>
        /// Thống kê số lượt theo từng dịch vụ spa trong các booking đã hoàn thành.
        /// </summary>
        private static List<StaffSpaServiceShareDto> BuildSpaServiceShares(IReadOnlyList<Booking> completedSpas)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var b in completedSpas)
            {
                foreach (var bs in b.BookingServices)
                {
                    var name = bs.Service?.Name;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    counts[name!] = counts.TryGetValue(name!, out var c)
                        ? c + Math.Max(bs.Quantity, 1)
                        : Math.Max(bs.Quantity, 1);
                }
            }

            var total = counts.Values.Sum();

            return counts
                .OrderByDescending(kv => kv.Value)
                .Take(6)
                .Select(kv => new StaffSpaServiceShareDto
                {
                    Name = kv.Key,
                    Count = kv.Value,
                    Percent = total > 0 ? Math.Round(kv.Value * 100m / total, 1) : 0m
                })
                .ToList();
        }

        public async Task<StaffDashboardMetricsDto> GetDashboardMetricsAsync(CancellationToken ct = default)
        {
            var now = DateTime.Now;
            var today = now.Date;
            var startOfWeek = today.AddDays(-(int)now.DayOfWeek);

            var allOrders = await _repository.GetAllOrdersAsync(ct);
            var completedSpas = await _repository.GetCompletedBookingsAsync(ct);
            var spaPayments = await _repository.GetPaidSpaPaymentsAsync(ct);

            var spaAmounts = BuildSpaAmountMap(completedSpas, spaPayments);

            var paidOrders = allOrders
                .Where(o => o.Status == "Hoàn thành" || o.PaymentStatus == "Đã thanh toán")
                .ToList();

            var spaRevToday = SumSpa(completedSpas, spaAmounts, b => b.AppointmentStart.Date == today);
            var spaRevWeek = SumSpa(completedSpas, spaAmounts, b => b.AppointmentStart.Date >= startOfWeek);

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
                var spaSum = SumSpa(completedSpas, spaAmounts, b => b.AppointmentStart.Date == targetDate);
                metrics.LineData.Add(orderSum + spaSum);
            }

            metrics.TodaySpaCount = await CountTodayBookingsAsync(today, ct);

            return metrics;
        }

        public async Task<IReadOnlyList<StaffBookingListItemDto>> GetBookingListAsync(CancellationToken ct = default)
        {
            var rawBookings = await _repository.GetAllBookingsAsync(ct);

            // KHÔNG dùng tên ngẫu nhiên từ mảng hardcode.
            // Trước đây service gán "Trần Bích Ngọc / Lu (Corgi) / ..." theo thứ tự
            // index ⇒ mọi lịch hẹn hiển thị tên khách/giống thú cưng GIẢ, không
            // khớp với dữ liệu thật trong DB. Nay lấy thẳng từ quan hệ
            // Customer / Pet / Breed / BookingService đã Include sẵn.
            return rawBookings
                .Select(b => new StaffBookingListItemDto
                {
                    BookingId = b.BookingId,
                    CustomerName = string.IsNullOrWhiteSpace(b.Customer?.Name) ? "Khách hàng" : b.Customer!.Name,
                    PetName = string.IsNullOrWhiteSpace(b.Pet?.PetName) ? "Thú cưng" : b.Pet!.PetName,
                    PetBreed = string.IsNullOrWhiteSpace(b.Pet?.Breed?.BreedName) ? "—" : b.Pet!.Breed!.BreedName,
                    AppointmentStart = b.AppointmentStart,
                    Status = string.IsNullOrWhiteSpace(b.Status) ? "Chờ xác nhận" : b.Status!,
                    ServiceName = BuildServiceName(b)
                })
                .ToList();
        }

        /// <summary>
        /// Tên dịch vụ hiển thị cho một booking — ghép từ các dòng
        /// <c>BookingService</c> trong DB. Nếu booking không có dòng dịch vụ thì
        /// dùng tên trong <c>Note</c> (nếu có), cuối cùng mới là nhãn chung.
        /// </summary>
        private static string BuildServiceName(Booking booking)
        {
            var names = booking.BookingServices
                .Where(bs => bs.Service is not null && !string.IsNullOrWhiteSpace(bs.Service.Name))
                .Select(bs => bs.Service!.Name)
                .Distinct()
                .ToList();

            if (names.Count > 0)
            {
                return string.Join(", ", names);
            }

            if (!string.IsNullOrWhiteSpace(booking.Note))
            {
                return booking.Note!.Trim();
            }

            return "Dịch vụ Spa";
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

        /// <summary>
        /// Bản đồ doanh thu (đồng) cho từng booking spa.
        ///
        /// <para>Thứ tự ưu tiên:</para>
        /// <list type="number">
        /// <item>Tổng <c>unit_price × quantity</c> của các dòng <c>BookingService</c> (giá thật).</item>
        /// <item>Số tiền đã thanh toán trong bảng <c>Payments</c> (giao dịch PayOS).</item>
        /// <item>Giá dự phòng cho booking cũ không có dòng dịch vụ lẫn giao dịch.</item>
        /// </list>
        /// </summary>
        private static Dictionary<int, decimal> BuildSpaAmountMap(
            IReadOnlyList<Booking> bookings,
            IReadOnlyList<Payment> payments)
        {
            var paymentByBooking = new Dictionary<int, decimal>();
            foreach (var p in payments)
            {
                if (p.ReferenceId is int bookingId)
                {
                    paymentByBooking[bookingId] = paymentByBooking.TryGetValue(bookingId, out var current)
                        ? current + p.Amount
                        : p.Amount;
                }
            }

            var map = new Dictionary<int, decimal>();
            foreach (var b in bookings)
            {
                var fromServices = b.BookingServices
                    .Where(bs => bs.Service != null)
                    .Sum(bs => (bs.UnitPrice ?? bs.Service?.Price ?? 0m) * (bs.Quantity <= 0 ? 1 : bs.Quantity));

                if (fromServices > 0)
                {
                    map[b.BookingId] = fromServices;
                }
                else if (paymentByBooking.TryGetValue(b.BookingId, out var paid))
                {
                    map[b.BookingId] = paid;
                }
                else
                {
                    map[b.BookingId] = SpaFallbackUnitPrice;
                }
            }

            return map;
        }

        /// <summary>Tổng doanh thu spa theo điều kiện lọc trên thời gian bắt đầu.</summary>
        private static decimal SumSpa(
            IReadOnlyList<Booking> bookings,
            IReadOnlyDictionary<int, decimal> amounts,
            Func<Booking, bool> predicate)
        {
            decimal sum = 0m;
            foreach (var b in bookings)
            {
                if (!predicate(b))
                {
                    continue;
                }

                sum += amounts.TryGetValue(b.BookingId, out var amount) ? amount : SpaFallbackUnitPrice;
            }

            return sum;
        }
    }
}
