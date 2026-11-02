using System.ComponentModel.DataAnnotations;

namespace PetShop.Models
{
    public sealed record VerifyNetworkDto(
        string Ip,
        bool IsCompanyNetwork,
        string CompanyPrefix,
        string? XClientIp,
        string? RealClientIp,
        string? XForwardedFor,
        string? RemoteIp,
        string? ResolvedFrom,
        string? XRealIp
    );

    public sealed record ShiftOptionDto(int ScheduleId, int? StaffId, string? StaffName, DateOnly WorkDate, int? ShiftId, TimeSpan StartTime, TimeSpan EndTime, string? Status);

    public sealed record StaffOptionDto(int StaffId, string Name);

    public sealed record CreatePassShiftRequestDto(
        [Range(1, int.MaxValue, ErrorMessage = "Mã nhân viên không hợp lệ.")]
        int StaffId,

        [Required(ErrorMessage = "Ngày làm việc không được để trống.")]
        DateOnly FromDate,

        [Range(1, int.MaxValue, ErrorMessage = "Mã ca không hợp lệ.")]
        int FromScheduleId,

        [Range(1, int.MaxValue, ErrorMessage = "Mã nhân viên nhận ca không hợp lệ.")]
        int ToStaffId,

        [StringLength(500, ErrorMessage = "Lý do không được vượt quá 500 ký tự.")]
        string? Reason
    );

    public sealed record CreateSwapShiftRequestDto(
        [Range(1, int.MaxValue, ErrorMessage = "Mã nhân viên không hợp lệ.")]
        int StaffId,

        [Required(ErrorMessage = "Ngày làm việc không được để trống.")]
        DateOnly FromDate,

        [Required(ErrorMessage = "Ngày làm việc không được để trống.")]
        DateOnly ToDate,

        [Range(1, int.MaxValue, ErrorMessage = "Mã ca không hợp lệ.")]
        int FromScheduleId,

        [Range(1, int.MaxValue, ErrorMessage = "Mã ca không hợp lệ.")]
        int ToScheduleId,

        [Range(1, int.MaxValue, ErrorMessage = "Mã nhân viên nhận ca không hợp lệ.")]
        int ToStaffId,

        [StringLength(500, ErrorMessage = "Lý do không được vượt quá 500 ký tự.")]
        string? Reason
    );

    public sealed record ShiftExchangeActionResultDto(string Status, string Message);

    public sealed record PendingRequestDto(
        int RequestId,
        int? EmployeeId,
        string? EmployeeName,
        int? ToStaffId,
        string Type,
        DateOnly FromDate,
        DateOnly? ToDate,
        int? FromShiftId,
        int? ToShiftId,
        string Reason,
        string Status,
        DateTime CreatedAt
    );

    public sealed record MyRequestDto(
        int RequestId,
        int? ToStaffId,
        string? ToStaffName,
        string Type,
        DateOnly FromDate,
        DateOnly? ToDate,
        string Status,
        string Reason,
        DateTime CreatedAt,
        bool ApprovedByTo
    );
}
