using System.ComponentModel.DataAnnotations;

namespace PetShop.Models
{
    public sealed record PayrollSummaryDto(
        int PayrollId,
        int? StaffId,
        string? StaffName,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        double? TotalHours,
        decimal? HourlyRate,
        decimal? BaseSalary,
        int? ActualShifts,
        int? StandardShifts,
        decimal? TotalSalary,
        DateTime? CreatedAt
    );

    public sealed record GenerateAllPayrollDto(
        [Required(ErrorMessage = "Ngày bắt đầu kỳ không được để trống.")]
        DateOnly PeriodStart,
        [Required(ErrorMessage = "Ngày kết thúc kỳ không được để trống.")]
        DateOnly PeriodEnd
    );

    public sealed record StaffSalaryConfigDto(
        int SalaryId,
        int StaffId,
        string? StaffName,
        decimal? HourlyRate,
        decimal? MonthlyBaseSalary,
        int? StandardShifts,
        DateTime? UpdatedAt
    );

    public sealed record UpdateSalaryConfigDto(
        [Range(1, int.MaxValue, ErrorMessage = "Mã nhân viên không hợp lệ.")]
        int StaffId,

        [Range(0, double.MaxValue, ErrorMessage = "Lương cơ bản không hợp lệ.")]
        decimal? MonthlyBaseSalary,

        [Range(0, 31, ErrorMessage = "Số ca chuẩn phải từ 0 đến 31.")]
        int? StandardShifts,

        [Range(0, double.MaxValue, ErrorMessage = "Mức lương theo giờ không hợp lệ.")]
        decimal? HourlyRate
    );

    public sealed record PayrollListResultDto(
        string Status,
        string Message,
        IReadOnlyList<PayrollSummaryDto> Records
    );

    public sealed record PayrollConfigResultDto(
        string Status,
        string Message,
        StaffSalaryConfigDto? Config
    );

    public sealed record PayrollAttendanceDetailDto(
        DateOnly Date,
        double? TotalHours,
        string? Status
    );

    public sealed record StaffPayrollDetailDto(
        int StaffId,
        string? StaffName,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        int ActualShifts,
        int StandardShifts,
        decimal MonthlyBaseSalary,
        decimal TotalSalary,
        IReadOnlyList<PayrollAttendanceDetailDto> AttendanceDetails
    );
}
