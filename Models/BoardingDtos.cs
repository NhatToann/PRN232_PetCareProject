using System.ComponentModel.DataAnnotations;

namespace PetShop.Models
{
    public sealed record BoardingCheckoutRequest(
        [Range(1, int.MaxValue, ErrorMessage = "Mã phòng không hợp lệ.")]
        int RoomId,

        [StringLength(100, ErrorMessage = "Tên phòng không được vượt quá 100 ký tự.")]
        string RoomName,

        [StringLength(50, ErrorMessage = "Loại phòng không được vượt quá 50 ký tự.")]
        string RoomType,

        [Required(ErrorMessage = "Ngày nhận phòng không được để trống.")]
        DateOnly CheckIn,

        [Required(ErrorMessage = "Ngày trả phòng không được để trống.")]
        DateOnly CheckOut,

        [Range(1, 365, ErrorMessage = "Số ngày lưu trú phải từ 1 đến 365.")]
        int Days,

        [Range(1, 20, ErrorMessage = "Số thú cưng phải từ 1 đến 20.")]
        int Pets,

        [StringLength(1000, ErrorMessage = "Ghi chú không được vượt quá 1000 ký tự.")]
        string? Notes,

        [Range(0, double.MaxValue, ErrorMessage = "Giá phòng không hợp lệ.")]
        decimal PricePerDay,

        [Range(0, double.MaxValue, ErrorMessage = "Tổng tiền không hợp lệ.")]
        decimal TotalPrice,

        int? CustomerId,
        string? CustomerName,
        string? CustomerEmail,
        string? CustomerPhone,

        [Required(ErrorMessage = "Phương thức thanh toán không được để trống.")]
        [StringLength(30, ErrorMessage = "Phương thức thanh toán không hợp lệ.")]
        string PaymentMethod
    );

    public sealed record BoardingCheckoutResponse(
        bool Success,
        string? Message,
        int? BookingId,
        string? OrderCode,
        string? RedirectUrl,
        string PaymentMethod
    );

    public sealed record BoardingBookingDto(
        int BookingId,
        int CustomerId,
        string RoomType,
        decimal PricePerDay,
        int BoardingDays,
        DateOnly CheckInDate,
        DateOnly CheckOutDate,
        string? CheckInTime,
        string? CheckOutTime,
        string? PetInfo,
        string? SpecialNotes,
        string? EmergencyPhone1,
        string? EmergencyPhone2,
        string Status,
        DateTime? CreatedAt,
        DateTime? UpdatedAt,
        decimal TotalPrice,
        CustomerDto? Customer
    );

    public sealed record CustomerDto(
        int CustomerId,
        string? Name,
        string? Email,
        string? Phone
    );

    public sealed record BoardingAvailabilityDto(
        string RoomType,
        int TotalRooms,
        int ActiveBookings,
        int AvailableRooms
    );
}
