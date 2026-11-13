using Microsoft.EntityFrameworkCore;
using PetShop.Components.Shared;
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
                .Select(p => new ChatbotPetItem(p.Id, p.PetName, p.Age))
                .ToList();
        }

        public async Task<IReadOnlyList<string>> GetRecentOrdersAsync(int customerId, CancellationToken ct = default)
        {
            // Ghi chú: định dạng tiền VND chuẩn hoá phải chạy ở TẠNG SỐ (client-side)
            // vì hàm helper không dịch được sang SQL ⇒ project dữ liệu thô rồi format.
            var rows = await _db.Orders
                .AsNoTracking()
                .Where(o => o.CustomerId == customerId)
                .OrderByDescending(o => o.OrderDate)
                .Take(5)
                .Select(o => new { o.OrderId, o.Status, o.TotalAmount })
                .ToListAsync(ct);

            return rows
                .Select(o => $"Đơn hàng #{o.OrderId} - {o.Status} - {Format.Vnd(o.TotalAmount)}")
                .ToList();
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
            var rows = await _db.Products
                .AsNoTracking()
                .Where(p => !p.IsDeleted && p.StockQuantity > 0)
                .OrderBy(p => p.ProductId)
                .Take(60)
                .Select(p => new { p.Name, p.Price, p.StockQuantity })
                .ToListAsync(ct);

            return rows
                .Select(p => $"{p.Name} - {Format.Vnd(p.Price)} (Còn {p.StockQuantity})")
                .Where(p => !p.StartsWith("Allain Kirito") && !p.StartsWith("Nguyễn Văn A"))
                .ToList();
        }

        public async Task<IReadOnlyList<string>> GetServiceSummariesAsync(CancellationToken ct = default)
        {
            var rows = await _db.PetServices
                .AsNoTracking()
                .Where(s => s.Status == "active")
                .Select(s => new { s.Name, s.Price, s.Duration })
                .ToListAsync(ct);

            return rows
                .Select(s => $"{s.Name} - {Format.Vnd(s.Price)} - {s.Duration} phút")
                .ToList();
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
            var rows = await _db.BoardingRooms
                .AsNoTracking()
                .Select(r => new { r.RoomName, r.RoomType, r.PricePerDay })
                .ToListAsync(ct);

            return rows
                .Select(r => $"{r.RoomName} - {r.RoomType} - {Format.Vnd(r.PricePerDay)}/ngày")
                .ToList();
        }

        /// <summary>
        /// Đếm sản phẩm theo điều kiện (loại sản phẩm / loài thú / khoảng giá).
        ///
        /// <para>
        /// <b>LỖI CŨ (11/11/2026):</b> không lọc <c>IsDeleted</c> ⇒ đếm cả sản phẩm
        /// đã bị xoá mềm ⇒ trả về "42 sản phẩm" trong khi shop thực tế chỉ còn 40.
        /// </para>
        /// </summary>
        public async Task<int> CountProductsAsync(
            string? productType,
            string? pet,
            decimal? minPrice = null,
            decimal? maxPrice = null,
            bool maxPriceInclusive = true,
            CancellationToken ct = default)
        {
            var query = _db.Products
                .Include(p => p.Category)
                .AsQueryable();

            // Sản phẩm đã xoá mềm KHÔNG được tính vào kết quả.
            query = query.Where(p => !p.IsDeleted);

            if (!string.IsNullOrEmpty(productType))
            {
                query = query.Where(p => (p.Category != null && p.Category.Name.Contains(productType)) || p.Name.Contains(productType));
            }

            if (!string.IsNullOrEmpty(pet))
            {
                query = query.Where(p => p.Name.Contains(pet) || (p.Description != null && p.Description.Contains(pet)));
            }

            if (minPrice.HasValue)
            {
                query = query.Where(p => p.Price >= minPrice.Value);
            }

            if (maxPrice.HasValue)
            {
                query = maxPriceInclusive
                    ? query.Where(p => p.Price <= maxPrice.Value)
                    : query.Where(p => p.Price < maxPrice.Value);
            }

            return await query.CountAsync(ct);
        }

        public async Task<IReadOnlyList<ChatbotProductItem>> FindProductsByKeywordsAsync(
            IReadOnlyList<string> keywords,
            int take,
            CancellationToken ct = default)
        {
            var baseProductQ = _db.Products.Include(p => p.Category).AsNoTracking().Where(p => !p.IsDeleted);
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

        /// <summary>
        /// Tìm sản phẩm theo khoảng giá (có thể kèm loại sản phẩm / loài thú).
        ///
        /// <para>
        /// <b>VÌ SAO CẦN (11/11/2026):</b> khách hỏi "món nào dưới 20k" nhưng
        /// trước đây chatbot không hề lọc theo giá ⇒ đếm ra tổng số sản phẩm
        /// (42) và trả về câu template cố định ⇒ người dùng tưởng bị hardcode.
        /// </para>
        /// </summary>
        public async Task<IReadOnlyList<ChatbotProductItem>> FindProductsByPriceRangeAsync(
            decimal? minPrice,
            decimal? maxPrice,
            bool maxPriceInclusive = true,
            string? productType = null,
            string? pet = null,
            int take = 20,
            CancellationToken ct = default)
        {
            var query = _db.Products
                .Include(p => p.Category)
                .AsNoTracking()
                .Where(p => !p.IsDeleted);

            if (!string.IsNullOrEmpty(productType))
            {
                query = query.Where(p => (p.Category != null && p.Category.Name.Contains(productType)) || p.Name.Contains(productType));
            }

            if (!string.IsNullOrEmpty(pet))
            {
                query = query.Where(p => p.Name.Contains(pet) || (p.Description != null && p.Description.Contains(pet)));
            }

            if (minPrice.HasValue)
            {
                query = query.Where(p => p.Price >= minPrice.Value);
            }

            if (maxPrice.HasValue)
            {
                query = maxPriceInclusive
                    ? query.Where(p => p.Price <= maxPrice.Value)
                    : query.Where(p => p.Price < maxPrice.Value);
            }

            var products = await query
                .OrderBy(p => p.Price)
                .Take(take)
                .ToListAsync(ct);

            return products.Select(MapProduct).ToList();
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

        public async Task<IReadOnlyDictionary<string, string>> GetShopContactSettingsAsync(CancellationToken ct = default)
        {
            var keys = new[] { "ShopAddress", "ShopHotline", "ShopEmail", "ShopWorkingHours" };

            var rows = await _db.SystemSettings
                .AsNoTracking()
                .Where(s => keys.Contains(s.SettingKey))
                .ToListAsync(ct);

            return rows
                .GroupBy(s => s.SettingKey)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.SettingValue).FirstOrDefault() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);
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
