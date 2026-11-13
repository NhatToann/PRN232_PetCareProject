using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Repositories
{
    /// <summary>
    /// Truy vấn phòng lưu trú + booking.
    ///
    /// <para>
    /// <b>VÌ SAO DÙNG IDbContextFactory:</b> trước đây repository nhận
    /// <c>ShopPetDatabaseContext</c> scoped. Trong Blazor Server, layout
    /// (TopBar/Footer — đọc <c>SystemSettings</c>) và page (đọc
    /// <c>BoardingRooms</c>) được render SONG SONG trong cùng circuit ⇒ hai
    /// truy vấn dùng chung một context ⇒ EF Core ném
    /// <c>"A second operation was started on this context instance before a
    /// previous operation completed"</c> ⇒ trang <c>/boarding</c> crash hoàn toàn
    /// (lỗi tải danh sách phòng).
    /// </para>
    ///
    /// <para>
    /// Nay mỗi thao tác tạo context RIÊNG qua <see cref="IDbContextFactory{TContext}"/>
    /// ⇒ an toàn khi truy vấn chạy song song, đúng pattern đã dùng ở
    /// <c>StaffReportsRepository</c>.
    /// </para>
    /// </summary>
    public sealed class BoardingRepository : IBoardingRepository
    {
        private readonly IDbContextFactory<ShopPetDatabaseContext> _factory;

        public BoardingRepository(IDbContextFactory<ShopPetDatabaseContext> factory)
        {
            _factory = factory;
        }

        public async Task<IReadOnlyList<BoardingRoom>> GetAllRoomsAsync(CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            return await context.BoardingRooms
                .AsNoTracking()
                .OrderBy(r => r.RoomId)
                .ToListAsync(ct);
        }

        public async Task<BoardingRoom?> GetRoomByIdAsync(int roomId, CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            return await context.BoardingRooms
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RoomId == roomId, ct);
        }

        public async Task<IReadOnlyList<BoardingBookingDto>> GetAllBookingsAsync(CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            var bookings = await context.BoardingBookings
                .AsNoTracking()
                .Include(b => b.Customer)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync(ct);

            return bookings.Select(b => new BoardingBookingDto(
                b.BookingId,
                b.CustomerId,
                b.RoomType,
                b.PricePerDay,
                b.BoardingDays,
                b.CheckInDate,
                b.CheckOutDate,
                b.CheckInTime,
                b.CheckOutTime,
                b.PetInfo,
                b.SpecialNotes,
                b.EmergencyPhone1,
                b.EmergencyPhone2,
                b.Status,
                b.CreatedAt,
                b.UpdatedAt,
                b.TotalPrice,
                new CustomerDto(
                    b.Customer.CustomerId,
                    b.Customer.Name,
                    b.Customer.Email,
                    b.Customer.Phone
                )
            )).ToList();
        }

        public async Task<bool> ConfirmCheckinAsync(int bookingId, CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            var booking = await context.BoardingBookings
                .AsTracking()
                .FirstOrDefaultAsync(b => b.BookingId == bookingId, ct);
            if (booking is null) return false;

            booking.Status = "Đang sử dụng";
            booking.UpdatedAt = DateTime.Now;
            await context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> ConfirmCheckoutAsync(int bookingId, CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            var booking = await context.BoardingBookings
                .AsTracking()
                .FirstOrDefaultAsync(b => b.BookingId == bookingId, ct);
            if (booking is null) return false;

            booking.Status = "Đã trả phòng";
            booking.UpdatedAt = DateTime.Now;
            await context.SaveChangesAsync(ct);
            return true;
        }

        public async Task<IReadOnlyList<BoardingAvailabilityDto>> GetAvailabilityAsync(CancellationToken ct = default)
        {
            await using var context = await _factory.CreateDbContextAsync(ct);
            var today = DateOnly.FromDateTime(DateTime.Today);

            var activeByType = await context.BoardingBookings
                .AsNoTracking()
                .Where(b => b.Status == "Đang sử dụng"
                    && b.CheckInDate <= today
                    && b.CheckOutDate >= today)
                .GroupBy(b => b.RoomType)
                .Select(g => new { RoomType = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var allRooms = await context.BoardingRooms
                .AsNoTracking()
                .ToListAsync(ct);

            return allRooms.Select(room =>
            {
                var active = activeByType.FirstOrDefault(x => x.RoomType == room.RoomType);
                return new BoardingAvailabilityDto(
                    room.RoomType,
                    room.Rooms,
                    active?.Count ?? 0,
                    room.Rooms - (active?.Count ?? 0)
                );
            }).ToList();
        }
    }
}
