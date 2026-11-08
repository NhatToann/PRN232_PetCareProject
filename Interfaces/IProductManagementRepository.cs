using PetShop.Models;

namespace PetShop.Interfaces
{
    /// <summary>
    /// Truy vấn dữ liệu cho màn hình quản lý sản phẩm (danh sách, kho, thùng rác, doanh thu).
    /// Tách riêng khỏi <see cref="IProductRepository"/> vốn phục vụ API bán hàng công khai.
    /// </summary>
    public interface IProductManagementRepository
    {
        // ----- Danh sách / chi tiết -----
        Task<IReadOnlyList<Product>> GetActiveProductsAsync(CancellationToken ct = default);

        Task<IReadOnlyList<Product>> GetDeletedProductsAsync(CancellationToken ct = default);

        Task<Product?> GetByIdAsync(int productId, CancellationToken ct = default);

        // ----- Ghi dữ liệu -----
        Task<Product> CreateAsync(Product product, CancellationToken ct = default);

        Task UpdateAsync(Product product, CancellationToken ct = default);

        Task<bool> SoftDeleteAsync(int productId, CancellationToken ct = default);

        Task<bool> RestoreAsync(int productId, CancellationToken ct = default);

        Task<bool> PermanentDeleteAsync(int productId, CancellationToken ct = default);

        Task<bool> IncreaseStockAsync(int productId, int quantity, CancellationToken ct = default);
    }
}
