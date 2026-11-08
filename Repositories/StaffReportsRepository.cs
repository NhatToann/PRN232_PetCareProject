using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Repositories
{
    /// <summary>
    /// Lớp truy cập dữ liệu cho các báo cáo/dashboard của khu vực nhân viên.
    /// Không dùng SQL thô — toàn bộ truy vấn chuyển được sang LINQ trên EF Core.
    /// </summary>
    public sealed class StaffReportsRepository : IStaffReportsRepository
    {
        private readonly ShopPetDatabaseContext _context;

        public StaffReportsRepository(ShopPetDatabaseContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<Order>> GetAllOrdersAsync(CancellationToken ct = default)
        {
            return await _context.Orders
                .AsNoTracking()
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Order>> GetRecentOrdersAsync(int take, CancellationToken ct = default)
        {
            return await _context.Orders
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

            return await _context.Customers
                .AsNoTracking()
                .Where(c => customerIds.Contains(c.CustomerId))
                .Select(c => new ValueTuple<int, string>(c.CustomerId, c.Name ?? string.Empty))
                .ToListAsync(ct);
        }

        public async Task<bool> UpdateOrderStatusAsync(int orderId, string status, CancellationToken ct = default)
        {
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.OrderId == orderId, ct);

            if (order is null)
            {
                return false;
            }

            order.Status = status;
            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<IReadOnlyList<Booking>> GetCompletedBookingsAsync(CancellationToken ct = default)
        {
            return await _context.Bookings
                .AsNoTracking()
                .Where(b => b.Status == "Hoàn thành")
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Booking>> GetAllBookingsAsync(CancellationToken ct = default)
        {
            return await _context.Bookings
                .AsNoTracking()
                .Include(b => b.Customer)
                .Include(b => b.Pet).ThenInclude(p => p.Breed)
                .OrderBy(b => b.AppointmentStart)
                .ToListAsync(ct);
        }

        public async Task<bool> UpdateBookingStatusAsync(int bookingId, string status, CancellationToken ct = default)
        {
            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.BookingId == bookingId, ct);

            if (booking is null)
            {
                return false;
            }

            booking.Status = status;
            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<IReadOnlyList<(int ProductId, int Quantity)>> GetSoldQuantitiesAsync(CancellationToken ct = default)
        {
            return await _context.OrderDetails
                .AsNoTracking()
                .Where(d => d.ProductId != null)
                .Select(d => new ValueTuple<int, int>(d.ProductId!.Value, d.Quantity ?? 0))
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Product>> GetAllProductsAsync(CancellationToken ct = default)
        {
            return await _context.Products
                .AsNoTracking()
                .ToListAsync(ct);
        }

        public Task<int> CountLowStockProductsAsync(CancellationToken ct = default)
        {
            return _context.Products
                .AsNoTracking()
                .CountAsync(p => p.StockQuantity <= 5 && !p.IsDeleted, ct);
        }

        public Task<int> CountCustomersAsync(CancellationToken ct = default)
        {
            return _context.Customers
                .AsNoTracking()
                .CountAsync(ct);
        }
    }
}
