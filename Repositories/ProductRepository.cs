using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Repositories
{
    public sealed class ProductRepository : IProductRepository
    {
        private readonly ShopPetDatabaseContext _db;

        public ProductRepository(ShopPetDatabaseContext db)
        {
            _db = db;
        }

        public async Task<IReadOnlyList<ProductDto>> GetAllAsync(string? keyword = null, int? categoryId = null, CancellationToken ct = default)
        {
            var query = BuildBaseQuery(keyword);

            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId.Value);
            }

            var products = await query
                .OrderBy(p => p.ProductId)
                .ToListAsync(ct);

            var productIds = products.Select(p => p.ProductId).ToList();
            var reviewStats = await _db.Reviews
                .AsNoTracking()
                .Where(r => r.ProductId != null && productIds.Contains(r.ProductId.Value))
                .GroupBy(r => r.ProductId!.Value)
                .Select(g => new
                {
                    ProductId = g.Key,
                    AvgRating = g.Average(x => (double)x.Rating),
                    ReviewCount = g.Count()
                })
                .ToListAsync(ct);

            var ratingMap = reviewStats.ToDictionary(
                x => x.ProductId,
                x => new { x.AvgRating, x.ReviewCount });

            return products
                .Select(p =>
                {
                    var hasRating = ratingMap.TryGetValue(p.ProductId, out var rs);
                    return new ProductDto(
                        p.ProductId,
                        p.Name ?? string.Empty,
                        p.Price,
                        p.StockQuantity,
                        p.Description,
                        p.ImageUrl,
                        p.CategoryId,
                        p.Category?.Name,
                        p.SupplierId,
                        p.Supplier?.NameCompany,
                        hasRating ? Math.Round(rs!.AvgRating, 1) : 0d,
                        hasRating ? rs!.ReviewCount : 0
                    );
                })
                .ToList();
        }

        public async Task<IReadOnlyList<ProductCategoryFilterDto>> GetCategoriesAsync(string? keyword = null, CancellationToken ct = default)
        {
            var query = BuildBaseQuery(keyword);

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

        private IQueryable<Product> BuildBaseQuery(string? keyword)
        {
            var query = _db.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.Supplier)
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
            var product = await _db.Products
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

            var stats = await _db.Reviews
                .AsNoTracking()
                .Where(r => r.ProductId == productId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    AvgRating = g.Average(x => (double)x.Rating),
                    ReviewCount = g.Count()
                })
                .FirstOrDefaultAsync(ct);

            if (stats is null)
            {
                return product;
            }

            return product with
            {
                AvgRating = Math.Round(stats.AvgRating, 1),
                ReviewCount = stats.ReviewCount
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
            var supplierId = await ResolveSupplierIdAsync(request.SupplierName, ct);

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

            _db.Products.Add(product);
            await _db.SaveChangesAsync(ct);

            return await GetByIdAsync(product.ProductId, ct);
        }

        public async Task<ProductDto?> UpdateAsync(ProductUpdateRequest request, CancellationToken ct = default)
        {
            var product = await _db.Products
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

            var supplierId = await ResolveSupplierIdAsync(request.SupplierName, ct);
            product.SupplierId = supplierId;

            await _db.SaveChangesAsync(ct);

            return await GetByIdAsync(product.ProductId, ct);
        }

        public async Task<bool> SoftDeleteAsync(int productId, CancellationToken ct = default)
        {
            var product = await _db.Products
                .Where(p => p.ProductId == productId)
                .FirstOrDefaultAsync(ct);

            if (product is null)
            {
                return false;
            }

            product.IsDeleted = true;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<IReadOnlyList<ProductDto>> GetDeletedAsync(CancellationToken ct = default)
        {
            var products = await _db.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.Supplier)
                .Where(p => p.IsDeleted)
                .OrderBy(p => p.ProductId)
                .ToListAsync(ct);

            return products
                .Select(p => new ProductDto(
                    p.ProductId,
                    p.Name ?? string.Empty,
                    p.Price,
                    p.StockQuantity,
                    p.Description,
                    p.ImageUrl,
                    p.CategoryId,
                    p.Category?.Name,
                    p.SupplierId,
                    p.Supplier?.NameCompany,
                    0d,
                    0
                ))
                .ToList();
        }

        public async Task<bool> RestoreAsync(int productId, CancellationToken ct = default)
        {
            var product = await _db.Products
                .Where(p => p.ProductId == productId)
                .FirstOrDefaultAsync(ct);

            if (product is null)
            {
                return false;
            }

            product.IsDeleted = false;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> PermanentDeleteAsync(int productId, CancellationToken ct = default)
        {
            var product = await _db.Products
                .Where(p => p.ProductId == productId)
                .FirstOrDefaultAsync(ct);

            if (product is null)
            {
                return false;
            }

            _db.Products.Remove(product);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        /// <summary>
        /// Tìm supplier theo tên công ty; nếu chưa có thì tạo mới để không mất dữ liệu khi update.
        /// </summary>
        private async Task<int?> ResolveSupplierIdAsync(string? supplierName, CancellationToken ct)
        {
            var name = supplierName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var existing = await _db.Suppliers
                .Where(s => s.NameCompany == name)
                .Select(s => (int?)s.SupplierId)
                .FirstOrDefaultAsync(ct);

            if (existing.HasValue)
            {
                return existing.Value;
            }

            var supplier = new Supplier { NameCompany = name };
            _db.Suppliers.Add(supplier);
            await _db.SaveChangesAsync(ct);
            return supplier.SupplierId;
        }
    }
}
