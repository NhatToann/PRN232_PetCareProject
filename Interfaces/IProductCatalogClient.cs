using PetShop.Models;

namespace PetShop.Interfaces;

public interface IProductCatalogClient
{
    Task<ProductCatalogResult<IReadOnlyList<ProductDto>>> GetProductsAsync(string? keyword = null, int? categoryId = null, CancellationToken cancellationToken = default);

    /// <summary>Danh sách tên sản phẩm — payload nhẹ cho ô tìm kiếm header.</summary>
    Task<ProductCatalogResult<IReadOnlyList<string>>> GetProductNamesAsync(CancellationToken cancellationToken = default);

    Task<ProductCatalogResult<IReadOnlyList<ProductCategoryFilterDto>>> GetCategoriesAsync(string? keyword = null, CancellationToken cancellationToken = default);

    Task<ProductCatalogResult<ProductDto?>> GetProductAsync(int productId, CancellationToken cancellationToken = default);
}
