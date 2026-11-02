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
    }

}
