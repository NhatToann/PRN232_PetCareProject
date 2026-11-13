using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Repositories
{
    /// <summary>
    /// Lớp truy cập dữ liệu cho các báo cáo/dashboard của khu vực nhân viên.
    /// Không dùng SQL thô — toàn bộ truy vấn chuyển được sang LINQ trên EF Core.
    ///
    /// <para>
    /// Dùng <see cref="IDbContextFactory{TContext}"/> thay vì <c>DbContext</c> scoped
    /// để mỗi thao tác có context riêng ⇒ nhiều truy vấn chạy song song (Blazor Server
    /// render layout + page cùng lúc) không còn lỗi
    /// "A second operation was started on this context instance".
    /// </para>
    /// </summary>
    public sealed class StaffReportsRepository : IStaffReportsRepository
    {
        private readonly IDbContextFactory<ShopPetDatabaseContext> _factory;

        public StaffReportsRepository(IDbContextFactory<ShopPetDatabaseContext> factory)
        {
            _factory = factory;
        }

        public async Task<IReadOnlyList<Order>> GetAllOrdersAsync(CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            return await context.Orders
                .AsNoTracking()
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Order>> GetRecentOrdersAsync(int take, CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            return await context.Orders
                .AsNoTracking()
                .Take(take)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<(int CustomerId, string Name)>> GetCustomerNamesAsync(
            IReadOnlyCollection<int> customerIds,
            CancellationToken ct = default)
        {
            if (customerIds.Count == 0)
            {
                return Array.Empty<(int, string)>();
            }

            await using var context = await _factory.CreateDbContextAsync(ct);
            return await context.Customers
                .AsNoTracking()
                .Where(c => customerIds.Contains(c.CustomerId))
                .Select(c => new ValueTuple<int, string>(c.CustomerId, c.Name ?? string.Empty))
                .ToListAsync(ct);
        }

        public async Task<bool> UpdateOrderStatusAsync(int orderId, string status, CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            var order = await context.Orders
                .AsTracking()
                .FirstOrDefaultAsync(o => o.OrderId == orderId, ct);

            if (order is null)
            {
                return false;
            }

            order.Status = status;
            await context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<IReadOnlyList<Booking>> GetCompletedBookingsAsync(CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            return await context.Bookings
                .AsNoTracking()
                .Include(b => b.BookingServices).ThenInclude(bs => bs.Service)
                .Where(b => b.Status == "Hoàn thành")
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Booking>> GetAllBookingsAsync(CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            return await context.Bookings
                .AsNoTracking()
                .Include(b => b.Customer)
                .Include(b => b.Pet).ThenInclude(p => p.Breed)
                // Nạp luôn dịch vụ của booking để tên dịch vụ + giá lấy từ DB
                // (thay vì chuỗi hardcode "Tắm sấy trọn gói - 250.000đ").
                .Include(b => b.BookingServices).ThenInclude(bs => bs.Service)
                .OrderBy(b => b.AppointmentStart)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Payment>> GetPaidSpaPaymentsAsync(CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            return await context.Payments
                .AsNoTracking()
                .Where(p => p.PaymentType == "spa" && p.ReferenceId != null)
                .Where(p => p.PaymentStatus == "paid"
                         || p.PaymentStatus == "Đã thanh toán"
                         || p.PaymentStatus == "completed")
                .ToListAsync(ct);
        }

        public async Task<bool> UpdateBookingStatusAsync(int bookingId, string status, CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            var booking = await context.Bookings
                .AsTracking()
                .FirstOrDefaultAsync(b => b.BookingId == bookingId, ct);

            if (booking is null)
            {
                return false;
            }

            booking.Status = status;
            await context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<IReadOnlyList<(int ProductId, int Quantity)>> GetSoldQuantitiesAsync(CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            return await context.OrderDetails
                .AsNoTracking()
                .Where(d => d.ProductId != null)
                .Select(d => new ValueTuple<int, int>(d.ProductId!.Value, d.Quantity ?? 0))
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Product>> GetAllProductsAsync(CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            return await context.Products
                .AsNoTracking()
                .ToListAsync(ct);
        }

        public async Task<int> CountLowStockProductsAsync(CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            return await context.Products
                .AsNoTracking()
                .CountAsync(p => p.StockQuantity <= 5 && !p.IsDeleted, ct);
        }

        public async Task<int> CountCustomersAsync(CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            return await context.Customers
                .AsNoTracking()
                .CountAsync(ct);
        }
    }
}
