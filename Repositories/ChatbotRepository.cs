using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Repositories
{
    /// <summary>
    /// Lớp truy vấn dữ liệu cho chatbot. Giữ nguyên văn biểu thức truy vấn và định dạng
    /// hiển thị (tiếng Việt) từ bản gốc OJT (Services/ChatbotService.cs).
    /// </summary>
    public sealed class ChatbotRepository : IChatbotRepository
    {
        private readonly ShopPetDatabaseContext _db;

        public ChatbotRepository(ShopPetDatabaseContext db)
        {
            _db = db;
        }

        public async Task<string?> GetCustomerNameAsync(int customerId, CancellationToken ct = default)
        {
            return await _db.Customers
                .AsNoTracking()
                .Where(c => c.CustomerId == customerId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<IReadOnlyList<ChatbotPetItem>> GetCustomerPetsAsync(int customerId, CancellationToken ct = default)
        {
            var pets = await _db.Pets
                .AsNoTracking()
                .Where(p => p.CustomerId == customerId && p.DeletedAt == null)
                .Include(p => p.Breed)
                    .ThenInclude(b => b!.Species)
                .Take(10)
                .ToListAsync(ct);

            return pets
                .Where(p => p.Age > 0 && p.Age < 50)
                .Select(p => new ChatbotPetItem(p.Id, p.PetName, p.Age))
                .ToList();
        }

        public async Task<IReadOnlyList<string>> GetRecentOrdersAsync(int customerId, CancellationToken ct = default)
        {
            return await _db.Orders
                .AsNoTracking()
                .Where(o => o.CustomerId == customerId)
                .OrderByDescending(o => o.OrderDate)
                .Take(5)
                .Select(o => $"Đơn hàng #{o.OrderId} - {o.Status} - {o.TotalAmount:N0}đ")
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<string>> GetRecentBookingsAsync(int customerId, CancellationToken ct = default)
        {
            return await _db.Bookings
                .AsNoTracking()
                .Where(b => b.CustomerId == customerId)
                .OrderByDescending(b => b.CreatedAt)
                .Take(5)
                .Select(b => $"Lịch hẹn #{b.BookingId} - {b.Status} - {b.AppointmentStart:dd/MM/yyyy HH:mm}")
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<string>> GetStaffListAsync(CancellationToken ct = default)
        {
            return await _db.Staff
                .AsNoTracking()
                .Select(s => $"{s.Name} - {s.Position}")
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<string>> GetDoctorListAsync(CancellationToken ct = default)
        {
            return await _db.Doctors
                .AsNoTracking()
                .Select(d => $"{d.Name} - {d.Specialization}")
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<string>> GetProductSummariesAsync(CancellationToken ct = default)
        {
            var products = await _db.Products
                .AsNoTracking()
                .Where(p => p.StockQuantity > 0)
                .OrderByDescending(p => p.ProductId)
                .Take(50)
                .Select(p => $"{p.Name} - {p.Price:N0}đ (Còn {p.StockQuantity})")
                .ToListAsync(ct);

            return products
                .Where(p => !p.StartsWith("Allain Kirito") && !p.StartsWith("Nguyễn Văn A"))
                .ToList();
        }

        public async Task<IReadOnlyList<string>> GetServiceSummariesAsync(CancellationToken ct = default)
        {
            return await _db.PetServices
                .AsNoTracking()
                .Where(s => s.Status == "active")
                .Select(s => $"{s.Name} - {s.Price:N0}đ - {s.Duration} phút")
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<string>> GetProductCategoryNamesAsync(CancellationToken ct = default)
        {
            return await _db.ProductCategories
                .AsNoTracking()
                .Select(c => c.Name)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<string>> GetSpeciesNamesAsync(CancellationToken ct = default)
        {
            return await _db.Species
                .AsNoTracking()
                .Select(s => s.SpeciesName)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<string>> GetBoardingRoomSummariesAsync(CancellationToken ct = default)
        {
            return await _db.BoardingRooms
                .AsNoTracking()
                .Select(r => $"{r.RoomName} - {r.RoomType} - {r.PricePerDay:N0}đ/ngày")
                .ToListAsync(ct);
        }

        public async Task<int> CountProductsAsync(string? productType, string? pet, CancellationToken ct = default)
        {
            var query = _db.Products
                .Include(p => p.Category)
                .AsQueryable();

            if (!string.IsNullOrEmpty(productType))
            {
                query = query.Where(p => (p.Category != null && p.Category.Name.Contains(productType)) || p.Name.Contains(productType));
            }

            if (!string.IsNullOrEmpty(pet))
            {
                query = query.Where(p => p.Name.Contains(pet) || (p.Description != null && p.Description.Contains(pet)));
            }

            return await query.CountAsync(ct);
        }

        public async Task<IReadOnlyList<ChatbotProductItem>> FindProductsByKeywordsAsync(
            IReadOnlyList<string> keywords,
            int take,
            CancellationToken ct = default)
        {
            var baseProductQ = _db.Products.Include(p => p.Category).AsNoTracking();
            IQueryable<Product> andQ = baseProductQ;
            foreach (var kw in keywords)
            {
                var k = kw;
                andQ = andQ.Where(p => p.Name.Contains(k) || (p.Category != null && p.Category.Name.Contains(k)) || (p.Description != null && p.Description.Contains(k)));
            }

            var products = await andQ.ToListAsync(ct);

            if (products.Count == 0)
            {
                var orBag = new List<Product>();
                foreach (var kw in keywords)
                {
                    var k = kw;
                    orBag.AddRange(await baseProductQ.Where(p => p.Name.Contains(k) || (p.Category != null && p.Category.Name.Contains(k))).ToListAsync(ct));
                }
                products = orBag.DistinctBy(p => p.ProductId).ToList();
            }

            return products
                .Select(MapProduct)
                .ToList();
        }

        public async Task<IReadOnlyList<ChatbotServiceItem>> FindServicesByKeywordsAsync(
            IReadOnlyList<string> keywords,
            CancellationToken ct = default)
        {
            var serviceBag = new List<PetService>();
            foreach (var kw in keywords)
            {
                var k = kw;
                serviceBag.AddRange(await _db.PetServices.AsNoTracking()
                    .Where(s => s.Status == "active" && (s.Name.Contains(k) || (s.Description != null && s.Description.Contains(k))))
                    .ToListAsync(ct));
            }

            return serviceBag
                .DistinctBy(s => s.ServiceId)
                .Select(MapService)
                .ToList();
        }

        public async Task<IReadOnlyList<ChatbotRoomItem>> FindRoomsByKeywordsAsync(
            IReadOnlyList<string> keywords,
            CancellationToken ct = default)
        {
            var roomBag = new List<BoardingRoom>();
            foreach (var kw in keywords)
            {
                var k = kw;
                roomBag.AddRange(await _db.BoardingRooms.AsNoTracking()
                    .Where(r => r.RoomName.Contains(k) || r.RoomType.Contains(k) || (r.Description != null && r.Description.Contains(k)))
                    .ToListAsync(ct));
            }

            return roomBag
                .DistinctBy(r => r.RoomId)
                .Select(MapRoom)
                .ToList();
        }

        public async Task<IReadOnlyList<ChatbotProductItem>> FindProductsByNameAsync(
            IReadOnlyList<string> keywords,
            int take,
            CancellationToken ct = default)
        {
            var query = _db.Products.AsQueryable();
            foreach (var kw in keywords)
            {
                var k = kw;
                query = query.Where(p => p.Name.Contains(k));
            }

            var products = await query.Take(take).ToListAsync(ct);

            return products
                .Select(MapProduct)
                .ToList();
        }

        public async Task<IReadOnlyList<ChatbotServiceItem>> GetActiveServicesAsync(int take, CancellationToken ct = default)
        {
            var services = await _db.PetServices
                .Where(s => s.Status == "active")
                .Take(take)
                .ToListAsync(ct);

            return services
                .Select(MapService)
                .ToList();
        }

        public async Task<IReadOnlyList<ChatbotOrderItem>> GetCustomerOrdersAsync(int customerId, int take, CancellationToken ct = default)
        {
            var orders = await _db.Orders
                .Where(o => o.CustomerId == customerId)
                .OrderByDescending(o => o.OrderDate)
                .Take(take)
                .ToListAsync(ct);

            return orders
                .Select(o => new ChatbotOrderItem(o.OrderId, o.Status, o.TotalAmount))
                .ToList();
        }

        private static ChatbotProductItem MapProduct(Product p) => new(
            p.ProductId,
            p.Name ?? string.Empty,
            p.Category?.Name,
            p.Description,
            p.Price,
            p.StockQuantity);

        private static ChatbotServiceItem MapService(PetService s) => new(
            s.ServiceId,
            s.Name ?? string.Empty,
            s.Description,
            s.ServiceType ?? string.Empty,
            s.Price,
            s.Duration);

        private static ChatbotRoomItem MapRoom(BoardingRoom r) => new(
            r.RoomId,
            r.RoomName ?? string.Empty,
            r.RoomType ?? string.Empty,
            r.Description,
            r.PricePerDay,
            r.Rooms);
    }
}
