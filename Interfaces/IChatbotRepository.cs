using PetShop.Models;

namespace PetShop.Interfaces
{
    /// <summary>
    /// Truy vấn dữ liệu shop cho chatbot (tách riêng khỏi service theo slide Ch02 3-layer).
    /// </summary>
    public interface IChatbotRepository
    {
        Task<string?> GetCustomerNameAsync(int customerId, CancellationToken ct = default);

        Task<IReadOnlyList<ChatbotPetItem>> GetCustomerPetsAsync(int customerId, CancellationToken ct = default);

        Task<IReadOnlyList<string>> GetRecentOrdersAsync(int customerId, CancellationToken ct = default);

        Task<IReadOnlyList<string>> GetRecentBookingsAsync(int customerId, CancellationToken ct = default);

        Task<IReadOnlyList<string>> GetStaffListAsync(CancellationToken ct = default);

        Task<IReadOnlyList<string>> GetDoctorListAsync(CancellationToken ct = default);

        Task<IReadOnlyList<string>> GetProductSummariesAsync(CancellationToken ct = default);

        Task<IReadOnlyList<string>> GetServiceSummariesAsync(CancellationToken ct = default);

        Task<IReadOnlyList<string>> GetProductCategoryNamesAsync(CancellationToken ct = default);

        Task<IReadOnlyList<string>> GetSpeciesNamesAsync(CancellationToken ct = default);

        Task<IReadOnlyList<string>> GetBoardingRoomSummariesAsync(CancellationToken ct = default);

        /// <summary>
        /// Đếm sản phẩm theo điều kiện (loại sản phẩm / loài thú / khoảng giá).
        /// Sản phẩm đã xoá mềm không được tính.
        /// </summary>
        Task<int> CountProductsAsync(
            string? productType,
            string? pet,
            decimal? minPrice = null,
            decimal? maxPrice = null,
            bool maxPriceInclusive = true,
            CancellationToken ct = default);

        /// <summary>
        /// Tìm sản phẩm theo khoảng giá (kèm lọc loại sản phẩm / loài thú).
        /// Sản phẩm đã xoá mềm không được trả về.
        /// </summary>
        Task<IReadOnlyList<ChatbotProductItem>> FindProductsByPriceRangeAsync(
            decimal? minPrice,
            decimal? maxPrice,
            bool maxPriceInclusive = true,
            string? productType = null,
            string? pet = null,
            int take = 20,
            CancellationToken ct = default);

        Task<IReadOnlyList<ChatbotProductItem>> FindProductsByKeywordsAsync(
            IReadOnlyList<string> keywords,
            int take,
            CancellationToken ct = default);

        Task<IReadOnlyList<ChatbotServiceItem>> FindServicesByKeywordsAsync(
            IReadOnlyList<string> keywords,
            CancellationToken ct = default);

        Task<IReadOnlyList<ChatbotRoomItem>> FindRoomsByKeywordsAsync(
            IReadOnlyList<string> keywords,
            CancellationToken ct = default);

        Task<IReadOnlyList<ChatbotProductItem>> FindProductsByNameAsync(
            IReadOnlyList<string> keywords,
            int take,
            CancellationToken ct = default);

        Task<IReadOnlyList<ChatbotServiceItem>> GetActiveServicesAsync(int take, CancellationToken ct = default);

        Task<IReadOnlyList<ChatbotOrderItem>> GetCustomerOrdersAsync(int customerId, int take, CancellationToken ct = default);

        /// <summary>
        /// Đọc cấu hình liên hệ của shop từ bảng <c>SystemSettings</c>
        /// (khóa: <c>ShopAddress</c>, <c>ShopHotline</c>, <c>ShopEmail</c>,
        /// <c>ShopWorkingHours</c>).
        /// </summary>
        Task<IReadOnlyDictionary<string, string>> GetShopContactSettingsAsync(CancellationToken ct = default);
    }
}
