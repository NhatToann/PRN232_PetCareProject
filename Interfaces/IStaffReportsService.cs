using PetShop.Models;

namespace PetShop.Interfaces
{
    /// <summary>
    /// Nghiệp vụ báo cáo/dashboard cho khu vực nhân viên.
    /// </summary>
    public interface IStaffReportsService
    {
        Task<StaffRevenueSummaryDto> GetRevenueSummaryAsync(CancellationToken ct = default);

        Task<StaffDashboardMetricsDto> GetDashboardMetricsAsync(CancellationToken ct = default);

        Task<IReadOnlyList<StaffBookingListItemDto>> GetBookingListAsync(CancellationToken ct = default);

        Task<IReadOnlyList<StaffOrderListItemDto>> GetOrderListAsync(int take, CancellationToken ct = default);

        Task<bool> UpdateBookingStatusAsync(int bookingId, string newStatus, CancellationToken ct = default);

        Task<bool> UpdateOrderStatusAsync(int orderId, string newStatus, CancellationToken ct = default);
    }
}
