using PetShop.Models;

namespace PetShop.Interfaces
{
    public interface IPetServiceRepository
    {
        Task<IReadOnlyList<PetServiceDto>> GetAllAsync(CancellationToken ct = default);
        Task<PetServiceDetailDto?> GetDetailAsync(int serviceId, CancellationToken ct = default);

        // LS-04: POST api/pet-services
        Task<PetServiceDto?> CreateAsync(PetServiceCreateRequest request, CancellationToken ct = default);
    }
}
