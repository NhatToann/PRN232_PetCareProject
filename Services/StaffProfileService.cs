using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services
{
    /// <summary>
    /// Đọc/ghi hồ sơ người dùng đang đăng nhập.
    ///
    /// <para>
    /// Lưu ý về định danh: hệ thống đăng nhập trả về UserId theo bảng tương ứng với vai trò
    /// (Customer cho user, Staff cho staff, Admin cho admin). Vì vậy ta định vị bản ghi
    /// bằng email trước (email là duy nhất trong toàn hệ thống), rồi mới dùng id làm điều kiện
    /// kiểm tra bổ sung. Nhờ vậy không bao giờ ghi nhầm sang người khác chỉ vì id trùng.
    /// </para>
    ///
    /// <para>
    /// Dùng <see cref="IDbContextFactory{TContext}"/> thay vì <c>DbContext</c> scoped
    /// để truy vấn hồ sơ (chạy trong layout) không xung đột với truy vấn dashboard
    /// (chạy trong page) khi Blazor Server render đồng thời.
    /// </para>
    /// </summary>
    public sealed class StaffProfileService : IStaffProfileService
    {
        private readonly IDbContextFactory<ShopPetDatabaseContext> _factory;

        public StaffProfileService(IDbContextFactory<ShopPetDatabaseContext> factory)
        {
            _factory = factory;
        }

        public async Task<StaffProfileDto?> GetProfileAsync(int userId, string? email, CancellationToken ct = default)
        {
            var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();

            await using var db = await _factory.CreateDbContextAsync(ct);

            // Ưu tiên bảng Staff — người dùng đang đăng nhập ở khu vực nhân viên.
            if (!string.IsNullOrWhiteSpace(normalizedEmail))
            {
                var staff = await db.Staff
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        s => (s.Email ?? string.Empty).Trim().ToLower() == normalizedEmail,
                        ct);

                if (staff is not null)
                {
                    return new StaffProfileDto
                    {
                        Source = "staff",
                        Id = staff.StaffId,
                        Name = staff.Name ?? string.Empty,
                        Phone = staff.Phone ?? string.Empty,
                        Email = staff.Email ?? string.Empty,
                        Position = staff.Position ?? string.Empty,
                        Role = string.Equals(staff.Position, "admin", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(staff.Position, "quản lý", StringComparison.OrdinalIgnoreCase)
                            ? "admin"
                            : "staff"
                    };
                }
            }

            // Không có email khớp ở bảng Staff → tìm trong bảng Customer.
            var customer = await db.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    c => !string.IsNullOrWhiteSpace(normalizedEmail)
                         && (c.Email ?? string.Empty).Trim().ToLower() == normalizedEmail,
                    ct);

            if (customer is null && userId > 0)
            {
                // Dự phòng: không có email (ví dụ đăng nhập bằng Google mà DB chưa đồng bộ) → dùng id.
                customer = await db.Customers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.CustomerId == userId, ct);
            }

            if (customer is null)
            {
                return null;
            }

            return new StaffProfileDto
            {
                Source = "customer",
                Id = customer.CustomerId,
                Name = customer.Name ?? string.Empty,
                Phone = customer.Phone ?? string.Empty,
                Email = customer.Email ?? string.Empty,
                Role = customer.Role ?? string.Empty
            };
        }

        public async Task<int> ResolveStaffIdAsync(int userId, string? email, CancellationToken ct = default)
        {
            var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(normalizedEmail))
            {
                await using var db = await _factory.CreateDbContextAsync(ct);
                var staffId = await db.Staff
                    .AsNoTracking()
                    .Where(s => (s.Email ?? string.Empty).Trim().ToLower() == normalizedEmail)
                    .Select(s => (int?)s.StaffId)
                    .FirstOrDefaultAsync(ct);

                if (staffId.HasValue)
                {
                    return staffId.Value;
                }
            }

            // Không có trong bảng Staff → dùng userId làm staff_id.
            // Điều này giữ nguyên hành vi cũ cho các tài khoản staff lưu trong bảng Customer
            // (lịch/ca được seed theo đúng id đó).
            return userId > 0 ? userId : 0;
        }

        public async Task<bool> UpdateProfileAsync(StaffProfileDto profile, CancellationToken ct = default)
        {
            if (profile.Id <= 0)
            {
                return false;
            }

            await using var db = await _factory.CreateDbContextAsync(ct);

            if (string.Equals(profile.Source, "staff", StringComparison.OrdinalIgnoreCase))
            {
                var staff = await db.Staff
                    .AsTracking()
                    .FirstOrDefaultAsync(s => s.StaffId == profile.Id, ct);
                if (staff is null)
                {
                    return false;
                }

                staff.Name = profile.Name;
                staff.Phone = profile.Phone;
                staff.Email = profile.Email;
                await db.SaveChangesAsync(ct);
                return true;
            }

            var customer = await db.Customers
                .AsTracking()
                .FirstOrDefaultAsync(c => c.CustomerId == profile.Id, ct);
            if (customer is null)
            {
                return false;
            }

            customer.Name = profile.Name;
            customer.Phone = profile.Phone;
            customer.Email = profile.Email;
            await db.SaveChangesAsync(ct);
            return true;
        }
    }
}
