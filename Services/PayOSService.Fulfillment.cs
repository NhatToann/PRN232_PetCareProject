using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services
{
    /// <summary>
    /// Phần cập nhật trạng thái DB + gửi hoá đơn sau khi PayOS báo thanh toán thành công.
    /// Xem <see cref="PayOSService"/> để phần tạo link thanh toán và xử lý webhook.
    /// </summary>
    public sealed partial class PayOSService
    {
        private async Task UpdateSpaBookingStatusAsync(int? bookingId, string status, CancellationToken ct)
        {
            if (!bookingId.HasValue)
            {
                return;
            }

            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingId == bookingId.Value, ct);
            if (booking is null)
            {
                return;
            }

            booking.Status = status;
            booking.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync(ct);
        }

        private async Task UpdateBoardingBookingStatusAsync(int? bookingId, string status, CancellationToken ct)
        {
            if (!bookingId.HasValue)
            {
                return;
            }

            var booking = await _db.BoardingBookings.FirstOrDefaultAsync(b => b.BookingId == bookingId.Value, ct);
            if (booking is null)
            {
                return;
            }

            booking.Status = status;
            booking.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync(ct);

            await SendBoardingInvoiceEmailAsync(booking.BookingId, ct);
        }

        private async Task SendBoardingInvoiceEmailAsync(int bookingId, CancellationToken ct)
        {
            try
            {
                var booking = await _db.BoardingBookings
                    .Include(b => b.Customer)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.BookingId == bookingId, ct);

                if (booking?.Customer is null || string.IsNullOrWhiteSpace(booking.Customer.Email))
                {
                    return;
                }

                var room = await _db.BoardingRooms
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.RoomType == booking.RoomType, ct);

                var petCount = ParsePetCount(booking.PetInfo);

                var roomDisplay = !string.IsNullOrWhiteSpace(room?.RoomName)
                    ? room!.RoomName.Trim()
                    : booking.RoomType;

                await _emailService.SendBoardingInvoiceEmailAsync(
                    booking.Customer.Email,
                    booking.Customer.Name,
                    $"BR-{booking.BookingId:D4}",
                    roomDisplay,
                    booking.CheckInDate,
                    booking.CheckOutDate,
                    booking.BoardingDays,
                    petCount,
                    booking.SpecialNotes,
                    booking.PricePerDay,
                    booking.TotalPrice,
                    "Thanh toán trực tuyến (PayOS)",
                    "Đã thanh toán",
                    ct
                );
                _logger.Line($"[PayOS] Boarding invoice email sent for booking #{bookingId}.");
            }
            catch (Exception ex)
            {
                _logger.Line($"[PayOS] Failed to send boarding invoice email: {ex.Message}");
            }
        }

        private static int ParsePetCount(string? petInfo)
        {
            if (string.IsNullOrWhiteSpace(petInfo))
                return 1;

            var match = System.Text.RegularExpressions.Regex.Match(petInfo, @"\d+");
            return match.Success ? int.Parse(match.Value) : 1;
        }

        private async Task UpdateOrderStatusAsync(int? orderId, CancellationToken ct)
        {
            if (!orderId.HasValue)
            {
                return;
            }

            var order = await _db.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.OrderId == orderId.Value, ct);
            if (order is null)
            {
                _logger.Line($"[PayOS] Order #{orderId} not found.");
                return;
            }

            // Cap nhat payment status
            order.PaymentStatus = "Đã thanh toán";
            // Chuyen tu "Cho thanh toan" sang "Cho xac nhan" sau khi quet QR thanh cong
            if (string.Equals(order.Status, "Chờ thanh toán", StringComparison.OrdinalIgnoreCase))
            {
                order.Status = "Chờ xác nhận";
            }
            order.PaidAt = DateTime.Now;

            // Tru stock cho tung san pham trong don hang
            foreach (var detail in order.OrderDetails.Where(d => d.ProductId != null))
            {
                var product = await _db.Products.FirstOrDefaultAsync(p => p.ProductId == detail.ProductId, ct);
                if (product != null && product.StockQuantity > 0)
                {
                    var qty = detail.Quantity ?? 1;
                    product.StockQuantity = Math.Max(0, product.StockQuantity - qty);
                }
            }

            await _db.SaveChangesAsync(ct);
            _logger.Line($"[PayOS] Order #{orderId} updated. Status -> Chờ xác nhận, PaymentStatus -> Đã thanh toán.");
        }

        private async Task CancelOrderAsync(int? orderId, CancellationToken ct)
        {
            if (!orderId.HasValue)
            {
                return;
            }

            var order = await _db.Orders.FirstOrDefaultAsync(o => o.OrderId == orderId.Value, ct);
            if (order is null)
            {
                _logger.Line($"[PayOS] CancelOrder: Order #{orderId} not found.");
                return;
            }

            // Chi huy neu dang o trang thai "Cho thanh toan"
            if (string.Equals(order.Status, "Chờ thanh toán", StringComparison.OrdinalIgnoreCase))
            {
                order.Status = "Đã hủy";
                order.PaymentStatus = "cancelled";
                await _db.SaveChangesAsync(ct);
                _logger.Line($"[PayOS] Order #{orderId} cancelled.");
            }
            else
            {
                _logger.Line($"[PayOS] Order #{orderId} has status '{order.Status}', not cancelling.");
            }
        }

        private async Task SendSpaInvoiceEmailAsync(int? bookingId, string paymentStatusLabel, CancellationToken ct)
        {
            if (!bookingId.HasValue)
            {
                return;
            }

            var booking = await _db.Bookings
                .AsNoTracking()
                .Include(b => b.Customer)
                .Include(b => b.BookingServices)
                .ThenInclude(bs => bs.Service)
                .FirstOrDefaultAsync(b => b.BookingId == bookingId.Value, ct);

            if (booking?.Customer?.Email is null)
            {
                return;
            }

            var items = booking.BookingServices
                .Select(bs => (
                    ItemName: bs.Service?.Name ?? "Dich vu spa",
                    Quantity: bs.Quantity,
                    UnitPrice: bs.UnitPrice ?? 0m
                ))
                .ToList();

            var total = items.Sum(item => item.UnitPrice * item.Quantity);
            if (total <= 0)
            {
                total = booking.BookingServices.Sum(bs => (bs.UnitPrice ?? 0m) * bs.Quantity);
            }

            await _emailService.SendSpaInvoiceEmailAsync(
                booking.Customer.Email,
                booking.Customer.Name,
                $"SPA-{booking.BookingId}",
                items,
                total,
                paymentStatusLabel,
                ct);
        }
    }
}