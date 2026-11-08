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

        Task<int> CountProductsAsync(string? productType, string? pet, CancellationToken ct = default);

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
    }
}
