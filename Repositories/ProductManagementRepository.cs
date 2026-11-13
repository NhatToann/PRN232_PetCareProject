using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Repositories
{
    /// <summary>
    /// Lớp truy cập dữ liệu cho màn hình quản lý sản phẩm.
    /// Mọi truy vấn EF đều nằm ở đây — Razor không được chạm trực tiếp DbContext.
    ///
    /// <para>
    /// <b>VÌ SAO DÙNG IDbContextFactory:</b> các màn hình quản lý sản phẩm nằm
    /// trong <c>StaffWorkspaceLayout</c> — layout và page được render SONG SONG
    /// trong cùng một circuit Blazor Server. Nếu dùng chung một
    /// <c>DbContext</c> scoped, hai lệnh EF chạy đồng thời sẽ kích hoạt
    /// concurrency detector ⇒ EF Core ném
    /// <c>"A second operation was started on this context instance before a
    /// previous operation completed"</c> ⇒ trang crash.
    /// </para>
    ///
    /// <para>
    /// Nay mỗi thao tác tạo context RIÊNG ⇒ an toàn khi truy vấn chạy song song,
    /// đúng pattern đã dùng ở <c>BoardingRepository</c> / <c>StaffReportsRepository</c>.
    /// </para>
    /// </summary>
    public sealed class ProductManagementRepository : IProductManagementRepository
    {
        private readonly IDbContextFactory<ShopPetDatabaseContext> _factory;

        public ProductManagementRepository(IDbContextFactory<ShopPetDatabaseContext> factory)
        {
            _factory = factory;
        }

        public async Task<IReadOnlyList<Product>> GetActiveProductsAsync(CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);

            return await context.Products
                .AsNoTracking()
                .Where(p => !p.IsDeleted)
                .OrderByDescending(p => p.ProductId)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Product>> GetDeletedProductsAsync(CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);

            return await context.Products
                .AsNoTracking()
                .Where(p => p.IsDeleted)
                .ToListAsync(ct);
        }

        public async Task<Product?> GetByIdAsync(int productId, CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);

            return await context.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProductId == productId, ct);
        }

        public async Task<Product> CreateAsync(Product product, CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);

            context.Products.Add(product);
            await context.SaveChangesAsync(ct);
            return product;
        }

        public async Task UpdateAsync(Product product, CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);

            context.Products.Update(product);
            await context.SaveChangesAsync(ct);
        }

        public async Task<bool> SoftDeleteAsync(int productId, CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);

            var product = await context.Products
                .AsTracking()
                .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

            if (product is null)
            {
                return false;
            }

            product.IsDeleted = true;
            await context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> RestoreAsync(int productId, CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);

            var product = await context.Products
                .AsTracking()
                .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

            if (product is null)
            {
                return false;
            }

            product.IsDeleted = false;
            await context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> PermanentDeleteAsync(int productId, CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);

            var product = await context.Products
                .AsTracking()
                .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

            if (product is null)
            {
                return false;
            }

            context.Products.Remove(product);
            await context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> IncreaseStockAsync(int productId, int quantity, CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);

            var product = await context.Products
                .AsTracking()
                .FirstOrDefaultAsync(p => p.ProductId == productId, ct);

            if (product is null)
            {
                return false;
            }

            product.StockQuantity += quantity;
            await context.SaveChangesAsync(ct);
            return true;
        }
    }
}
