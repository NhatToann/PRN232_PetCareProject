using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Services
{
    /// <summary>
    /// Pháº§n táº¡o booking spa + lá»‹ch sá»­ + cáº­p nháº­t tráº¡ng thĂ¡i + Ä‘Ă¡nh giĂ¡.
    /// Xem <see cref="SpaBookingService"/> Ä‘á»ƒ pháº§n Ä‘á»c dá»¯ liá»‡u, kiá»ƒm tra kháº£ dá»¥ng vĂ  Æ°á»›c tĂ­nh giĂ¡.
    /// </summary>
    public sealed partial class SpaBookingService
    {
        public async Task<SpaBookingDto> CreateBookingAsync(int customerId, CreateSpaBookingRequest request, CancellationToken ct = default)
        {
            if (request.PetIds == null || request.PetIds.Count == 0)
            {
                throw new ArgumentException("Vui lòng chọn pet.");
            }

            var estimate = await EstimateAsync(customerId, new SpaBookingEstimateRequest(request.PetIds, request.Items), ct);

            var appointmentStart = request.AppointmentStart;
            if (appointmentStart.Kind == DateTimeKind.Unspecified)
            {
                appointmentStart = DateTime.SpecifyKind(appointmentStart, DateTimeKind.Local);
            }
            else if (appointmentStart.Kind == DateTimeKind.Utc)
            {
                appointmentStart = appointmentStart.ToLocalTime();
            }

            if (appointmentStart <= DateTime.Now)
            {
                throw new InvalidOperationException("Thời gian hẹn không được nằm trong quá khứ.");
            }

            var startTime = TimeOnly.FromDateTime(appointmentStart);

            // Ca tối (>= 19:00) không nhận đặt dịch vụ
            if (startTime >= EveningShiftStart)
            {
                throw new InvalidOperationException("Ca tối (bắt đầu từ 19:00) không nhận đặt dịch vụ. Vui lòng chọn ca sáng hoặc ca chiều.");
            }

            // Giờ đặt muộn nhất là 22:00
            if (startTime >= LatestBookingTime)
            {
                throw new InvalidOperationException($"Giờ bắt đầu ({startTime:HH\\:mm}) sau giới hạn đặt lịch (22:00). Vui lòng chọn thời gian sớm hơn.");
            }

            var appointmentEnd = appointmentStart.AddMinutes(estimate.TotalDurationMin);

            var pets = await _repo.GetPetsByCustomerIdAsync(customerId, ct);
            var selectedPets = pets.Where(p => request.PetIds.Contains(p.PetId)).ToList();

            if (selectedPets.Count == 0)
            {
                throw new InvalidOperationException("Không tìm thấy pet của khách hàng.");
            }

            await SchedulingLock.WaitAsync(ct);
            // Theo dõi các booking đã tạo trong request để rollback nếu fail giữa chừng (tránh partial success).
            var createdBookingIds = new List<int>();

            try
            {
                var createdBookingId = 0;
                string? firstPetName = null;
                var paymentMethod = (request.PaymentMethod ?? "cash").Trim().ToLowerInvariant();
                var initialStatus = paymentMethod == "payos" ? "Chờ thanh toán PayOS" : "Chưa thanh toán";
                var nextPreferredStart = appointmentStart;
                DateTime? latestEnd = null;
                var plannedSlots = new List<(DateTime Start, DateTime End)>();

                var petIndex = 0;
                while (petIndex < selectedPets.Count)
                {
                    var petsInThisWave = Math.Min(2, selectedPets.Count - petIndex);

                    DateTime scheduledStart;
                    if (petIndex == 0)
                    {
                        // Rule hiện tại: wave đầu phải kiểm tra đúng ngay khung giờ khách chọn; full thì báo lỗi, không auto đẩy.
                        var slotStep = GetSlotStepMinutes();
                        scheduledStart = AlignToSlot(nextPreferredStart, slotStep);
                        var firstWaveEnd = scheduledStart.AddMinutes(estimate.PerPetDurationMin);
                        var existingSpa = await _repo.CountSpaBookingsInTimeSlotAsync(scheduledStart, firstWaveEnd, ct);

                        if (existingSpa + petsInThisWave > 2)
                        {
                            var suggested = await FindNearestAvailableStartAsync(scheduledStart, estimate.PerPetDurationMin, petsInThisWave, 2, ct);
                            throw new SlotCapacityException("Khung giờ đã đủ 2 slot.", suggested);
                        }
                    }
                    else
                    {
                        // Wave sau: tự tìm khung gần nhất còn slot để xếp các pet còn lại theo chuỗi.
                        scheduledStart = await FindNearestAvailableStartWithPlannedAsync(nextPreferredStart, estimate.PerPetDurationMin, petsInThisWave, 2, plannedSlots, ct)
                            ?? throw new SlotCapacityException("Không tìm được khung giờ phù hợp cho tất cả pet. Vui lòng chọn thời gian khác.", null);
                    }

                    DateTime currentEnd = scheduledStart.AddMinutes(estimate.PerPetDurationMin);

                    for (var waveOffset = 0; waveOffset < petsInThisWave; waveOffset++)
                    {
                        var pet = selectedPets[petIndex + waveOffset];

                        var booking = new Booking
                        {
                            CustomerId = customerId,
                            PetId = pet.PetId,
                            AppointmentStart = scheduledStart,
                            AppointmentEnd = currentEnd,
                            Status = initialStatus,
                            Note = request.Note?.Trim(),
                            CreatedAt = DateTime.Now
                        };

                        var bookingId = await _repo.CreateBookingAsync(booking, ct);
                        createdBookingIds.Add(bookingId);
                        if (createdBookingId == 0)
                        {
                            createdBookingId = bookingId;
                            firstPetName = pet.PetName;
                        }

                        foreach (var item in request.Items)
                        {
                            if (item.Quantity <= 0)
                            {
                                continue;
                            }

                            var pricing = await _repo.GetServicePricingAsync(item.ServiceId, pet.BreedId, ct)
                                ?? throw new InvalidOperationException("Dịch vụ không hợp lệ.");

                            var bookingService = new BookingService
                            {
                                BookingId = bookingId,
                                ServiceId = item.ServiceId,
                                Quantity = item.Quantity,
                                UnitPrice = pricing.UnitPrice,
                                DurationMin = pricing.DurationMin,
                                Note = string.Empty,
                                CreatedAt = DateTime.Now
                            };

                            await _repo.AddBookingServiceAsync(bookingService, ct);
                        }

                        plannedSlots.Add((scheduledStart, currentEnd));
                        latestEnd = !latestEnd.HasValue || currentEnd > latestEnd.Value ? currentEnd : latestEnd;
                    }

                    petIndex += petsInThisWave;
                    nextPreferredStart = currentEnd;
                }

                var finalAppointmentEnd = latestEnd ?? appointmentEnd;

                return new SpaBookingDto(
                    createdBookingId,
                    selectedPets.First().PetId,
                    firstPetName ?? string.Empty,
                    selectedPets.First().BreedName,
                    selectedPets.First().SpeciesName,
                    null,
                    null,
                    appointmentStart,
                    finalAppointmentEnd,
                    initialStatus,
                    DateTime.Now,
                    estimate.Items.Select(i => new SpaBookingItemDto(i.ServiceId, i.ServiceName, i.Quantity, i.UnitPrice, i.DurationMin)).ToList(),
                    estimate.TotalPrice,
                    paymentMethod == "payos" ? "payos" : "cash"
                )
                {
                    BookingIds = createdBookingIds
                };
            }
            catch
            {
                // Rollback toàn bộ booking đã tạo của request này khi có lỗi.
                if (createdBookingIds.Count > 0)
                {
                    await _repo.DeleteBookingsAsync(createdBookingIds, ct);
                }

                throw;
            }
            finally
            {
                SchedulingLock.Release();
            }
        }

        public Task<IReadOnlyList<SpaBookingDto>> GetHistoryAsync(int customerId, CancellationToken ct = default)
        {
            return _repo.GetSpaBookingsByCustomerIdAsync(customerId, ct);
        }

        public Task<IReadOnlyList<SpaBookingDto>> GetAllHistoryAsync(CancellationToken ct = default)
        {
            return _repo.GetAllSpaBookingsAsync(ct);
        }

        public Task<SpaBookingInvoiceDto?> GetInvoiceAsync(int bookingId, CancellationToken ct = default)
        {
            return _repo.GetSpaBookingInvoiceAsync(bookingId, ct);
        }

        public async Task UpdateStatusAsync(int bookingId, string status, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                throw new ArgumentException("Trạng thái không hợp lệ.");
            }

            var ok = await _repo.UpdateBookingStatusAsync(bookingId, status.Trim(), ct);
            if (!ok)
            {
                throw new InvalidOperationException("Không tìm thấy booking để cập nhật trạng thái.");
            }
        }

        public async Task<int> UpsertReviewAsync(int customerId, CreateSpaReviewRequest request, CancellationToken ct = default)
        {
            if (request.Rating < 1 || request.Rating > 5)
            {
                throw new ArgumentException("Rating phải từ 1 đến 5.");
            }

            var canReview = await _repo.HasCompletedBookingAsync(customerId, request.BookingId, request.ServiceId, ct);
            if (!canReview)
            {
                throw new InvalidOperationException("Bạn chưa hoàn thành dịch vụ này.");
            }

            // Cho phép đánh giá nhiều lần: luôn tạo review mới thay vì ghi đè review cũ.
            var newReview = new Review
            {
                CustomerId = customerId,
                BookingId = request.BookingId,
                ServiceId = request.ServiceId,
                Rating = request.Rating,
                Comment = request.Comment?.Trim(),
                CreatedAt = DateTime.Now
            };

            await _repo.AddReviewAsync(newReview, ct);
            return newReview.ReviewId;
        }

        private async Task<DateTime?> FindNearestAvailableStartAsync(
            DateTime from,
            int durationMin,
            int quantity,
            int maxCapacity,
            CancellationToken ct)
        {
            var slotStep = GetSlotStepMinutes();
            var candidate = AlignToSlot(from, slotStep).AddMinutes(slotStep);
            const int maxChecks = 96;

            for (var i = 0; i < maxChecks; i++)
            {
                var candidateEnd = candidate.AddMinutes(durationMin);
                var existingSpa = await _repo.CountSpaBookingsInTimeSlotAsync(candidate, candidateEnd, ct);
                if (existingSpa + quantity <= maxCapacity)
                {
                    return candidate;
                }

                candidate = candidate.AddMinutes(slotStep);
            }

            return null;
        }

        private static int GetSlotStepMinutes()
        {
            // Tách thành hàm riêng để sau này đổi bước slot (15/20/30 phút...) chỉ cần sửa 1 chỗ.
            return 30;
        }

        private static DateTime AlignToSlot(DateTime value, int stepMinutes)
        {
            if (stepMinutes <= 0)
            {
                return value;
            }

            var minute = value.Minute;
            var remainder = minute % stepMinutes;

            if (remainder == 0 && value.Second == 0 && value.Millisecond == 0)
            {
                return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind);
            }

            var aligned = value.AddMinutes(stepMinutes - remainder);
            return new DateTime(aligned.Year, aligned.Month, aligned.Day, aligned.Hour, aligned.Minute, 0, aligned.Kind);
        }

        private async Task<DateTime?> FindNearestAvailableStartWithPlannedAsync(
            DateTime from,
            int durationMin,
            int quantity,
            int maxCapacity,
            IReadOnlyCollection<(DateTime Start, DateTime End)> plannedSlots,
            CancellationToken ct)
        {
            var stepMinutes = GetSlotStepMinutes();
            var candidate = AlignToSlot(from, stepMinutes);
            const int maxChecks = 96;

            for (var i = 0; i < maxChecks; i++)
            {
                var candidateEnd = candidate.AddMinutes(durationMin);
                var existingSpa = await _repo.CountSpaBookingsInTimeSlotAsync(candidate, candidateEnd, ct);

                var plannedOverlap = plannedSlots.Count(p => p.Start < candidateEnd && p.End > candidate);
                if (existingSpa + plannedOverlap + quantity <= maxCapacity)
                {
                    return candidate;
                }

                candidate = candidate.AddMinutes(stepMinutes);
            }

            return null;
        }
    }
}
