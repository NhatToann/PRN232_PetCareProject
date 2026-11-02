using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Controllers
{
    [ApiController]
    [Route("api/spa-booking")]
    public sealed class SpaBookingController : ControllerBase
    {
        private readonly ISpaBookingService _service;
        private readonly ILogger<SpaBookingController> _logger;

        public SpaBookingController(ISpaBookingService service,
            ILogger<SpaBookingController> logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet("pets")]
        public async Task<ActionResult<IReadOnlyList<PetSummaryDto>>> GetPets([FromQuery] int customerId, CancellationToken ct)
        {
            var pets = await _service.GetPetsAsync(customerId, ct);
            return Ok(pets);
        }

        [HttpGet("services")]
        public async Task<ActionResult<IReadOnlyList<SpaServiceDto>>> GetServices(CancellationToken ct)
        {
            var services = await _service.GetSpaServicesAsync(ct);
            return Ok(services);
        }

        [HttpPost("availability")]
        public async Task<ActionResult<SpaAvailabilityResponse>> CheckAvailability([FromBody] SpaAvailabilityRequest request, CancellationToken ct)
        {
            var result = await _service.CheckAvailabilityAsync(request.Start, request.DurationMin, request.Quantity, ct);
            return Ok(result);
        }

        [HttpPost("validate-slot")]
        public async Task<ActionResult<BookingSlotValidationResult>> ValidateSlot([FromBody] BookingSlotValidationRequest request, CancellationToken ct)
        {
            var result = await _service.ValidateBookingSlotAsync(request, ct);
            if (!result.IsValid)
                return BadRequest(result);
            return Ok(result);
        }

        [HttpPost("estimate")]
        public async Task<ActionResult<SpaBookingEstimateDto>> Estimate([FromQuery] int customerId, [FromBody] SpaBookingEstimateRequest request, CancellationToken ct)
        {
            var result = await _service.EstimateAsync(customerId, request, ct);
            return Ok(result);
        }

        [HttpPost]
        public async Task<ActionResult<SpaBookingDto>> Create([FromQuery] int customerId, [FromBody] CreateSpaBookingRequest request, CancellationToken ct)
        {
            try
            {
                var result = await _service.CreateBookingAsync(customerId, request, ct);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (SlotCapacityException ex)
            {
                // Trả suggestedStart field riêng để frontend popup dùng trực tiếp, không phải parse message.
                return BadRequest(new { message = ex.Message, suggestedStart = ex.SuggestedStart?.ToString("o") });
            }
            catch (InvalidOperationException ex)
            {
                var suggestedStart = TryExtractSuggestedStart(ex.Message);
                return BadRequest(new { message = ex.Message, suggestedStart });
            }
        }

        [HttpGet("history")]
        public async Task<ActionResult<IReadOnlyList<SpaBookingDto>>> History([FromQuery] int customerId, CancellationToken ct)
        {
            var history = await _service.GetHistoryAsync(customerId, ct);
            return Ok(history);
        }

        [HttpGet("history/all")]
        [HttpGet("all-bookings")]
        public async Task<ActionResult<IReadOnlyList<SpaBookingDto>>> HistoryAll(CancellationToken ct)
        {
            var history = await _service.GetAllHistoryAsync(ct);
            return Ok(history);
        }

        [HttpGet("{bookingId:int}/invoice")]
        public async Task<ActionResult<SpaBookingInvoiceDto>> Invoice(int bookingId, CancellationToken ct)
        {
            var invoice = await _service.GetInvoiceAsync(bookingId, ct);
            if (invoice == null)
            {
                return NotFound(new { message = "Không tìm thấy booking." });
            }

            return Ok(invoice);
        }

        [HttpPatch("{bookingId:int}/status")]
        public async Task<ActionResult<object>> UpdateStatus(int bookingId, [FromBody] UpdateBookingStatusRequest request, CancellationToken ct)
        {
            _logger.Line($"[SpaBooking] UpdateStatus {bookingId} -> {request.Status}");

            try
            {
                await _service.UpdateStatusAsync(bookingId, request.Status, ct);
                return Ok(new { message = "Updated" });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpPost("review")]
        public async Task<ActionResult<object>> Review([FromQuery] int customerId, [FromBody] CreateSpaReviewRequest request, CancellationToken ct)
        {
            try
            {
                var reviewId = await _service.UpsertReviewAsync(customerId, request, ct);
                return Ok(new { reviewId });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        public sealed record SpaAvailabilityRequest(
            [Required(ErrorMessage = "Thời gian bắt đầu không được để trống.")]
            DateTime Start,
            [Range(5, 480, ErrorMessage = "Thời lượng phải từ 5 đến 480 phút.")]
            int DurationMin,
            [Range(1, 20, ErrorMessage = "Số lượng thú cưng phải từ 1 đến 20.")]
            int Quantity);

        public sealed record UpdateBookingStatusRequest(
            [Required(ErrorMessage = "Trạng thái không được để trống.")]
            [StringLength(30, ErrorMessage = "Trạng thái không được vượt quá 30 ký tự.")]
            string Status);

        private static string? TryExtractSuggestedStart(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            var match = Regex.Match(message, @"(\d{2}/\d{2}/\d{4})\s+(\d{2}:\d{2})");
            if (!match.Success)
            {
                return null;
            }

            var text = $"{match.Groups[1].Value} {match.Groups[2].Value}";
            if (!DateTime.TryParseExact(text, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return null;
            }

            return dt.ToString("o");
        }
    }
}
