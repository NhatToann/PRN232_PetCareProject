namespace PetShop.Models
{
    public sealed record PetServiceDto(
        int ServiceId,
        string Name,
        string? Description,
        decimal Price,
        int Duration,
        string ServiceType,
        string? Status,
        DateTime? CreatedAt,
        DateTime? UpdatedAt
    );

    /// <summary>
    /// Body JSON cho POST api/pet-services (khớp frontend/src/api/petServicesApi.js::createPetService).
    /// </summary>
    public sealed record PetServiceCreateRequest(
        string Name,
        string? Description,
        decimal Price,
        int Duration,
        string ServiceType,
        string? Status,
        string? ImageUrl
    );
}
