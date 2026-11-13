using PetShop.Models;

namespace PetShop.Interfaces
{
    /// <summary>
    /// Truy vấn dữ liệu báo cáo/dashboard cho khu vực nhân viên
    /// (doanh thu, đơn hàng, lịch spa, tồn kho, khách hàng).
    /// Tách riêng khỏi <see cref="IStaffRepository"/> vốn xử lý CRUD bảng Staff.
    /// </summary>
    public interface IStaffReportsRepository
    {
        // ----- Đơn hàng -----
        Task<IReadOnlyList<Order>> GetAllOrdersAsync(CancellationToken ct = default);

        Task<IReadOnlyList<Order>> GetRecentOrdersAsync(int take, CancellationToken ct = default);

        Task<IReadOnlyList<(int CustomerId, string Name)>> GetCustomerNamesAsync(
            IReadOnlyCollection<int> customerIds,
            CancellationToken ct = default);

        Task<bool> UpdateOrderStatusAsync(int orderId, string status, CancellationToken ct = default);

        // ----- Lịch spa -----
        Task<IReadOnlyList<Booking>> GetCompletedBookingsAsync(CancellationToken ct = default);

        Task<IReadOnlyList<Booking>> GetAllBookingsAsync(CancellationToken ct = default);

        /// <summary>
        /// Các giao dịch thanh toán dịch vụ spa đã thu tiền.
        /// Dùng để tính doanh thu spa theo SỐ TIỀN THẬT thay vì nhân với
        /// một mức giá cố định hardcode (250.000đ).
        /// </summary>
        Task<IReadOnlyList<Payment>> GetPaidSpaPaymentsAsync(CancellationToken ct = default);

        Task<bool> UpdateBookingStatusAsync(int bookingId, string status, CancellationToken ct = default);

        // ----- Báo cáo sản phẩm / khách hàng -----
        Task<IReadOnlyList<(int ProductId, int Quantity)>> GetSoldQuantitiesAsync(CancellationToken ct = default);

        Task<IReadOnlyList<Product>> GetAllProductsAsync(CancellationToken ct = default);

        Task<int> CountLowStockProductsAsync(CancellationToken ct = default);

        Task<int> CountCustomersAsync(CancellationToken ct = default);
    }
}
