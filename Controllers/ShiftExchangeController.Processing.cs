using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Controllers
{
    /// <summary>
    /// Phần xử lý yêu cầu đổi ca: chấp nhận / từ chối / huỷ.
    /// Xem <see cref="ShiftExchangeController"/> để phần hạ tầng và endpoint đọc.
    /// </summary>
    public sealed partial class ShiftExchangeController
    {
        [HttpPost("accept/{requestId:int}")]
        public async Task<ActionResult<ShiftExchangeActionResultDto>> AcceptRequest([FromRoute] int requestId, [FromQuery] int responderStaffId, CancellationToken ct)
        {
            _logger.Line($"[AcceptRequest] requestId={requestId}, responderStaffId={responderStaffId}");

            var req = await _db.ShiftRequests
                .FirstOrDefaultAsync(r => r.RequestId == requestId && r.ToStaffId == responderStaffId && r.Status == "Pending", ct);

            if (req is null)
            {
                return BadRequest(new ShiftExchangeActionResultDto("error", "Yêu cầu không tồn tại hoặc không thuộc về bạn."));
            }

            _logger.Line($"[AcceptRequest] Found: Type={req.Type}, FromShiftId={req.FromShiftId}, ToShiftId={req.ToShiftId}, EmployeeId={req.EmployeeId}, FromDate={req.FromDate}, ToDate={req.ToDate}");

            var targetDate = req.Type == "Swap" ? req.ToDate : req.FromDate;

            if (targetDate.HasValue && await _attendanceService.HasAttendanceForDateAsync(responderStaffId, targetDate.Value, ct))
            {
                req.Status = "Rejected";
                req.ApprovedByTo = false;
                await _db.SaveChangesAsync(ct);

                return BadRequest(new ShiftExchangeActionResultDto("error", "Bạn đã chấm công ngày đó rồi, không thể nhận đổi."));
            }

            if (req.Type == "Swap")
            {
                // myShift = ca MUON CHO của responder (req.ToShiftId), bên responder sở hữu
                // partnerShift = ca MUON NHẬN của requester (req.FromShiftId), bên requester sở hữu
                var myShift = await _db.WorkSchedules
                    .FirstOrDefaultAsync(s => s.ScheduleId == req.ToShiftId && s.StaffId == responderStaffId, ct);
                var partnerShift = await _db.WorkSchedules
                    .FirstOrDefaultAsync(s => s.ScheduleId == req.FromShiftId && s.StaffId == req.EmployeeId, ct);

                _logger.Line($"[AcceptRequest] myShift=(ScheduleId={myShift?.ScheduleId}, StaffId={myShift?.StaffId}), partnerShift=(ScheduleId={partnerShift?.ScheduleId}, StaffId={partnerShift?.StaffId})");

                if (myShift is null || partnerShift is null)
                {
                    _logger.Line($"[AcceptRequest] FAIL: {(myShift is null ? "myShift" : "partnerShift")} is null. ToShiftId={req.ToShiftId} responderStaffId={responderStaffId}, FromShiftId={req.FromShiftId} EmployeeId={req.EmployeeId}");
                    return BadRequest(new ShiftExchangeActionResultDto("error", "Ca của một trong hai bên không còn tồn tại."));
                }

                if (await _attendanceService.HasAttendanceForDateAsync(req.EmployeeId!.Value, partnerShift.WorkDate, ct))
                {
                    return BadRequest(new ShiftExchangeActionResultDto("error", "Người gửi yêu cầu đã chấm công ca kia rồi."));
                }

                // ⚠️ Kiểm tra requester đã có ca trùng khung giờ ngày nhận (partnerShift) chưa
                var requesterHasSlot = await _db.WorkSchedules
                    .AnyAsync(s =>
                        s.StaffId == req.EmployeeId &&
                        s.WorkDate == partnerShift.WorkDate &&
                        s.StartTime == partnerShift.StartTime &&
                        s.EndTime == partnerShift.EndTime,
                        ct);
                if (requesterHasSlot)
                {
                    return BadRequest(new ShiftExchangeActionResultDto("error",
                        $"Người gửi yêu cầu đã có ca cùng khung giờ ngày {partnerShift.WorkDate:dd/MM/yyyy} rồi, không thể nhận thêm ca nữa."));
                }

                // ⚠️ Kiểm tra responder đã có ca trùng khung giờ ngày nhận (myShift) chưa
                var responderHasSlot = await _db.WorkSchedules
                    .AnyAsync(s =>
                        s.StaffId == responderStaffId &&
                        s.WorkDate == myShift.WorkDate &&
                        s.StartTime == myShift.StartTime &&
                        s.EndTime == myShift.EndTime,
                        ct);
                if (responderHasSlot)
                {
                    return BadRequest(new ShiftExchangeActionResultDto("error",
                        $"Bạn đã có ca cùng khung giờ ngày {myShift.WorkDate:dd/MM/yyyy} rồi, không thể nhận thêm ca nữa."));
                }

                // Swap: responder nhận ca requester (myShift), requester nhận ca responder (partnerShift)
                (myShift.StaffId, partnerShift.StaffId) = (partnerShift.StaffId, myShift.StaffId);

                // Sau khi swap, dọn trùng khung giờ cho cả hai (nếu sau swap có dòng trùng)
                await RemoveDuplicateSlotRowsForStaffAsync(
                    req.EmployeeId!.Value,
                    myShift.WorkDate,
                    myShift.StartTime,
                    myShift.EndTime,
                    ct);
                await RemoveDuplicateSlotRowsForStaffAsync(
                    responderStaffId,
                    partnerShift.WorkDate,
                    partnerShift.StartTime,
                    partnerShift.EndTime,
                    ct);
            }
            else
            {
                // Leave: ca MUON NHO của requester = req.FromShiftId, sở hữu bởi requester
                var myShift = await _db.WorkSchedules
                    .FirstOrDefaultAsync(s => s.ScheduleId == req.FromShiftId && s.StaffId == req.EmployeeId, ct);

                if (myShift is null)
                {
                    return BadRequest(new ShiftExchangeActionResultDto("error", "Ca cần nhờ làm thay không còn tồn tại."));
                }

                // ⚠️ Kiểm tra requester đã nhận ca cùng khung giờ chưa
                var requesterHasSlot = await _db.WorkSchedules
                    .AnyAsync(s =>
                        s.StaffId == req.EmployeeId &&
                        s.WorkDate == myShift.WorkDate &&
                        s.StartTime == myShift.StartTime &&
                        s.EndTime == myShift.EndTime,
                        ct);
                if (requesterHasSlot)
                {
                    return BadRequest(new ShiftExchangeActionResultDto("error",
                        $"Người gửi yêu cầu đã có ca cùng khung giờ ngày {myShift.WorkDate:dd/MM/yyyy} rồi, không thể nhận thêm ca."));
                }

                // ⚠️ Kiểm tra responder đã có ca cùng khung giờ chưa
                var responderHasSlot = await _db.WorkSchedules
                    .AnyAsync(s =>
                        s.StaffId == responderStaffId &&
                        s.WorkDate == myShift.WorkDate &&
                        s.StartTime == myShift.StartTime &&
                        s.EndTime == myShift.EndTime,
                        ct);
                if (responderHasSlot)
                {
                    return BadRequest(new ShiftExchangeActionResultDto("error",
                        $"Nhân viên được nhờ đã có ca cùng khung giờ ngày {myShift.WorkDate:dd/MM/yyyy} rồi, không thể nhận thêm ca."));
                }

                myShift.StaffId = responderStaffId;

                await RemoveDuplicateSlotRowsForStaffAsync(
                    responderStaffId,
                    myShift.WorkDate,
                    myShift.StartTime,
                    myShift.EndTime,
                    ct);
            }

            req.Status = "Approved";
            req.ApprovedByTo = true;

            await _db.SaveChangesAsync(ct);

            return Ok(new ShiftExchangeActionResultDto("success", "Đã chấp nhận yêu cầu đổi ca."));
        }

        [HttpPost("decline/{requestId:int}")]
        public async Task<ActionResult<ShiftExchangeActionResultDto>> DeclineRequest([FromRoute] int requestId, [FromQuery] int responderStaffId, CancellationToken ct)
        {
            var req = await _db.ShiftRequests
                .FirstOrDefaultAsync(r => r.RequestId == requestId && r.ToStaffId == responderStaffId && r.Status == "Pending", ct);

            if (req is null)
            {
                return BadRequest(new ShiftExchangeActionResultDto("error", "Yêu cầu không tồn tại hoặc không thuộc về bạn."));
            }

            req.Status = "Rejected";
            req.ApprovedByTo = false;

            await _db.SaveChangesAsync(ct);

            return Ok(new ShiftExchangeActionResultDto("success", "Đã từ chối yêu cầu."));
        }

        [HttpPost("cancel/{requestId:int}")]
        public async Task<ActionResult<ShiftExchangeActionResultDto>> CancelRequest([FromRoute] int requestId, [FromQuery] int staffId, CancellationToken ct)
        {
            var req = await _db.ShiftRequests
                .FirstOrDefaultAsync(r => r.RequestId == requestId && r.EmployeeId == staffId && r.Status == "Pending", ct);

            if (req is null)
            {
                return BadRequest(new ShiftExchangeActionResultDto("error", "Yêu cầu không tồn tại hoặc không thể huỷ."));
            }

            req.Status = "Cancelled";

            await _db.SaveChangesAsync(ct);

            return Ok(new ShiftExchangeActionResultDto("success", "Đã huỷ yêu cầu."));
        }
    }
}
