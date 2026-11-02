using PetShop.Models;

namespace PetShop.Interfaces;

public interface IProductCatalogClient
{
    Task<ProductCatalogResult<IReadOnlyList<ProductDto>>> GetProductsAsync(string? keyword = null, int? categoryId = null, CancellationToken cancellationToken = default);

    Task<ProductCatalogResult<IReadOnlyList<ProductCategoryFilterDto>>> GetCategoriesAsync(string? keyword = null, CancellationToken cancellationToken = default);

    Task<ProductCatalogResult<ProductDto?>> GetProductAsync(int productId, CancellationToken cancellationToken = default);
}
