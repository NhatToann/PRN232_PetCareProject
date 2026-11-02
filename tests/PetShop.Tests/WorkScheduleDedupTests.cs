using PetShop.Models;
using PetShop.Services;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho <see cref="WorkScheduleDedup"/> — gom các dòng WorkSchedule trùng
/// (cùng nhân viên / ngày / khung giờ), giữ bản ghi có <c>schedule_id</c> nhỏ nhất.
/// </summary>
public class WorkScheduleDedupTests
{
    private static readonly DateOnly Day = new(2026, 3, 10);

    private static WorkSchedule Row(int id, int? staffId, TimeOnly start, TimeOnly end, DateOnly? day = null) => new()
    {
        ScheduleId = id,
        StaffId = staffId,
        WorkDate = day ?? Day,
        StartTime = start,
        EndTime = end,
        Status = "active"
    };

    [Fact]
    public void ByStaffDateTimeSlot_RemovesExactDuplicates_KeepsSmallestScheduleId()
    {
        var rows = new[]
        {
            Row(30, 1, new TimeOnly(8, 0), new TimeOnly(16, 0)),
            Row(10, 1, new TimeOnly(8, 0), new TimeOnly(16, 0)),
            Row(20, 1, new TimeOnly(8, 0), new TimeOnly(16, 0))
        };

        var result = WorkScheduleDedup.ByStaffDateTimeSlot(rows);

        var only = Assert.Single(result);
        Assert.Equal(10, only.ScheduleId);
    }

    [Fact]
    public void ByStaffDateTimeSlot_KeepsDifferentStaffSeparately()
    {
        var rows = new[]
        {
            Row(1, 1, new TimeOnly(8, 0), new TimeOnly(16, 0)),
            Row(2, 2, new TimeOnly(8, 0), new TimeOnly(16, 0))
        };

        var result = WorkScheduleDedup.ByStaffDateTimeSlot(rows);

        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { 1, 2 }, result.Select(r => r.StaffId!.Value).Order().ToArray());
    }

    [Fact]
    public void ByStaffDateTimeSlot_KeepsDifferentTimeSlotsOfSameStaff()
    {
        var rows = new[]
        {
            Row(1, 1, new TimeOnly(8, 0), new TimeOnly(12, 0)),
            Row(2, 1, new TimeOnly(13, 0), new TimeOnly(17, 0))
        };

        var result = WorkScheduleDedup.ByStaffDateTimeSlot(rows);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ByStaffDateTimeSlot_OrdersByStartTime()
    {
        var rows = new[]
        {
            Row(1, 1, new TimeOnly(13, 0), new TimeOnly(17, 0)),
            Row(2, 1, new TimeOnly(8, 0), new TimeOnly(12, 0))
        };

        var result = WorkScheduleDedup.ByStaffDateTimeSlot(rows);

        Assert.Equal(new[] { 2, 1 }, result.Select(r => r.ScheduleId).ToArray());
    }

    [Fact]
    public void ByStaffDateTimeSlot_TreatsNullStaffAsKeyZero()
    {
        var rows = new[]
        {
            Row(5, null, new TimeOnly(8, 0), new TimeOnly(12, 0)),
            Row(3, null, new TimeOnly(8, 0), new TimeOnly(12, 0))
        };

        var result = WorkScheduleDedup.ByStaffDateTimeSlot(rows);

        var only = Assert.Single(result);
        Assert.Equal(3, only.ScheduleId);
        Assert.Null(only.StaffId);
    }

    [Fact]
    public void ByStaffDateTimeSlot_EmptyInput_ReturnsEmpty()
    {
        var result = WorkScheduleDedup.ByStaffDateTimeSlot(Array.Empty<WorkSchedule>());

        Assert.Empty(result);
    }

    [Fact]
    public void ByTimeSlot_IgnoresStaff_KeepsSmallestScheduleId()
    {
        var rows = new[]
        {
            Row(9, 1, new TimeOnly(8, 0), new TimeOnly(12, 0)),
            Row(4, 2, new TimeOnly(8, 0), new TimeOnly(12, 0))
        };

        var result = WorkScheduleDedup.ByTimeSlot(rows);

        var only = Assert.Single(result);
        Assert.Equal(4, only.ScheduleId);
    }
}
