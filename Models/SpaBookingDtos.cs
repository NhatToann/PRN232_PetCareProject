using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PetShop.Models
{
    public sealed record PetSummaryDto(
        int PetId,
        string PetName,
        int? BreedId,
        string? BreedName,
        string? SpeciesName,
        decimal? WeightKg
    );

    public sealed record SpaServiceDto(
        int ServiceId,
        string Name,
        string? Description,
        decimal Price,
        int Duration,
        string ServiceType,
        string? Status
    );

    public sealed record SpaCartItemRequest(
        [Range(1, int.MaxValue, ErrorMessage = "Mã dịch vụ không hợp lệ.")]
        int ServiceId,

        [Range(1, 20, ErrorMessage = "Số lượng phải từ 1 đến 20.")]
        int Quantity
    );

    public sealed record SpaBookingEstimateRequest(
        [Required(ErrorMessage = "Danh sách thú cưng không được để trống.")]
        [MinLength(1, ErrorMessage = "Phải chọn ít nhất 1 thú cưng.")]
        IReadOnlyList<int> PetIds,

        [Required(ErrorMessage = "Danh sách dịch vụ không được để trống.")]
        [MinLength(1, ErrorMessage = "Giỏ dịch vụ không được để trống.")]
        IReadOnlyList<SpaCartItemRequest> Items
    );

    public sealed record SpaBookingEstimateItemDto(
        int PetId,
        string PetName,
        int ServiceId,
        string ServiceName,
        int Quantity,
        decimal UnitPrice,
        int DurationMin
    );

    public sealed record SpaBookingEstimateDto(
        IReadOnlyList<SpaBookingEstimateItemDto> Items,
        int PerPetDurationMin,
        int TotalDurationMin,
        int PetCount,
        decimal TotalPrice
    );

    public sealed record CreateSpaBookingRequest(
        [Required(ErrorMessage = "Danh sách thú cưng không được để trống.")]
        [MinLength(1, ErrorMessage = "Phải chọn ít nhất 1 thú cưng.")]
        IReadOnlyList<int> PetIds,

        [Required(ErrorMessage = "Thời gian hẹn không được để trống.")]
        DateTime AppointmentStart,

        [StringLength(500, ErrorMessage = "Ghi chú không được vượt quá 500 ký tự.")]
        string? Note,

        [Required(ErrorMessage = "Danh sách dịch vụ không được để trống.")]
        [MinLength(1, ErrorMessage = "Phải chọn ít nhất 1 dịch vụ.")]
        IReadOnlyList<SpaCartItemRequest> Items,

        [StringLength(30, ErrorMessage = "Phương thức thanh toán không hợp lệ.")]
        string? PaymentMethod
    );

    public sealed record SpaBookingItemDto(
        int ServiceId,
        string ServiceName,
        int Quantity,
        decimal UnitPrice,
        int DurationMin
    );

    public sealed record SpaBookingDto(
        int BookingId,
        int PetId,
        string PetName,
        string? BreedName,
        string? SpeciesName,
        string? CustomerName,
        string? CustomerPhone,
        DateTime AppointmentStart,
        DateTime AppointmentEnd,
        string? Status,
        DateTime? CreatedAt,
        IReadOnlyList<SpaBookingItemDto> Items,
        decimal TotalPrice,
        string? PaymentMethod
    )
    {
        [JsonIgnore]
        public IReadOnlyList<int>? BookingIds { get; init; }
    }

    public sealed record SpaBookingInvoiceDto(
        int BookingId,
        string CustomerName,
        string CustomerEmail,
        int PetId,
        string PetName,
        DateTime AppointmentStart,
        DateTime AppointmentEnd,
        string? Status,
        DateTime? CreatedAt,
        IReadOnlyList<SpaBookingItemDto> Items,
        decimal TotalPrice,
        string? PaymentMethod
    );

    public sealed record SpaAvailabilityResponse(
        bool IsAvailable,
        int ExistingBookings,
        int MaxCapacity,
        DateTime? SuggestedStart,
        string? Message
    );

    /// <summary>
    /// Validate giờ đặt dịch vụ SPA có nằm trong ca làm việc của nhân viên hay không.
    /// </summary>
    public sealed record BookingSlotValidationRequest(
        [Required(ErrorMessage = "Thời gian hẹn không được để trống.")]
        DateTime AppointmentStart,

        [Range(5, 480, ErrorMessage = "Thời lượng phải từ 5 đến 480 phút.")]
        int DurationMinutes
    );

    public sealed record BookingSlotShiftDto(
        int ScheduleId,
        TimeOnly StartTime,
        TimeOnly EndTime,
        string ShiftLabel
    );

    public sealed record BookingSlotValidationResult(
        bool IsValid,
        DateTime AppointmentStart,
        DateTime AppointmentEnd,
        int DurationMinutes,
        string? Message,
        IReadOnlyList<BookingSlotShiftDto> AvailableShifts,
        IReadOnlyList<BookingSlotShiftDto> ConflictingShifts
    );

    public sealed record CreateSpaReviewRequest(
        [Range(1, int.MaxValue, ErrorMessage = "Mã đặt lịch không hợp lệ.")]
        int BookingId,

        [Range(1, int.MaxValue, ErrorMessage = "Mã dịch vụ không hợp lệ.")]
        int ServiceId,

        [Range(1, 5, ErrorMessage = "Đánh giá phải từ 1 đến 5 sao.")]
        int Rating,

        [StringLength(1000, ErrorMessage = "Nhận xét không được vượt quá 1000 ký tự.")]
        string? Comment
    );
}
