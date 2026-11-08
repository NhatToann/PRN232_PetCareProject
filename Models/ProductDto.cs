namespace PetShop.Models
{
    public sealed record ProductDto(
        int ProductId,
        string Name,
        decimal Price,
        int StockQuantity,
        string? Description,
        string? ImageUrl,
        int? CategoryId,
        string? CategoryName,
        int? SupplierId,
        string? SupplierName,
        double AvgRating,
        int ReviewCount
    );

    /// <summary>
    /// Body JSON cho POST api/products-temp/create (khớp frontend/src/api/productsApi.js::createProduct).
    /// </summary>
    public sealed record ProductCreateRequest(
        string Name,
        int CategoryId,
        decimal Price,
        int StockQuantity,
        string? SupplierName,
        string? Description,
        string? ImageUrl
    );

    /// <summary>
    /// Body JSON cho POST api/products-temp/update (khớp frontend/src/api/productsApi.js::updateProduct).
    /// </summary>
    public sealed record ProductUpdateRequest(
        int ProductId,
        string Name,
        int CategoryId,
        decimal Price,
        int StockQuantity,
        string? SupplierName,
        string? Description,
        string? ImageUrl
    );

    /// <summary>
    /// Body JSON cho POST api/products-temp/delete | restore | permanent-delete
    /// (khớp frontend/src/api/productsApi.js — gửi { productId }).
    /// </summary>
    public sealed record ProductIdRequest(int ProductId);

    /// <summary>
    /// Response chung cho các action write của products-temp: { "message": "...", ... }.
    /// </summary>
    public sealed record ProductActionResponse(
        string Message,
        int? ProductId = null,
        ProductDto? Product = null
    );
}
