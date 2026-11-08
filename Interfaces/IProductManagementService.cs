using PetShop.Models;

namespace PetShop.Interfaces
{
    /// <summary>
    /// Nghiệp vụ màn hình quản lý sản phẩm — tầng giữa giữa Razor và Repository.
    /// </summary>
    public interface IProductManagementService
    {
        Task<IReadOnlyList<Product>> GetActiveProductsAsync(CancellationToken ct = default);

        Task<IReadOnlyList<Product>> GetDeletedProductsAsync(CancellationToken ct = default);

        Task<Product?> GetForEditAsync(int productId, CancellationToken ct = default);

        Task<Product> CreateAsync(Product product, CancellationToken ct = default);

        Task<bool> UpdateAsync(Product product, CancellationToken ct = default);

        Task<bool> SoftDeleteAsync(int productId, CancellationToken ct = default);

        Task<bool> RestoreAsync(int productId, CancellationToken ct = default);

        Task<bool> PermanentDeleteAsync(int productId, CancellationToken ct = default);

        Task<bool> IncreaseStockAsync(int productId, int quantity, CancellationToken ct = default);
    }
}
