namespace PetShop.Interfaces
{
    /// <summary>
    /// Truy vấn cấu hình liên hệ của shop từ bảng <c>SystemSettings</c>.
    /// </summary>
    public interface IShopContactRepository
    {
        /// <summary>
        /// Đọc cấu hình liên hệ của shop (khóa: <c>ShopAddress</c>, <c>ShopHotline</c>,
        /// <c>ShopEmail</c>, <c>ShopWorkingHours</c>).
        /// </summary>
        Task<IReadOnlyDictionary<string, string>> GetShopContactSettingsAsync(CancellationToken ct = default);
    }
}
