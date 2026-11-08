using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services
{
    /// <summary>
    /// Nghiệp vụ màn hình quản lý sản phẩm.
    ///
    /// Ghi chú (AGENTS.md §2.2): các giá trị DateTime trong entity có thể xuất hiện
    /// trước năm 1753 (mốc hợp lệ của SQL Server). Trước đây Razor tự quét property
    /// để vá giá trị này; nay chuyển vào service để Razor không cần biết EF.
    /// </summary>
    public sealed class ProductManagementService : IProductManagementService
    {
        /// <summary>Mốc thời gian nhỏ nhất mà SQL Server chấp nhận cho DATETIME.</summary>
        private static readonly DateTime SqlServerMinDate = new(1753, 1, 1);

        private readonly IProductManagementRepository _repository;
        private readonly ILogger<ProductManagementService> _logger;

        public ProductManagementService(
            IProductManagementRepository repository,
            ILogger<ProductManagementService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public Task<IReadOnlyList<Product>> GetActiveProductsAsync(CancellationToken ct = default)
            => _repository.GetActiveProductsAsync(ct);

        public Task<IReadOnlyList<Product>> GetDeletedProductsAsync(CancellationToken ct = default)
            => _repository.GetDeletedProductsAsync(ct);

        public Task<Product?> GetForEditAsync(int productId, CancellationToken ct = default)
            => _repository.GetByIdAsync(productId, ct);

        public Task<Product> CreateAsync(Product product, CancellationToken ct = default)
        {
            // Ảnh mặc định giữ nguyên như trước khi tách lớp (UI không đổi).
            if (string.IsNullOrEmpty(product.ImageUrl))
            {
                product.ImageUrl = "https://ui-avatars.com/api/?name=Product&background=3b82f6&color=fff&size=250";
            }

            NormalizeInvalidDates(product);

            return _repository.CreateAsync(product, ct);
        }

        public async Task<bool> UpdateAsync(Product product, CancellationToken ct = default)
        {
            if (product is null)
            {
                return false;
            }

            NormalizeInvalidDates(product);

            try
            {
                await _repository.UpdateAsync(product, ct);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không cập nhật được sản phẩm {ProductId}.", product.ProductId);
                throw;
            }
        }

        public Task<bool> SoftDeleteAsync(int productId, CancellationToken ct = default)
            => _repository.SoftDeleteAsync(productId, ct);

        public Task<bool> RestoreAsync(int productId, CancellationToken ct = default)
            => _repository.RestoreAsync(productId, ct);

        public Task<bool> PermanentDeleteAsync(int productId, CancellationToken ct = default)
            => _repository.PermanentDeleteAsync(productId, ct);

        public Task<bool> IncreaseStockAsync(int productId, int quantity, CancellationToken ct = default)
        {
            if (quantity <= 0)
            {
                return Task.FromResult(false);
            }

            return _repository.IncreaseStockAsync(productId, quantity, ct);
        }

        /// <summary>
        /// SQL Server không chấp nhận DateTime trước năm 1753. Quét các property kiểu
        /// DateTime/DateTime? và đưa giá trị không hợp lệ về hiện tại.
        /// </summary>
        private static void NormalizeInvalidDates(Product product)
        {
            foreach (var prop in product.GetType().GetProperties())
            {
                if (prop.PropertyType != typeof(DateTime) && prop.PropertyType != typeof(DateTime?))
                {
                    continue;
                }

                // DateTime? khi box sẽ thành DateTime (hoặc null), nên dùng DateTime cho cả hai nhánh.
                if (prop.GetValue(product) is DateTime val && val < SqlServerMinDate)
                {
                    prop.SetValue(product, DateTime.Now);
                }
            }
        }
    }
}
