using PetShop.Models;
using PetShop.Services;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho luật tính công của Pets4Care.
///
/// Luật (xem <c>Services/AttendanceService.CalculateWorkHours</c>):
///  - Có checkout: workTime = min(checkout, shiftEnd) - checkIn; earlyMinutes = shiftEnd - checkout.
///  - Không checkout: workTime = shiftEnd - checkIn, đánh dấu thiếu checkout, earlyMinutes = 0.
/// </summary>
public class AttendanceServiceTests
{
    private static readonly DateOnly WorkDate = new(2026, 3, 10);

    private static WorkSchedule Schedule(TimeOnly start, TimeOnly end) => new()
    {
        ScheduleId = 1,
        StaffId = 7,
        WorkDate = WorkDate,
        StartTime = start,
        EndTime = end,
        Status = "active"
    };

    private static AttendanceRecord Record(DateTime checkIn, DateTime? checkOut) => new()
    {
        AttendanceId = 1,
        StaffId = 7,
        CheckIn = checkIn,
        CheckOut = checkOut
    };

    private static WorkHoursResult Calculate(AttendanceRecord att, WorkSchedule sched)
        => new AttendanceService(null!).CalculateWorkHours(att, sched);

    [Fact]
    public void FullShift_NoCheckout_MarksMissingAndUsesShiftEnd()
    {
        // Ca 08:00–16:00, check-in đúng 08:00, không checkout.
        var sched = Schedule(new TimeOnly(8, 0), new TimeOnly(16, 0));
        var att = Record(WorkDate.ToDateTime(new TimeOnly(8, 0)), null);

        var result = Calculate(att, sched);

        Assert.True(result.IsMissingCheckout);
        Assert.Equal(8.0, result.WorkHours);
        Assert.Equal(0.0, result.EarlyMinutes);
    }

    [Fact]
    public void FullShift_WithCheckout_AtShiftEnd_NotMissing()
    {
        var sched = Schedule(new TimeOnly(8, 0), new TimeOnly(16, 0));
        var att = Record(
            WorkDate.ToDateTime(new TimeOnly(8, 0)),
            WorkDate.ToDateTime(new TimeOnly(16, 0)));

        var result = Calculate(att, sched);

        Assert.False(result.IsMissingCheckout);
        Assert.Equal(8.0, result.WorkHours);
        Assert.Equal(0.0, result.EarlyMinutes);
    }

    [Fact]
    public void Checkout_BeforeShiftEnd_CountsEarlyMinutes()
    {
        // Ra sớm 30 phút: 08:00 -> 15:30 trên ca 08:00–16:00.
        var sched = Schedule(new TimeOnly(8, 0), new TimeOnly(16, 0));
        var att = Record(
            WorkDate.ToDateTime(new TimeOnly(8, 0)),
            WorkDate.ToDateTime(new TimeOnly(15, 30)));

        var result = Calculate(att, sched);

        Assert.False(result.IsMissingCheckout);
        Assert.Equal(7.5, result.WorkHours);
        Assert.Equal(30.0, result.EarlyMinutes);
    }

    [Fact]
    public void Checkout_AfterShiftEnd_IsClampedToShiftEnd()
    {
        // Ở lại quá giờ (17:00) => chỉ tính tới giờ kết thúc ca, không cộng overtime.
        var sched = Schedule(new TimeOnly(8, 0), new TimeOnly(16, 0));
        var att = Record(
            WorkDate.ToDateTime(new TimeOnly(8, 0)),
            WorkDate.ToDateTime(new TimeOnly(17, 0)));

        var result = Calculate(att, sched);

        Assert.False(result.IsMissingCheckout);
        Assert.Equal(8.0, result.WorkHours);
        Assert.Equal(0.0, result.EarlyMinutes);
    }

    [Fact]
    public void LateCheckIn_ReducesWorkHours()
    {
        // Đến muộn 30 phút: 08:30 -> 16:00.
        var sched = Schedule(new TimeOnly(8, 0), new TimeOnly(16, 0));
        var att = Record(
            WorkDate.ToDateTime(new TimeOnly(8, 30)),
            WorkDate.ToDateTime(new TimeOnly(16, 0)));

        var result = Calculate(att, sched);

        Assert.False(result.IsMissingCheckout);
        Assert.Equal(7.5, result.WorkHours);
        Assert.Equal(0.0, result.EarlyMinutes);
    }

    [Fact]
    public void PartialShift_IsRoundedToTwoDecimals()
    {
        // Ca 08:00–16:00, check-in 08:00, checkout 10:07 => 127 phút = 2.1166.. giờ => 2.12.
        var sched = Schedule(new TimeOnly(8, 0), new TimeOnly(16, 0));
        var att = Record(
            WorkDate.ToDateTime(new TimeOnly(8, 0)),
            WorkDate.ToDateTime(new TimeOnly(10, 7)));

        var result = Calculate(att, sched);

        Assert.Equal(2.12, result.WorkHours);
    }

    [Fact]
    public void CheckIn_AfterShiftEnd_NeverReturnsNegativeHours()
    {
        // Dữ liệu bất thường: check-in sau giờ kết thúc ca => 0 giờ, không âm.
        var sched = Schedule(new TimeOnly(8, 0), new TimeOnly(16, 0));
        var att = Record(
            WorkDate.ToDateTime(new TimeOnly(17, 0)),
            WorkDate.ToDateTime(new TimeOnly(18, 0)));

        var result = Calculate(att, sched);

        Assert.Equal(0.0, result.WorkHours);
        Assert.Equal(0.0, result.EarlyMinutes);
    }
}
