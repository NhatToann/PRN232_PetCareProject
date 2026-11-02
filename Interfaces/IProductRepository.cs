using PetShop.Models;

namespace PetShop.Interfaces
{
    public interface IProductRepository
    {
        Task<IReadOnlyList<ProductDto>> GetAllAsync(string? keyword = null, int? categoryId = null, CancellationToken ct = default);
        Task<IReadOnlyList<ProductCategoryFilterDto>> GetCategoriesAsync(string? keyword = null, CancellationToken ct = default);
        Task<ProductDto?> GetByIdAsync(int productId, CancellationToken ct = default);
    }

}
