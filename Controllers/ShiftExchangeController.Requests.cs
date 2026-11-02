using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Controllers
{
    /// <summary>
    /// Phần tạo yêu cầu đổi ca: "nhờ làm thay" (Leave) và "đổi ca" (Swap).
    /// Xem <see cref="ShiftExchangeController"/> để phần hạ tầng và endpoint đọc.
    /// </summary>
    public sealed partial class ShiftExchangeController
    {
        [HttpPost("pass")]
        public async Task<ActionResult<ShiftExchangeActionResultDto>> CreatePass([FromBody] CreatePassShiftRequestDto request, CancellationToken ct)
        {
            _logger.Line($"[CreatePass] StaffId={request.StaffId}, FromDate={request.FromDate}, FromScheduleId={request.FromScheduleId}, ToStaffId={request.ToStaffId}");

            var today = DateOnly.FromDateTime(DateTime.Now);
            if (request.FromDate < today)
            {
                _logger.Line("[CreatePass] FAIL: date in past");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Không thể gửi yêu cầu cho ngày trong quá khứ."));
            }

            if (request.FromDate == today)
            {
                _logger.Line("[CreatePass] FAIL: same day as today");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Đã qua 0h sáng ngày hôm nay, không thể gửi yêu cầu đổi ca cùng ngày."));
            }

            if (await _attendanceService.HasAttendanceForDateAsync(request.StaffId, request.FromDate, ct))
            {
                _logger.Line("[CreatePass] FAIL: already clocked in");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Bạn đã chấm công ngày này rồi, không thể yêu cầu đổi ca."));
            }

            var mySchedule = await _db.WorkSchedules
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.ScheduleId == request.FromScheduleId &&
                    x.StaffId == request.StaffId,
                    ct);

            _logger.Line($"[CreatePass] mySchedule=(ScheduleId={mySchedule?.ScheduleId}, StaffId={mySchedule?.StaffId}, WorkDate={mySchedule?.WorkDate})");

            if (mySchedule is null)
            {
                _logger.Line("[CreatePass] FAIL: schedule not found or wrong staff");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Không tìm thấy ca của bạn để nhờ làm thay."));
            }

            if (mySchedule.WorkDate != request.FromDate)
            {
                _logger.Line($"[CreatePass] FAIL: date mismatch. ScheduleDate={mySchedule.WorkDate}, RequestDate={request.FromDate}");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Ngày ca không khớp với ca đã chọn."));
            }

            // Chỉ chặn trùng theo đúng dòng lịch (ScheduleId), không chặn cả ngày — cùng ngày nhiều ca vẫn gửi nhờ làm thay/đổi tiếp được
            var existingReq = await _db.ShiftRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.EmployeeId == request.StaffId &&
                    r.Type == "Leave" &&
                    r.FromShiftId == request.FromScheduleId &&
                    r.Status == "Pending",
                    ct);

            if (existingReq is not null)
            {
                return BadRequest(new ShiftExchangeActionResultDto("error", "Ca (dòng lịch) này đã có yêu cầu nhờ làm thay đang chờ duyệt. Huỷ yêu cầu cũ nếu muốn gửi lại."));
            }

            // ⚠️ Kiểm tra người được nhờ đã có ca cùng khung giờ chưa
            var toStaffHasSlot = await _db.WorkSchedules
                .AsNoTracking()
                .AnyAsync(s =>
                    s.StaffId == request.ToStaffId &&
                    s.WorkDate == mySchedule.WorkDate &&
                    s.StartTime == mySchedule.StartTime &&
                    s.EndTime == mySchedule.EndTime,
                    ct);
            if (toStaffHasSlot)
            {
                return BadRequest(new ShiftExchangeActionResultDto("error",
                    $"Nhân viên được nhờ đã có ca cùng khung giờ ngày {mySchedule.WorkDate:dd/MM/yyyy} rồi, không thể nhận thêm ca này."));
            }

            var req = new ShiftRequest
            {
                EmployeeId = request.StaffId,
                ToStaffId = request.ToStaffId,
                Type = "Leave",
                TargetDate = request.FromDate,
                FromDate = request.FromDate,
                FromShiftId = mySchedule.ScheduleId,  // ScheduleId (id dòng trong WorkSchedule)
                Reason = request.Reason,
                Status = "Pending",
                CreatedAt = DateTime.Now,
                ToNotified = false,
                AdminNotified = false,
                ApprovedByTo = false,
            };

            _db.ShiftRequests.Add(req);
            await _db.SaveChangesAsync(ct);

            return Ok(new ShiftExchangeActionResultDto("success", "Gửi yêu cầu nhờ làm thay thành công."));
        }

        [HttpPost("swap")]
        public async Task<ActionResult<ShiftExchangeActionResultDto>> CreateSwap([FromBody] CreateSwapShiftRequestDto request, CancellationToken ct)
        {
            _logger.Line($"[CreateSwap] StaffId={request.StaffId}, FromDate={request.FromDate}, FromScheduleId={request.FromScheduleId}, ToDate={request.ToDate}, ToScheduleId={request.ToScheduleId}, ToStaffId={request.ToStaffId}");

            var today = DateOnly.FromDateTime(DateTime.Now);
            if (request.FromDate < today || request.ToDate < today)
            {
                _logger.Line("[CreateSwap] FAIL: date in past");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Không thể đổi ca cho ngày trong quá khứ."));
            }

            if (request.FromDate == today)
            {
                _logger.Line("[CreateSwap] FAIL: same day as today (fromDate)");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Đã qua 0h sáng ngày hôm nay, không thể gửi yêu cầu đổi ca cùng ngày."));
            }

            if (request.ToDate == today)
            {
                _logger.Line("[CreateSwap] FAIL: same day as today (toDate)");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Đã qua 0h sáng ngày hôm nay, không thể đổi ca nhận ngày hôm nay."));
            }

            if (await _attendanceService.HasAttendanceForDateAsync(request.StaffId, request.FromDate, ct))
            {
                _logger.Line("[CreateSwap] FAIL: requester already clocked in");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Bạn đã chấm công ngày này rồi, không thể yêu cầu đổi ca."));
            }

            if (await _attendanceService.HasAttendanceForDateAsync(request.ToStaffId, request.ToDate, ct))
            {
                _logger.Line("[CreateSwap] FAIL: toStaff already clocked in");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Nhân viên được đổi đã chấm công ngày kia rồi, không thể đổi."));
            }

            var fromSchedule = await _db.WorkSchedules
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.ScheduleId == request.FromScheduleId &&
                    x.StaffId == request.StaffId,
                    ct);

            _logger.Line($"[CreateSwap] fromSchedule=(ScheduleId={fromSchedule?.ScheduleId}, StaffId={fromSchedule?.StaffId}, WorkDate={fromSchedule?.WorkDate})");

            if (fromSchedule is null)
            {
                _logger.Line("[CreateSwap] FAIL: fromSchedule not found or wrong staff");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Không tìm thấy ca của bạn để đổi."));
            }

            if (fromSchedule.WorkDate != request.FromDate)
            {
                _logger.Line($"[CreateSwap] FAIL: fromSchedule date mismatch. DB={fromSchedule.WorkDate}, Request={request.FromDate}");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Ngày ca của bạn không khớp."));
            }

            var toSchedule = await _db.WorkSchedules
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.ScheduleId == request.ToScheduleId &&
                    x.StaffId == request.ToStaffId,
                    ct);

            _logger.Line($"[CreateSwap] toSchedule=(ScheduleId={toSchedule?.ScheduleId}, StaffId={toSchedule?.StaffId}, WorkDate={toSchedule?.WorkDate})");

            if (toSchedule is null)
            {
                _logger.Line("[CreateSwap] FAIL: toSchedule not found or wrong staff");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Không tìm thấy ca của nhân viên được chọn để đổi."));
            }

            if (toSchedule.WorkDate != request.ToDate)
            {
                _logger.Line($"[CreateSwap] FAIL: toSchedule date mismatch. DB={toSchedule.WorkDate}, Request={request.ToDate}");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Ngày ca muốn đổi không khớp."));
            }

            // Một dòng lịch (ca cục thể) chỉ một yêu cầu đổi đang Pending; khác ScheduleId cùng ngày vẫn đổi tiếp được
            var existingReq = await _db.ShiftRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(r =>
                    r.EmployeeId == request.StaffId &&
                    r.Type == "Swap" &&
                    r.FromShiftId == request.FromScheduleId &&
                    r.Status == "Pending",
                    ct);

            if (existingReq is not null)
            {
                _logger.Line($"[CreateSwap] FAIL: duplicate pending request #{existingReq.RequestId} for FromScheduleId={request.FromScheduleId}");
                return BadRequest(new ShiftExchangeActionResultDto("error", "Ca (dòng lịch) này đã có yêu cầu đổi đang chờ duyệt. Huỷ hoặc đợi xử lý xong để đổi tiếp."));
            }

            // ⚠️ Kiểm tra người gửi đã có ca cùng khung giờ ngày nhận (toSchedule) chưa
            var requesterHasSlot = await _db.WorkSchedules
                .AsNoTracking()
                .AnyAsync(s =>
                    s.StaffId == request.StaffId &&
                    s.WorkDate == toSchedule.WorkDate &&
                    s.StartTime == toSchedule.StartTime &&
                    s.EndTime == toSchedule.EndTime,
                    ct);
            if (requesterHasSlot)
            {
                return BadRequest(new ShiftExchangeActionResultDto("error",
                    $"Bạn đã có ca cùng khung giờ ngày {toSchedule.WorkDate:dd/MM/yyyy} rồi, không thể nhận thêm ca để đổi."));
            }

            // ⚠️ Kiểm tra nhân viên được đổi đã có ca cùng khung giờ ngày gửi (fromSchedule) chưa
            var toStaffHasSlot = await _db.WorkSchedules
                .AsNoTracking()
                .AnyAsync(s =>
                    s.StaffId == request.ToStaffId &&
                    s.WorkDate == fromSchedule.WorkDate &&
                    s.StartTime == fromSchedule.StartTime &&
                    s.EndTime == fromSchedule.EndTime,
                    ct);
            if (toStaffHasSlot)
            {
                return BadRequest(new ShiftExchangeActionResultDto("error",
                    $"Nhân viên được đổi đã có ca cùng khung giờ ngày {fromSchedule.WorkDate:dd/MM/yyyy} rồi, không thể nhận thêm ca để đổi."));
            }

            _logger.Line("[CreateSwap] SUCCESS — creating ShiftRequest");
            var req = new ShiftRequest
            {
                EmployeeId = request.StaffId,
                ToStaffId = request.ToStaffId,
                Type = "Swap",
                TargetDate = request.FromDate,
                FromDate = request.FromDate,
                ToDate = request.ToDate,
                FromShiftId = fromSchedule.ScheduleId,
                ToShiftId = toSchedule.ScheduleId,
                Reason = request.Reason,
                Status = "Pending",
                CreatedAt = DateTime.Now,
                ToNotified = false,
                AdminNotified = false,
                ApprovedByTo = false,
            };

            _db.ShiftRequests.Add(req);
            await _db.SaveChangesAsync(ct);

            return Ok(new ShiftExchangeActionResultDto("success", "Gửi yêu cầu đổi ca thành công."));
        }
    }
}
