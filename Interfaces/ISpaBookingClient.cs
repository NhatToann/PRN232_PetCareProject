using PetShop.Models;

namespace PetShop.Interfaces;

public interface ISpaBookingClient
{
    Task<FrontendApiResult<IReadOnlyList<PetSummaryDto>>> GetPetsAsync(int currentCustomerId, CancellationToken ct = default);

    Task<FrontendApiResult<IReadOnlyList<SpaServiceDto>>> GetSpaServicesAsync(CancellationToken ct = default);

    Task<FrontendApiResult<SpaAvailabilityResponse>> CheckAvailabilityAsync(DateTime start, int durationMin, int quantity, CancellationToken ct = default);

    Task<FrontendApiResult<SpaBookingEstimateDto>> EstimateAsync(int currentCustomerId, SpaBookingEstimateRequest req, CancellationToken ct = default);

    Task<FrontendApiResult<SpaBookingDto>> CreateAsync(int currentCustomerId, CreateSpaBookingRequest req, CancellationToken ct = default);

    Task<FrontendApiResult<IReadOnlyList<SpaBookingDto>>> GetHistoryAsync(int currentCustomerId, CancellationToken ct = default);

    Task<FrontendApiResult<SpaBookingInvoiceDto?>> GetInvoiceAsync(int bookingId, int currentCustomerId, CancellationToken ct = default);

    Task<FrontendApiResult<object>> UpdateStatusAsync(int bookingId, int currentCustomerId, string status, CancellationToken ct = default);

    /// <summary>
    /// Khách hàng tự huỷ lịch spa của chính mình.
    ///
    /// <para>
    /// Dùng endpoint riêng <c>PATCH api/spa-booking/{id}/cancel</c> — endpoint
    /// <c>/status</c> yêu cầu policy StaffOrAdmin nên khách hàng luôn nhận 403.
    /// </para>
    /// </summary>
    Task<FrontendApiResult<object>> CancelBookingAsync(int bookingId, int currentCustomerId, CancellationToken ct = default);

    /// <summary>
    /// Đồng bộ trạng thái thanh toán spa từ PayOS (server-side xác minh).
    ///
    /// <para>
    /// Thay cho việc client tự PATCH status (endpoint đó yêu cầu StaffOrAdmin
    /// ⇒ khách hàng luôn 403 ⇒ luồng đặt spa hỏng).
    /// </para>
    /// </summary>
    Task<FrontendApiResult<object>> SyncPaymentAsync(int bookingId, int currentCustomerId, CancellationToken ct = default);

    /// <summary>
    /// Tra cứu booking spa theo mã đơn PayOS — dùng để khôi phục mã booking
    /// khi đường dẫn redirect về trang hóa đơn bị mất query string.
    /// </summary>
    Task<FrontendApiResult<SpaBookingDto?>> GetByOrderCodeAsync(int orderCode, int currentCustomerId, CancellationToken ct = default);

    /// <summary>
    /// Tra cứu booking spa đang thanh toán PayOS gần nhất của khách — lớp dự phòng
    /// cuối cùng khi URL redirect mất query string VÀ localStorage cũng không có.
    /// </summary>
    Task<FrontendApiResult<SpaBookingDto?>> GetLatestPayOSBookingAsync(int currentCustomerId, CancellationToken ct = default);

    Task<FrontendApiResult<object>> SubmitReviewAsync(int currentCustomerId, CreateSpaReviewRequest req, CancellationToken ct = default);

    Task<FrontendApiResult<PayOSCheckoutResult>> CreateSpaPayOSPaymentAsync(int bookingId, IReadOnlyList<int>? bookingIds, string returnUrl, string cancelUrl, CancellationToken ct = default);

    Task<FrontendApiResult<PayOSPaymentStatusResult>> GetPayOSStatusAsync(int orderCode, CancellationToken ct = default);
}