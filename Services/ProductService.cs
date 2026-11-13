using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services
{
    public sealed class ProductService : IProductService
    {
        private readonly IProductRepository _repo;
        private readonly ProductNameCache _nameCache;

        public ProductService(IProductRepository repo, ProductNameCache nameCache)
        {
            _repo = repo;
            _nameCache = nameCache;
        }

        public Task<IReadOnlyList<ProductDto>> GetAllAsync(string? keyword = null, int? categoryId = null, CancellationToken ct = default)
        {
            return _repo.GetAllAsync(keyword, categoryId, ct);
        }

        /// <summary>
        /// Danh sách tên sản phẩm — có cache phía server (dùng chung cho mọi circuit).
        ///
        /// <para>
        /// Cache nằm ở tầng Service (không phải Repository) vì đây là quyết định
        /// về "dữ liệu hiển thị", không phải quy tắc truy xuất dữ liệu.
        /// </para>
        /// </summary>
        public Task<IReadOnlyList<string>> GetProductNamesAsync(CancellationToken ct = default)
        {
            return _nameCache.GetAsync(_repo.GetProductNamesAsync, ct);
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

        // ------------------------------------------------------------------
        // Mọi thao tác ghi sản phẩm đều vô hiệu hoá cache tên ⇒ ô tìm kiếm
        // không bao giờ gợi ý tên sản phẩm vừa bị xoá/đổi tên.
        // ------------------------------------------------------------------

        public async Task<ProductDto?> CreateAsync(ProductCreateRequest request, CancellationToken ct = default)
        {
            var result = await _repo.CreateAsync(request, ct);
            _nameCache.Invalidate();
            return result;
        }

        public async Task<ProductDto?> UpdateAsync(ProductUpdateRequest request, CancellationToken ct = default)
        {
            var result = await _repo.UpdateAsync(request, ct);
            _nameCache.Invalidate();
            return result;
        }

        public async Task<bool> SoftDeleteAsync(int productId, CancellationToken ct = default)
        {
            var result = await _repo.SoftDeleteAsync(productId, ct);
            _nameCache.Invalidate();
            return result;
        }

        public Task<IReadOnlyList<ProductDto>> GetDeletedAsync(CancellationToken ct = default)
        {
            return _repo.GetDeletedAsync(ct);
        }

        public async Task<bool> RestoreAsync(int productId, CancellationToken ct = default)
        {
            var result = await _repo.RestoreAsync(productId, ct);
            _nameCache.Invalidate();
            return result;
        }

        public async Task<bool> PermanentDeleteAsync(int productId, CancellationToken ct = default)
        {
            var result = await _repo.PermanentDeleteAsync(productId, ct);
            _nameCache.Invalidate();
            return result;
        }
    }
}
