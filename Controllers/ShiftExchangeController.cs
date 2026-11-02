using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Controllers
{
    /// <summary>
    /// API đổi ca / nhờ làm thay cho nhân viên.
    ///
    /// Được chia thành 3 partial class để mỗi file nằm dưới ngưỡng 500 dòng:
    ///   - ShiftExchangeController.cs            → hạ tầng + endpoint đọc (lịch, lựa chọn).
    ///   - ShiftExchangeController.Requests.cs   → tạo yêu cầu (nhờ làm thay / đổi ca).
    ///   - ShiftExchangeController.Processing.cs  → duyệt / từ chối / huỷ.
    /// </summary>
    [ApiController]
    [Route("api/shift-exchange")]
    public sealed partial class ShiftExchangeController : ControllerBase
    {
        private readonly ShopPetDatabaseContext _db;
        private readonly ILogger<ShiftExchangeController> _logger;
        private readonly string[] _allowedIpPrefixes;

        private readonly IAttendanceService _attendanceService;

        public ShiftExchangeController(
            ShopPetDatabaseContext db,
            IConfiguration configuration,
            IAttendanceService attendanceService,
            ILogger<ShiftExchangeController> logger)
        {
            _db = db;
            _logger = logger;
            _allowedIpPrefixes = configuration.GetSection("Attendance:AllowedIpPrefixes")
                .Get<string[]>() ?? new[] { "192.168.1." };
            _attendanceService = attendanceService;
        }

        [HttpGet("verify-network")]
        public ActionResult<VerifyNetworkDto> VerifyNetwork()
        {
            var xClientIp = Request.Headers["X-Client-IP"].FirstOrDefault();
            var realClientIp = HttpContext.Items["RealClientIp"] as string;
            var xForwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            var xRealIp = Request.Headers["X-Real-IP"].FirstOrDefault();
            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();

            var socketRemoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            var isLoopback = !string.IsNullOrEmpty(socketRemoteIp) &&
                (socketRemoteIp == "::1" || socketRemoteIp == "127.0.0.1");

            _logger.Line($"[VerifyNetwork] Headers -> X-Client-IP: [{xClientIp}], X-Real-IP: [{xRealIp}], X-Forwarded-For: [{xForwardedFor}], RemoteIp: [{remoteIp}], SocketLoopback: {isLoopback}");

            var raw = xClientIp
                ?? xRealIp
                ?? realClientIp
                ?? (xForwardedFor?.Split(',')[0].Trim())
                ?? remoteIp
                ?? "unknown";

            var ip = NormalizeClientIp(raw);
            var isCompanyNetwork = _allowedIpPrefixes.Any(prefix =>
                ip.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            _logger.Line($"[VerifyNetwork] Resolved IP: [{ip}], IsCompanyNetwork: {isCompanyNetwork}");

            return Ok(new VerifyNetworkDto(
                ip,
                isCompanyNetwork,
                string.Join(", ", _allowedIpPrefixes),
                xClientIp,
                realClientIp,
                xForwardedFor,
                remoteIp,
                raw,
                xRealIp
            ));
        }

        private static string NormalizeClientIp(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                return "unknown";

            var s = raw.Split('%')[0].Trim();

            if (!IPAddress.TryParse(s, out var addr))
                return raw;

            if (IPAddress.IsLoopback(addr))
                return "127.0.0.1";

            if (addr.AddressFamily == AddressFamily.InterNetworkV6 && addr.IsIPv4MappedToIPv6)
                return addr.MapToIPv4().ToString();

            return addr.ToString();
        }

        /// <summary>
        /// Cùng nhân viên + ngày + khung giờ: giữ schedule_id nhỏ nhất, xóa các dòng còn lại.
        /// </summary>
        private async Task RemoveDuplicateSlotRowsForStaffAsync(
            int staffId,
            DateOnly workDate,
            TimeOnly startTime,
            TimeOnly endTime,
            CancellationToken ct)
        {
            var rows = await _db.WorkSchedules
                .Where(s =>
                    s.StaffId == staffId &&
                    s.WorkDate == workDate &&
                    s.StartTime == startTime &&
                    s.EndTime == endTime)
                .OrderBy(s => s.ScheduleId)
                .ToListAsync(ct);

            if (rows.Count <= 1)
                return;

            _db.WorkSchedules.RemoveRange(rows.Skip(1));
        }

        [HttpGet("staff/{staffId:int}/my-shifts")]
        public async Task<ActionResult<IReadOnlyList<ShiftOptionDto>>> GetMyShifts([FromRoute] int staffId, CancellationToken ct)
        {
            var nowDate = DateOnly.FromDateTime(DateTime.Now);
            var shifts = await _db.WorkSchedules
                .AsNoTracking()
                .Where(x => x.StaffId == staffId && x.WorkDate >= nowDate)
                .OrderBy(x => x.WorkDate)
                .ThenBy(x => x.StartTime)
                .Take(60)
                .Select(x => new ShiftOptionDto(
                    x.ScheduleId,
                    x.StaffId,
                    x.Staff != null ? x.Staff.Name : null,
                    x.WorkDate,
                    x.ShiftId,
                    x.StartTime.ToTimeSpan(),
                    x.EndTime.ToTimeSpan(),
                    x.Status
                ))
                .ToListAsync(ct);

            return Ok(WorkScheduleDedup.ShiftOptions(shifts));
        }

        [HttpGet("all-shifts")]
        public async Task<ActionResult<IReadOnlyList<ShiftOptionDto>>> GetAllShifts(CancellationToken ct)
        {
            var nowDate = DateOnly.FromDateTime(DateTime.Now);
            var shifts = await _db.WorkSchedules
                .AsNoTracking()
                .Where(x => x.WorkDate >= nowDate)
                .OrderBy(x => x.WorkDate)
                .ThenBy(x => x.StartTime)
                .Take(300)
                .Select(x => new ShiftOptionDto(
                    x.ScheduleId,
                    x.StaffId,
                    x.Staff != null ? x.Staff.Name : null,
                    x.WorkDate,
                    x.ShiftId,
                    x.StartTime.ToTimeSpan(),
                    x.EndTime.ToTimeSpan(),
                    x.Status
                ))
                .ToListAsync(ct);

            return Ok(WorkScheduleDedup.ShiftOptions(shifts));
        }

        [HttpGet("staff/options")]
        public async Task<ActionResult<IReadOnlyList<StaffOptionDto>>> GetStaffOptions([FromQuery] int excludeStaffId, CancellationToken ct)
        {
            var list = await _db.Staff
                .AsNoTracking()
                .Where(x => x.StaffId != excludeStaffId)
                .OrderBy(x => x.Name)
                .Select(x => new StaffOptionDto(x.StaffId, x.Name))
                .ToListAsync(ct);

            return Ok(list);
        }

        [HttpGet("pending")]
        public async Task<ActionResult<IReadOnlyList<PendingRequestDto>>> GetPendingRequests([FromQuery] int staffId, CancellationToken ct)
        {
            var requests = await _db.ShiftRequests
                .AsNoTracking()
                .Where(r => r.ToStaffId == staffId && r.Status == "Pending")
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new PendingRequestDto(
                    r.RequestId,
                    r.EmployeeId,
                    r.Employee != null ? r.Employee.Name : null,
                    r.ToStaffId,
                    r.Type ?? "",
                    r.FromDate ?? DateOnly.MinValue,
                    r.ToDate,
                    r.FromShiftId,
                    r.ToShiftId,
                    r.Reason ?? "",
                    r.Status ?? "",
                    r.CreatedAt ?? DateTime.MinValue
                ))
                .ToListAsync(ct);

            return Ok(requests);
        }

        [HttpGet("my-requests")]
        public async Task<ActionResult<IReadOnlyList<MyRequestDto>>> GetMyRequests([FromQuery] int staffId, CancellationToken ct)
        {
            var requests = await _db.ShiftRequests
                .AsNoTracking()
                .Where(r => r.EmployeeId == staffId)
                .OrderByDescending(r => r.CreatedAt)
                .Take(50)
                .Select(r => new MyRequestDto(
                    r.RequestId,
                    r.ToStaffId,
                    r.ToStaffId != null ? _db.Staff.Where(s => s.StaffId == r.ToStaffId).Select(s => s.Name).FirstOrDefault() : null,
                    r.Type ?? "",
                    r.FromDate ?? DateOnly.MinValue,
                    r.ToDate,
                    r.Status ?? "",
                    r.Reason ?? "",
                    r.CreatedAt ?? DateTime.MinValue,
                    r.ApprovedByTo == true
                ))
                .ToListAsync(ct);

            return Ok(requests);
        }
    }
}
