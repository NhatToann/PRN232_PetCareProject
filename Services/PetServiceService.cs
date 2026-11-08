using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services
{
    public sealed class PetServiceService : IPetServiceService
    {
        private readonly IPetServiceRepository _repo;

        public PetServiceService(IPetServiceRepository repo)
        {
            _repo = repo;
        }

        public Task<IReadOnlyList<PetServiceDto>> GetAllAsync(CancellationToken ct = default)
        {
            return _repo.GetAllAsync(ct);
        }

        public Task<PetServiceDetailDto?> GetDetailAsync(int serviceId, CancellationToken ct = default)
        {
            return _repo.GetDetailAsync(serviceId, ct);
        }

        public Task<PetServiceDto?> CreateAsync(PetServiceCreateRequest request, CancellationToken ct = default)
        {
            return _repo.CreateAsync(request, ct);
        }
    }
}
