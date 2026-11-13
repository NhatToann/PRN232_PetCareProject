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
        /// <summary>
        /// Cập nhật trạng thái cho MỘT booking.
        /// </summary>
        private async Task UpdateSpaBookingStatusAsync(int? bookingId, string status, CancellationToken ct)
        {
            if (!bookingId.HasValue)
            {
                return;
            }

            var booking = await _db.Bookings
                .AsTracking()
                .FirstOrDefaultAsync(b => b.BookingId == bookingId.Value, ct);
            if (booking is null)
            {
                return;
            }

            booking.Status = status;
            booking.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync(ct);
        }

        /// <summary>
        /// Cập nhật trạng thái cho TẤT CẢ các booking thuộc cùng một payment.
        ///
        /// <para>
        /// <b>Vì sao cần:</b> khách có thể đặt spa cho N pet ⇒ hệ thống tạo N booking
        /// (mỗi pet 1 booking) nhưng gộp thành MỘT payment PayOS. Payment lưu
        /// <c>ReferenceId</c> = booking đầu tiên và ghi danh sách booking trong
        /// <c>Note</c> theo dạng <c>...; bookings=12,13,14</c>.
        ///
        /// Bản cũ chỉ gọi <c>UpdateSpaBookingStatusAsync(payment.ReferenceId)</c>
        /// ⇒ chỉ booking đầu tiên được chuyển sang "Đã thanh toán", các booking
        /// còn lại treo ở "Chờ thanh toán PayOS" vĩnh viễn ⇒ khách thấy đã trả tiền
        /// nhưng lịch vẫn bị tính là chưa thanh toán ⇒ "đặt spa không ổn".
        /// </para>
        /// </summary>
        private async Task UpdateSpaBookingsStatusAsync(Payment payment, string status, CancellationToken ct)
        {
            var bookingIds = ParseBookingIdsFromNote(payment.Note, payment.ReferenceId);

            if (bookingIds.Count == 0)
            {
                return;
            }

            var bookings = await _db.Bookings
                .AsTracking()
                .Where(b => bookingIds.Contains(b.BookingId))
                .ToListAsync(ct);

            foreach (var booking in bookings)
            {
                booking.Status = status;
                booking.UpdatedAt = DateTime.Now;
            }

            await _db.SaveChangesAsync(ct);

            if (bookings.Count > 1)
            {
                _logger.Line($"[PayOS] Updated {bookings.Count} bookings to '{status}' for payment #{payment.PaymentId}.");
            }
        }

        /// <summary>
        /// Bóc danh sách booking id từ <c>Note</c> của payment.
        /// Định dạng: <c>"...; bookings=12,13,14"</c>.
        /// </summary>
        private static List<int> ParseBookingIdsFromNote(string? note, int? fallbackReferenceId)
        {
            var result = new List<int>();

            if (!string.IsNullOrWhiteSpace(note))
            {
                var marker = note.IndexOf("bookings=", StringComparison.OrdinalIgnoreCase);
                if (marker >= 0)
                {
                    var tail = note[(marker + "bookings=".Length)..];

                    // Danh sách id dùng dấu phẩy làm tách ("12,13,14") ⇒ phải cắt ở
                    // dấu kết thúc mảng (chấm phẩy / khoảng trắng), KHÔNG cắt ở dấu phẩy.
                    var end = tail.IndexOfAny(new[] { ';', ' ', '\r', '\n' });
                    var segment = end >= 0 ? tail[..end] : tail;

                    foreach (var part in segment.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (int.TryParse(part, out var id) && id > 0 && !result.Contains(id))
                        {
                            result.Add(id);
                        }
                    }
                }
            }

            if (result.Count == 0 && fallbackReferenceId.HasValue && fallbackReferenceId.Value > 0)
            {
                result.Add(fallbackReferenceId.Value);
            }

            return result;
        }

        private async Task UpdateBoardingBookingStatusAsync(int? bookingId, string status, CancellationToken ct)
        {
            if (!bookingId.HasValue)
            {
                return;
            }

            var booking = await _db.BoardingBookings
                .AsTracking()
                .FirstOrDefaultAsync(b => b.BookingId == bookingId.Value, ct);
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
                .AsTracking()
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

            // KHÔNG trừ tồn kho ở đây.
            //
            // Lý do: trừ kho chỉ thực hiện ở bước "nhân viên xác nhận đơn"
            // (OrdersController.StaffConfirm). Trước đây cả webhook lẫn
            // StaffConfirm đều trừ ⇒ đơn thanh toán PayOS bị trừ kho 2 LẦN.
            await _db.SaveChangesAsync(ct);
            _logger.Line($"[PayOS] Order #{orderId} updated. Status -> Chờ xác nhận, PaymentStatus -> Đã thanh toán.");
        }

        private async Task CancelOrderAsync(int? orderId, CancellationToken ct)
        {
            if (!orderId.HasValue)
            {
                return;
            }

            var order = await _db.Orders
                .AsTracking()
                .FirstOrDefaultAsync(o => o.OrderId == orderId.Value, ct);
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

        /// <summary>
        /// Gửi hoá đơn spa cho TẤT CẢ các booking thuộc cùng một payment.
        ///
        /// <para>
        /// Bản cũ chỉ gửi cho <c>payment.ReferenceId</c> (booking đầu tiên) ⇒
        /// khi khách đặt N pet (N booking) thì N-1 booking không có hoá đơn.
        /// </para>
        /// </summary>
        private async Task SendSpaInvoicesEmailAsync(Payment payment, string paymentStatusLabel, CancellationToken ct)
        {
            var bookingIds = ParseBookingIdsFromNote(payment.Note, payment.ReferenceId);

            if (bookingIds.Count == 0)
            {
                return;
            }

            foreach (var bookingId in bookingIds)
            {
                await SendSpaInvoiceEmailAsync(bookingId, paymentStatusLabel, ct);
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