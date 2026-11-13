using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Repositories
{
    /// <summary>
    /// Truy vấn sản phẩm cho API <c>api/products</c> + <c>api/products-temp</c>.
    ///
    /// <para>
    /// <b>VÌ SAO DÙNG IDbContextFactory:</b> trước đây repository nhận
    /// <c>ShopPetDatabaseContext</c> scoped và dùng chung cho MỌI truy vấn.
    /// Trong Blazor Server, layout (TopBar/Footer) và page được render SONG SONG
    /// trong cùng một circuit ⇒ hai lệnh EF chạy đồng thời trên cùng một
    /// context ⇒ EF Core ném
    /// <c>"A second operation was started on this context instance before a
    /// previous operation completed"</c> ⇒ API trả 500.
    /// </para>
    ///
    /// <para>
    /// Nay <b>mỗi thao tác tạo context RIÊNG</b> qua
    /// <see cref="IDbContextFactory{TContext}"/> ⇒ không còn xung đột
    /// concurrency detector, đúng pattern đã dùng ở
    /// <c>BoardingRepository</c> / <c>StaffReportsRepository</c>.
    /// </para>
    /// </summary>
    public sealed class ProductRepository : IProductRepository
    {
        private readonly IDbContextFactory<ShopPetDatabaseContext> _factory;

        public ProductRepository(IDbContextFactory<ShopPetDatabaseContext> factory)
        {
            _factory = factory;
        }

        public async Task<IReadOnlyList<ProductDto>> GetAllAsync(string? keyword = null, int? categoryId = null, CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);

            var query = BuildBaseQuery(db, keyword);

            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }

            // TỐI ƯU RAM: PROJECT thẳng ra DTO thay vì materialize entity `Product`
            // + Include(Category) + Include(Supplier).
            //
            // Bản cũ: EF nại 3 entity cho MỖI sản phẩm rồi map sang DTO ở C#.
            //   ⇒ RAM giữ đồng thời entity gốc + DTO ⇒ gấp đôi bộ nhớ tạm.
            // Bản mới: SQL chỉ SELECT đúng các cột cần ⇒ không tạo entity ⇒
            //   RAM giảm ~50% cho truy vấn này, và EF không cần JOIN thêm bảng
            //   (Category/Supplier lấy bằng LEFT JOIN do EF sinh tự động).
            var rows = await query
                .OrderBy(p => p.ProductId)
                .Select(p => new
                {
                    p.ProductId,
                    Name = p.Name ?? string.Empty,
                    p.Price,
                    p.StockQuantity,
                    p.Description,
                    p.ImageUrl,
                    p.CategoryId,
                    CategoryName = p.Category != null ? p.Category.Name : null,
                    p.SupplierId,
                    SupplierName = p.Supplier != null ? p.Supplier.NameCompany : null
                })
                .ToListAsync(ct);

            var productIds = rows.Select(r => r.ProductId).ToList();

            // Truy vấn aggregate Reviews dùng context RIÊNG ⇒ không tranh chấp
            // với truy vấn Products ở trên (và an toàn khi chạy song song).
            var reviewStats = await LoadReviewStatsAsync(productIds, ct);

            var ratingMap = reviewStats.ToDictionary(
                x => x.ProductId,
                x => new { x.AvgRating, x.ReviewCount });

            return rows
                .Select(p =>
                {
                    var hasRating = ratingMap.TryGetValue(p.ProductId, out var rs);
                    return new ProductDto(
                        p.ProductId,
                        p.Name,
                        p.Price,
                        p.StockQuantity,
                        p.Description,
                        p.ImageUrl,
                        p.CategoryId,
                        p.CategoryName,
                        p.SupplierId,
                        p.SupplierName,
                        hasRating ? Math.Round(rs!.AvgRating, 1) : 0d,
                        hasRating ? rs!.ReviewCount : 0
                    );
                })
                .ToList();
        }

        public async Task<IReadOnlyList<string>> GetProductNamesAsync(CancellationToken ct = default)
        {
            // CHỈ lấy cột Name ⇒ payload nhỏ, không materialize entity đầy đủ.
            // Dùng cho ô tìm kiếm gợi ý ở Header (mọi trang) ⇒ nhẹ hơn nhiều
            // so với tải toàn bộ bảng Products + Category + Supplier + Reviews.
            await using var db = await _factory.CreateDbContextAsync(ct);

            return await db.Products
                .AsNoTracking()
                .Where(p => !p.IsDeleted)
                .Select(p => p.Name ?? string.Empty)
                .Distinct()
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<ProductCategoryFilterDto>> GetCategoriesAsync(string? keyword = null, CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);

            var query = BuildBaseQuery(db, keyword);

            var categoryRows = await query
                .Where(p => p.CategoryId.HasValue)
                .Select(p => new
                {
                    CategoryId = p.CategoryId,
                    CategoryName = p.Category != null ? p.Category.Name : null
                })
                .ToListAsync(ct);

            return categoryRows
                .Where(x => x.CategoryId.HasValue && !string.IsNullOrWhiteSpace(x.CategoryName))
                .GroupBy(x => new { CategoryId = x.CategoryId!.Value, CategoryName = x.CategoryName! })
                .Select(g => new ProductCategoryFilterDto(
                    g.Key.CategoryId,
                    g.Key.CategoryName,
                    g.Count()
                ))
                .OrderBy(x => x.CategoryName)
                .ToList();
        }

        private static IQueryable<Product> BuildBaseQuery(ShopPetDatabaseContext db, string? keyword)
        {
            // KHÔNG dùng Include(Category)/Include(Supplier) ở đây.
            //
            // Lý do (tối ưu RAM): Include làm EF nạp entity đầy đủ cho từng bản ghi
            // (kèm cả entity con) rồi mới map sang DTO ở C# ⇒ giữ đồng thời 2 bản
            // trong RAM. Các truy vấn đã PROJECT thẳng ra DTO nên EF tự sinh
            // LEFT JOIN cho Category/Supplier ⇒ SQL tương đương, RAM giảm rõ rệt.
            var query = db.Products
                .AsNoTracking()
                .AsQueryable();

            if (string.IsNullOrWhiteSpace(keyword))
            {
                return query;
            }

            var tokens = keyword
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var token in tokens)
            {
                var t = token;
                query = query.Where(p =>
                    EF.Functions.Like(EF.Functions.Collate(p.Name ?? string.Empty, "SQL_Latin1_General_CP1_CI_AI"), $"%{t}%") ||
                    EF.Functions.Like(EF.Functions.Collate(p.Description ?? string.Empty, "SQL_Latin1_General_CP1_CI_AI"), $"%{t}%") ||
                    EF.Functions.Like(EF.Functions.Collate(p.Category != null ? p.Category.Name : string.Empty, "SQL_Latin1_General_CP1_CI_AI"), $"%{t}%") ||
                    EF.Functions.Like(EF.Functions.Collate(p.Supplier != null ? p.Supplier.NameCompany ?? string.Empty : string.Empty, "SQL_Latin1_General_CP1_CI_AI"), $"%{t}%")
                );
            }

            return query;
        }

        public async Task<ProductDto?> GetByIdAsync(int productId, CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);

            var product = await db.Products
                .AsNoTracking()
                .Where(p => p.ProductId == productId)
                .Select(p => new ProductDto(
                    p.ProductId,
                    p.Name,
                    p.Price,
                    p.StockQuantity,
                    p.Description,
                    p.ImageUrl,
                    p.CategoryId,
                    p.Category != null ? p.Category.Name : null,
                    p.SupplierId,
                    p.Supplier != null ? p.Supplier.NameCompany : null,
                    0d,
                    0
                ))
                .FirstOrDefaultAsync(ct);

            if (product is null)
            {
                return null;
            }

            var stats = await LoadReviewStatsAsync(new[] { productId }, ct);

            var stat = stats.FirstOrDefault();
            if (stats.Count == 0)
            {
                return product;
            }

            return product with
            {
                AvgRating = Math.Round(stat.AvgRating, 1),
                ReviewCount = stat.ReviewCount
            };
        }

        // ---------------------------------------------------------------
        // LS-04: các method write/search cho api/products-temp/*
        // ---------------------------------------------------------------

        public async Task<IReadOnlyList<ProductDto>> SearchAsync(string? keyword, int? categoryId, CancellationToken ct = default)
        {
            return await GetAllAsync(keyword, categoryId, ct);
        }

        public async Task<ProductDto?> CreateAsync(ProductCreateRequest request, CancellationToken ct = default)
        {
            int? supplierId;
            int newProductId;

            // Context ghi: cô lập hoàn toàn với các truy vấn đọc chạy song song.
            await using (var db = await _factory.CreateDbContextAsync(ct))
            {
                supplierId = await ResolveSupplierIdAsync(db, request.SupplierName, ct);

                var product = new Product
                {
                    Name = request.Name,
                    Price = request.Price,
                    StockQuantity = request.StockQuantity,
                    Description = request.Description,
                    ImageUrl = request.ImageUrl,
                    CategoryId = request.CategoryId,
                    SupplierId = supplierId,
                    IsDeleted = false
                };

                db.Products.Add(product);
                await db.SaveChangesAsync(ct);

                newProductId = product.ProductId;
            }

            return await GetByIdAsync(newProductId, ct);
        }

        public async Task<ProductDto?> UpdateAsync(ProductUpdateRequest request, CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);

            var product = await db.Products
                .AsTracking()
                .Where(p => p.ProductId == request.ProductId)
                .FirstOrDefaultAsync(ct);

            if (product is null)
            {
                return null;
            }

            product.Name = request.Name;
            product.Price = request.Price;
            product.StockQuantity = request.StockQuantity;
            product.Description = request.Description;
            product.CategoryId = request.CategoryId;
            product.ImageUrl = request.ImageUrl;

            product.SupplierId = await ResolveSupplierIdAsync(db, request.SupplierName, ct);

            await db.SaveChangesAsync(ct);

            return await GetByIdAsync(product.ProductId, ct);
        }

        public async Task<bool> SoftDeleteAsync(int productId, CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);

            var product = await db.Products
                .AsTracking()
                .Where(p => p.ProductId == productId)
                .FirstOrDefaultAsync(ct);

            if (product is null)
            {
                return false;
            }

            product.IsDeleted = true;
            await db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<IReadOnlyList<ProductDto>> GetDeletedAsync(CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);

            // Project thẳng ra DTO (không Include) để tiết kiệm RAM — trang
            // "sản phẩm đã xoá" chỉ cần tên/giá/tồn kho, không cần entity đầy đủ.
            var products = await db.Products
                .AsNoTracking()
                .Where(p => p.IsDeleted)
                .OrderBy(p => p.ProductId)
                .Select(p => new
                {
                    p.ProductId,
                    Name = p.Name ?? string.Empty,
                    p.Price,
                    p.StockQuantity,
                    p.Description,
                    p.ImageUrl,
                    p.CategoryId,
                    CategoryName = p.Category != null ? p.Category.Name : null,
                    p.SupplierId,
                    SupplierName = p.Supplier != null ? p.Supplier.NameCompany : null
                })
                .ToListAsync(ct);

            return products
                .Select(p => new ProductDto(
                    p.ProductId,
                    p.Name,
                    p.Price,
                    p.StockQuantity,
                    p.Description,
                    p.ImageUrl,
                    p.CategoryId,
                    p.CategoryName,
                    p.SupplierId,
                    p.SupplierName,
                    0d,
                    0
                ))
                .ToList();
        }

        public async Task<bool> RestoreAsync(int productId, CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);

            var product = await db.Products
                .AsTracking()
                .Where(p => p.ProductId == productId)
                .FirstOrDefaultAsync(ct);

            if (product is null)
            {
                return false;
            }

            product.IsDeleted = false;
            await db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> PermanentDeleteAsync(int productId, CancellationToken ct = default)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);

            var product = await db.Products
                .AsTracking()
                .Where(p => p.ProductId == productId)
                .FirstOrDefaultAsync(ct);

            if (product is null)
            {
                return false;
            }

            db.Products.Remove(product);
            await db.SaveChangesAsync(ct);
            return true;
        }

        /// <summary>
        /// Aggregate điểm đánh giá theo danh sách productId — dùng context RIÊNG.
        /// </summary>
        private async Task<List<ProductRatingStat>> LoadReviewStatsAsync(IReadOnlyList<int> productIds, CancellationToken ct)
        {
            if (productIds.Count == 0)
            {
                return new List<ProductRatingStat>();
            }

            await using var db = await _factory.CreateDbContextAsync(ct);

            var rows = await db.Reviews
                .AsNoTracking()
                .Where(r => r.ProductId != null && productIds.Contains(r.ProductId.Value))
                .GroupBy(r => r.ProductId!.Value)
                .Select(g => new ProductRatingStat(
                    g.Key,
                    g.Average(x => (double)x.Rating),
                    g.Count()
                ))
                .ToListAsync(ct);

            return rows;
        }

        /// <summary>
        /// Tìm supplier theo tên công ty; nếu chưa có thì tạo mới để không mất dữ liệu khi update.
        /// </summary>
        private static async Task<int?> ResolveSupplierIdAsync(ShopPetDatabaseContext db, string? supplierName, CancellationToken ct)
        {
            var name = supplierName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var existing = await db.Suppliers
                .AsNoTracking()
                .Where(s => s.NameCompany == name)
                .Select(s => (int?)s.SupplierId)
                .FirstOrDefaultAsync(ct);

            if (existing.HasValue)
            {
                return existing.Value;
            }

            var supplier = new Supplier { NameCompany = name };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync(ct);
            return supplier.SupplierId;
        }
    }

    /// <summary>Kết quả aggregate đánh giá của một sản phẩm.</summary>
    /// <param name="ProductId">Id sản phẩm.</param>
    /// <param name="AvgRating">Điểm trung bình.</param>
    /// <param name="ReviewCount">Số lượt đánh giá.</param>
    public readonly record struct ProductRatingStat(int ProductId, double AvgRating, int ReviewCount);
}
