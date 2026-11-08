using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services
{
    public sealed class ProductService : IProductService
    {
        private readonly IProductRepository _repo;

        public ProductService(IProductRepository repo)
        {
            _repo = repo;
        }

        public Task<IReadOnlyList<ProductDto>> GetAllAsync(string? keyword = null, int? categoryId = null, CancellationToken ct = default)
        {
            return _repo.GetAllAsync(keyword, categoryId, ct);
        }

        public Task<IReadOnlyList<ProductCategoryFilterDto>> GetCategoriesAsync(string? keyword = null, CancellationToken ct = default)
        {
            return _repo.GetCategoriesAsync(keyword, ct);
        }

        public Task<ProductDto?> GetByIdAsync(int productId, CancellationToken ct = default)
        {
            return _repo.GetByIdAsync(productId, ct);
        }

        public Task<IReadOnlyList<ProductDto>> SearchAsync(string? keyword, int? categoryId, CancellationToken ct = default)
        {
            return _repo.SearchAsync(keyword, categoryId, ct);
        }

        public Task<ProductDto?> CreateAsync(ProductCreateRequest request, CancellationToken ct = default)
        {
            return _repo.CreateAsync(request, ct);
        }

        public Task<ProductDto?> UpdateAsync(ProductUpdateRequest request, CancellationToken ct = default)
        {
            return _repo.UpdateAsync(request, ct);
        }

        public Task<bool> SoftDeleteAsync(int productId, CancellationToken ct = default)
        {
            return _repo.SoftDeleteAsync(productId, ct);
        }

        public Task<IReadOnlyList<ProductDto>> GetDeletedAsync(CancellationToken ct = default)
        {
            return _repo.GetDeletedAsync(ct);
        }

        public Task<bool> RestoreAsync(int productId, CancellationToken ct = default)
        {
            return _repo.RestoreAsync(productId, ct);
        }

        public Task<bool> PermanentDeleteAsync(int productId, CancellationToken ct = default)
        {
            return _repo.PermanentDeleteAsync(productId, ct);
        }
    }
}
