using System.ComponentModel.DataAnnotations;

namespace PetShop.Models
{
    public sealed record PetManageDto(
        int Id,
        int CustomerId,
        string PetName,
        int Age,
        string Gender,
        decimal? WeightKg,
        string? Description,
        string? HealthStatus,
        string? ImagePath,
        int? BreedId,
        string? BreedName,
        int? SpeciesId,
        string? SpeciesName
    );

    public sealed record UpsertPetRequestDto(
        [Range(1, int.MaxValue, ErrorMessage = "Mã khách hàng không hợp lệ.")]
        int CustomerId,

        [Required(ErrorMessage = "Tên thú cưng không được để trống.")]
        [StringLength(100, ErrorMessage = "Tên thú cưng không được vượt quá 100 ký tự.")]
        string PetName,

        [Range(0, 50, ErrorMessage = "Tuổi phải từ 0 đến 50.")]
        int Age,

        [Required(ErrorMessage = "Giới tính không được để trống.")]
        [StringLength(20, ErrorMessage = "Giới tính không được vượt quá 20 ký tự.")]
        string Gender,

        [Range(0.1, 500, ErrorMessage = "Cân nặng phải từ 0.1 đến 500 kg.")]
        decimal? WeightKg,

        [StringLength(1000, ErrorMessage = "Mô tả không được vượt quá 1000 ký tự.")]
        string? Description,

        [StringLength(100, ErrorMessage = "Tình trạng sức khỏe không được vượt quá 100 ký tự.")]
        string? HealthStatus,

        [StringLength(255, ErrorMessage = "Đường dẫn ảnh không được vượt quá 255 ký tự.")]
        string? ImagePath,

        [Range(1, int.MaxValue, ErrorMessage = "Mã giống không hợp lệ.")]
        int? BreedId
    );

    public sealed record SpeciesOptionDto(
        int SpeciesId,
        string SpeciesName
    );

    public sealed record BreedOptionDto(
        int BreedId,
        string BreedName,
        int SpeciesId
    );
}
