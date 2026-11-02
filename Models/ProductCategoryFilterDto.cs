namespace PetShop.Models
{
    public sealed record ProductCategoryFilterDto(
        int CategoryId,
        string CategoryName,
        int ProductCount
    );
}
