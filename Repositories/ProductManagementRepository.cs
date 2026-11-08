using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Repositories
{
    /// <summary>
    /// Lớp truy cập dữ liệu cho màn hình quản lý sản phẩm.
    /// Mọi truy vấn EF đều nằm ở đây — Razor không được chạm trực tiếp DbContext.
    /// </summary>
    public sealed class ProductManagementRepository : IProductManagementRepository
    {
        private readonly ShopPetDatabaseContext _context;

        public ProductManagementRepository(ShopPetDatabaseContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<Product>> GetActiveProductsAsync(CancellationToken ct = default)
        {
            return await _context.Products
                .AsNoTracking()
                .Where(p => !p.IsDeleted)
                .OrderByDescending(p => p.ProductId)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Product>> GetDeletedProductsAsync(CancellationToken ct = default)
        {
            return await _context.Products
                .AsNoTracking()
                .Where(p => p.IsDeleted)
                .ToListAsync(ct);
        }

        public async Task<Product?> GetByIdAsync(int productId, CancellationToken ct = default)
        {
            return await _context.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProductId == productId, ct);
        }

        public async Task<Product> CreateAsync(Product product, CancellationToken ct = default)
        {
            _context.Products.Add(product);
            await _context.SaveChangesAsync(ct);
            return product;
        }

        public async Task UpdateAsync(Product product, CancellationToken ct = default)
        {
            _context.Products.Update(product);
            await _context.SaveChangesAsync(ct);
        }

        public async Task<bool> SoftDeleteAsync(int productId, CancellationToken ct = default)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

            if (product is null)
            {
                return false;
            }

            product.IsDeleted = true;
            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> RestoreAsync(int productId, CancellationToken ct = default)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

            if (product is null)
            {
                return false;
            }

            product.IsDeleted = false;
            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> PermanentDeleteAsync(int productId, CancellationToken ct = default)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

            if (product is null)
            {
                return false;
            }

            _context.Products.Remove(product);
            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> IncreaseStockAsync(int productId, int quantity, CancellationToken ct = default)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

            if (product is null)
            {
                return false;
            }

            product.StockQuantity += quantity;
            await _context.SaveChangesAsync(ct);
            return true;
        }
    }
}
