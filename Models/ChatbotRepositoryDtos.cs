namespace PetShop.Models
{
    /// <summary>
    /// Thú cưng của khách hàng (rút gọn cho chatbot).
    /// </summary>
    public sealed record ChatbotPetItem(int PetId, string PetName, int Age);

    /// <summary>
    /// Sản phẩm rút gọn cho chatbot (dùng cho đếm / tìm kiếm / tư vấn).
    /// </summary>
    public sealed record ChatbotProductItem(
        int ProductId,
        string Name,
        string? CategoryName,
        string? Description,
        decimal Price,
        int StockQuantity);

    /// <summary>
    /// Dịch vụ rút gọn cho chatbot.
    /// </summary>
    public sealed record ChatbotServiceItem(
        int ServiceId,
        string Name,
        string? Description,
        string ServiceType,
        decimal Price,
        int Duration);

    /// <summary>
    /// Phòng lưu trú rút gọn cho chatbot.
    /// </summary>
    public sealed record ChatbotRoomItem(
        int RoomId,
        string RoomName,
        string RoomType,
        string? Description,
        decimal PricePerDay,
        int Rooms);

    /// <summary>
    /// Đơn hàng rút gọn cho chatbot.
    /// </summary>
    public sealed record ChatbotOrderItem(int OrderId, string? Status, decimal TotalAmount);
}
