using PetShop.Models;

namespace PetShop.Interfaces
{
    public interface IProductService
    {
        Task<IReadOnlyList<ProductDto>> GetAllAsync(string? keyword = null, int? categoryId = null, CancellationToken ct = default);

        /// <summary>Chỉ lấy danh sách TÊN sản phẩm (payload nhẹ cho ô tìm kiếm).</summary>
        Task<IReadOnlyList<string>> GetProductNamesAsync(CancellationToken ct = default);

        Task<IReadOnlyList<ProductCategoryFilterDto>> GetCategoriesAsync(string? keyword = null, CancellationToken ct = default);
        Task<ProductDto?> GetByIdAsync(int productId, CancellationToken ct = default);

        // LS-04: api/products-temp/*
        Task<IReadOnlyList<ProductDto>> SearchAsync(string? keyword, int? categoryId, CancellationToken ct = default);
        Task<ProductDto?> CreateAsync(ProductCreateRequest request, CancellationToken ct = default);
        Task<ProductDto?> UpdateAsync(ProductUpdateRequest request, CancellationToken ct = default);
        Task<bool> SoftDeleteAsync(int productId, CancellationToken ct = default);
        Task<IReadOnlyList<ProductDto>> GetDeletedAsync(CancellationToken ct = default);
        Task<bool> RestoreAsync(int productId, CancellationToken ct = default);
        Task<bool> PermanentDeleteAsync(int productId, CancellationToken ct = default);
    }
}
