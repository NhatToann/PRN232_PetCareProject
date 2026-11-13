namespace PetShop.Models
{
    /// <summary>
    /// Tổng hợp doanh thu (đơn hàng + spa) dùng cho trang /revenue.
    /// Mọi con số và nhãn trục biểu đồ đều tính sẵn ở tầng service để Razor chỉ hiển thị.
    /// </summary>
    public class StaffRevenueSummaryDto
    {
        public decimal RevenueToday { get; set; }

        public decimal RevenueWeek { get; set; }

        public decimal RevenueMonth { get; set; }

        /// <summary>Nhãn trục X của biểu đồ (dd/MM), từ cũ tới mới.</summary>
        public List<string> LineLabels { get; set; } = new();

        /// <summary>Doanh thu từng ngày, khớp thứ tự với <see cref="LineLabels"/>.</summary>
        public List<decimal> LineData { get; set; } = new();

        public int CompletedOrders { get; set; }

        public int PendingOrders { get; set; }

        public int CancelledOrders { get; set; }

        public int TopSpaCount { get; set; }

        /// <summary>
        /// Phân bổ số lượt theo TỪNG DỊCH VỤ SPA thật (tên + số lượt), sắp xếp giảm dần.
        /// Dùng cho thẻ "Tỷ lệ dịch vụ Spa" ở trang /revenue.
        /// </summary>
        public List<StaffSpaServiceShareDto> SpaServiceShares { get; set; } = new();

        public List<StaffTopProductDto> TopProducts { get; set; } = new();
    }

    /// <summary>Tỷ trọng của một dịch vụ spa trong tổng số lượt spa đã hoàn thành.</summary>
    public class StaffSpaServiceShareDto
    {
        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }

        /// <summary>Tỷ lệ phần trăm (0-100) so với tổng số lượt spa.</summary>
        public decimal Percent { get; set; }
    }

    /// <summary>Sản phẩm bán chạy nhất (dùng cho thẻ "Top sản phẩm").</summary>
    public class StaffTopProductDto
    {
        public string Name { get; set; } = string.Empty;

        public int Sold { get; set; }

        public string ImageUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Chỉ số bảng điều khiển nhân viên (trang /system).
    /// </summary>
    public class StaffDashboardMetricsDto
    {
        public decimal RevenueToday { get; set; }

        public decimal RevenueWeek { get; set; }

        public int PendingOrders { get; set; }

        public int CompletedOrders { get; set; }

        public int CancelledOrders { get; set; }

        public int TodaySpaCount { get; set; }

        public int LowStockCount { get; set; }

        public int TotalCustomers { get; set; }

        public List<string> LineLabels { get; set; } = new();

        public List<decimal> LineData { get; set; } = new();
    }

    /// <summary>
    /// Một dòng lịch spa hiển thị ở trang /system/bookings.
    /// </summary>
    public class StaffBookingListItemDto
    {
        public int BookingId { get; set; }

        public string CustomerName { get; set; } = string.Empty;

        public string PetName { get; set; } = string.Empty;

        public string PetBreed { get; set; } = string.Empty;

        public DateTime AppointmentStart { get; set; }

        public string Status { get; set; } = string.Empty;

        public string ServiceName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Một dòng đơn hàng hiển thị ở trang /system/orders.
    /// </summary>
    public class StaffOrderListItemDto
    {
        public int OrderId { get; set; }

        public string CustomerName { get; set; } = string.Empty;

        public DateTime OrderDate { get; set; }

        public decimal TotalAmount { get; set; }

        public string Status { get; set; } = string.Empty;
    }
}
