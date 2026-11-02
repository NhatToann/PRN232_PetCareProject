using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Services
{
    public sealed partial class SpaBookingService : ISpaBookingService
    {
        // Lock toàn cục cho scheduling để tránh 2 request tạo booking cùng lúc gây đè slot (race condition).
        private static readonly SemaphoreSlim SchedulingLock = new(1, 1);

        // Ca tối bắt đầu từ 19:00 — trực ca, không nhận đặt dịch vụ
        private static readonly TimeOnly EveningShiftStart = new(19, 0, 0);
        // Giờ đặt muộn nhất: 22:00, không ai làm việc sau giờ này
        private static readonly TimeOnly LatestBookingTime = new(22, 0, 0);

        private readonly ISpaBookingRepository _repo;
        private readonly ShopPetDatabaseContext _db;

        public SpaBookingService(ISpaBookingRepository repo, ShopPetDatabaseContext db)
        {
            _repo = repo;
            _db = db;
        }

        public Task<IReadOnlyList<PetSummaryDto>> GetPetsAsync(int customerId, CancellationToken ct = default)
        {
            return _repo.GetPetsByCustomerIdAsync(customerId, ct);
        }

        public Task<IReadOnlyList<SpaServiceDto>> GetSpaServicesAsync(CancellationToken ct = default)
        {
            return _repo.GetActiveSpaServicesAsync(ct);
        }

        public async Task<SpaAvailabilityResponse> CheckAvailabilityAsync(DateTime start, int durationMin, int quantity, CancellationToken ct = default)
        {
            const int maxCapacity = 2;

            var startTime = TimeOnly.FromDateTime(start);

            // Ca tối (>= 19:00) không nhận đặt dịch vụ — kiểm tra trước, không phụ thuộc quantity
            if (startTime >= EveningShiftStart)
            {
                return new SpaAvailabilityResponse(
                    false, 0, maxCapacity, null,
                    "Ca tối (bắt đầu từ 19:00) không nhận đặt dịch vụ. Vui lòng chọn ca sáng hoặc ca chiều."
                );
            }

            // Giờ đặt muộn nhất là 22:00
            if (startTime >= LatestBookingTime)
            {
                return new SpaAvailabilityResponse(
                    false, 0, maxCapacity, null,
                    $"Giờ bắt đầu ({startTime:HH\\:mm}) sau giới hạn đặt lịch (22:00). Vui lòng chọn thời gian sớm hơn."
                );
            }

            if (durationMin <= 0 || quantity <= 0)
            {
                return new SpaAvailabilityResponse(true, 0, maxCapacity, null, null);
            }

            var end = start.AddMinutes(durationMin);
            var existing = await _repo.CountSpaBookingsInTimeSlotAsync(start, end, ct);
            var canBook = existing + quantity <= maxCapacity;

            if (canBook)
            {
                return new SpaAvailabilityResponse(true, existing, maxCapacity, null, null);
            }

            var suggested = await FindNearestAvailableStartAsync(start, durationMin, quantity, maxCapacity, ct);
            var message = suggested.HasValue
                ? $"Khung giờ đã đủ {maxCapacity} slot. Gợi ý gần nhất: {suggested.Value:dd/MM/yyyy HH:mm}."
                : $"Khung giờ đã đủ {maxCapacity} slot. Vui lòng chọn thời gian khác.";

            return new SpaAvailabilityResponse(false, existing, maxCapacity, suggested, message);
        }

        public async Task<BookingSlotValidationResult> ValidateBookingSlotAsync(BookingSlotValidationRequest request, CancellationToken ct = default)
        {
            var appointmentDate = DateOnly.FromDateTime(request.AppointmentStart);
            var appointmentEnd = request.AppointmentStart.AddMinutes(request.DurationMinutes);
            var appointmentStartTime = TimeOnly.FromDateTime(request.AppointmentStart);

            if (request.DurationMinutes <= 0)
            {
                return new BookingSlotValidationResult(
                    false,
                    request.AppointmentStart,
                    appointmentEnd,
                    request.DurationMinutes,
                    "Thời gian dịch vụ phải lớn hơn 0 phút.",
                    Array.Empty<BookingSlotShiftDto>(),
                    Array.Empty<BookingSlotShiftDto>()
                );
            }

            // Ca tối (>= 19:00) không nhận đặt dịch vụ
            if (appointmentStartTime >= EveningShiftStart)
            {
                return new BookingSlotValidationResult(
                    false,
                    request.AppointmentStart,
                    appointmentEnd,
                    request.DurationMinutes,
                    $"Ca tối (bắt đầu từ 19:00) không nhận đặt dịch vụ. Vui lòng chọn ca sáng hoặc ca chiều.",
                    Array.Empty<BookingSlotShiftDto>(),
                    Array.Empty<BookingSlotShiftDto>()
                );
            }

            // Giờ đặt muộn nhất là 22:00
            if (appointmentStartTime >= LatestBookingTime)
            {
                return new BookingSlotValidationResult(
                    false,
                    request.AppointmentStart,
                    appointmentEnd,
                    request.DurationMinutes,
                    $"Giờ bắt đầu ({appointmentStartTime:HH\\:mm}) sau giới hạn đặt lịch (22:00). Vui lòng chọn thời gian sớm hơn.",
                    Array.Empty<BookingSlotShiftDto>(),
                    Array.Empty<BookingSlotShiftDto>()
                );
            }

            var shiftsOnDate = await _db.WorkSchedules
                .AsNoTracking()
                .Where(w => w.WorkDate == appointmentDate)
                .OrderBy(w => w.StartTime)
                .ToListAsync(ct);

            var shifts = WorkScheduleDedup.ByTimeSlot(shiftsOnDate);

            // Chỉ giữ lại ca sáng và ca chiều (bỏ ca tối)
            var workShifts = shifts
                .Where(s => s.StartTime < EveningShiftStart)
                .ToList();

            // Kiểm tra xem appointment có nằm trọn trong ca nào không
            bool fitsInAnyShift = false;
            var available = new List<BookingSlotShiftDto>();
            var conflicting = new List<BookingSlotShiftDto>();

            foreach (var shift in workShifts)
            {
                var shiftStartDt = appointmentDate.ToDateTime(shift.StartTime);
                var shiftEndDt = appointmentDate.ToDateTime(shift.EndTime);

                if (request.AppointmentStart >= shiftStartDt && appointmentEnd <= shiftEndDt)
                {
                    string label;
                    if (shift.EndTime <= new TimeOnly(12, 0, 0))
                        label = $"Ca sáng ({shift.StartTime:HH\\:mm}–{shift.EndTime:HH\\:mm})";
                    else
                        label = $"Ca chiều ({shift.StartTime:HH\\:mm}–{shift.EndTime:HH\\:mm})";
                    available.Add(new BookingSlotShiftDto(shift.ScheduleId, shift.StartTime, shift.EndTime, label));
                    fitsInAnyShift = true;
                }
                else
                {
                    conflicting.Add(new BookingSlotShiftDto(shift.ScheduleId, shift.StartTime, shift.EndTime,
                        $"{shift.StartTime:HH\\:mm}–{shift.EndTime:HH\\:mm}"));
                }
            }

            string? message = null;
            if (!fitsInAnyShift && workShifts.Count > 0)
            {
                var firstConflict = conflicting.First();
                var appointmentEndTime = TimeOnly.FromDateTime(appointmentEnd);

                if (appointmentEndTime <= firstConflict.StartTime)
                {
                    message = $"Dịch vụ kết thúc lúc {appointmentEndTime:HH\\:mm}, ca làm việc bắt đầu lúc {firstConflict.StartTime:HH\\:mm}. " +
                              $"Vui lòng đặt lại từ {firstConflict.StartTime:HH\\:mm}.";
                }
                else if (appointmentStartTime >= firstConflict.EndTime)
                {
                    message = $"Giờ bắt đầu ({appointmentStartTime:HH\\:mm}) sau ca làm việc kết thúc ({firstConflict.EndTime:HH\\:mm}). " +
                              $"Vui lòng chọn ca khác.";
                }
                else if (appointmentStartTime < firstConflict.StartTime)
                {
                    message = $"Dịch vụ kết thúc lúc {appointmentEndTime:HH\\:mm}, ca làm việc bắt đầu lúc {firstConflict.StartTime:HH\\:mm}. " +
                              $"Vui lòng đặt lại từ {firstConflict.StartTime:HH\\:mm}.";
                }
                else
                {
                    message = $"Dịch vụ kết thúc lúc {appointmentEndTime:HH\\:mm}, ca làm việc kết thúc lúc {firstConflict.EndTime:HH\\:mm}. " +
                              $"Vui lòng rút ngắn dịch vụ hoặc đặt lại.";
                }
            }

            return new BookingSlotValidationResult(
                fitsInAnyShift,
                request.AppointmentStart,
                appointmentEnd,
                request.DurationMinutes,
                message,
                available,
                conflicting
            );
        }

        public async Task<SpaBookingEstimateDto> EstimateAsync(int customerId, SpaBookingEstimateRequest request, CancellationToken ct = default)
        {
            if (request.PetIds == null || request.PetIds.Count == 0)
            {
                throw new ArgumentException("Vui lòng chọn pet.");
            }

            if (request.Items == null || request.Items.Count == 0)
            {
                throw new ArgumentException("Giỏ dịch vụ trống.");
            }

            var pets = await _repo.GetPetsByCustomerIdAsync(customerId, ct);
            var selectedPets = pets.Where(p => request.PetIds.Contains(p.PetId)).ToList();

            if (selectedPets.Count == 0)
            {
                throw new InvalidOperationException("Không tìm thấy pet của khách hàng.");
            }

            var items = new List<SpaBookingEstimateItemDto>();
            int perPetDuration = 0;
            decimal totalPrice = 0m;

            foreach (var item in request.Items)
            {
                if (item.Quantity <= 0)
                {
                    continue;
                }

                var pricing = await _repo.GetServicePricingAsync(item.ServiceId, selectedPets[0].BreedId, ct)
                    ?? throw new InvalidOperationException("Dịch vụ không hợp lệ.");

                perPetDuration += pricing.DurationMin * item.Quantity;
            }

            foreach (var pet in selectedPets)
            {
                foreach (var item in request.Items)
                {
                    if (item.Quantity <= 0)
                    {
                        continue;
                    }

                    var pricing = await _repo.GetServicePricingAsync(item.ServiceId, pet.BreedId, ct)
                        ?? throw new InvalidOperationException("Dịch vụ không hợp lệ.");

                    items.Add(new SpaBookingEstimateItemDto(
                        pet.PetId,
                        pet.PetName,
                        item.ServiceId,
                        pricing.ServiceName,
                        item.Quantity,
                        pricing.UnitPrice,
                        pricing.DurationMin
                    ));

                    totalPrice += pricing.UnitPrice * item.Quantity;
                }
            }

            var slotStep = GetSlotStepMinutes();
            if (perPetDuration <= 0)
            {
                // fallback để tránh duration=0 khiến xếp lịch dồn cùng 1 thời điểm
                perPetDuration = slotStep;
            }

            int totalDuration = perPetDuration * selectedPets.Count;

            return new SpaBookingEstimateDto(items, perPetDuration, totalDuration, selectedPets.Count, totalPrice);
        }
    }
}

