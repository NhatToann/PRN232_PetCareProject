using System.ComponentModel.DataAnnotations;

namespace PetShop.Models
{
    public sealed record BreedPricingDto(
        int BreedPricingId,
        int BreedId,
        string BreedName,
        string? SpeciesName,
        decimal PriceAdjustPercent
    );

    public sealed record CreateBreedPricingRequest(
        [Range(1, int.MaxValue, ErrorMessage = "Mã giống không hợp lệ.")]
        int BreedId,

        [Range(-100, 500, ErrorMessage = "Phần trăm điều chỉnh phải từ -100 đến 500.")]
        decimal PriceAdjustPercent
    );

    public sealed record UpdateBreedPricingRequest(
        [Range(-100, 500, ErrorMessage = "Phần trăm điều chỉnh phải từ -100 đến 500.")]
        decimal PriceAdjustPercent
    );
}
