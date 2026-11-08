using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho logic xác thực mật khẩu của <see cref="Services.AuthService"/>.
///
/// Hành vi cần bảo vệ:
///  - Mật khẩu lưu dạng hash BCrypt ($2...) được kiểm tra bằng BCrypt.
///  - Mật khẩu lưu dạng plaintext (dữ liệu seed cũ) vẫn đăng nhập được 1 lần,
///    rồi được nâng cấp lại thành hash BCrypt (migrate-on-login).
///  - Mật khẩu sai không bao giờ được chấp nhận, và không kích hoạt migrate.
/// </summary>
public class AuthServiceTests
{
    private sealed class FakeAuthRepository : IAuthRepository
    {
        public Customer? Customer { get; set; }
        public Staff? Staff { get; set; }
        public Doctor? Doctor { get; set; }
        public Admin? Admin { get; set; }

        public int UpgradeCallCount { get; private set; }
        public string? LastUpgradedHash { get; private set; }
        public string? LastAvatarUrl { get; private set; }

        public Task<Customer?> GetCustomerByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult(Customer);

        public Task<Staff?> GetStaffByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult(Staff);

        public Task<Doctor?> GetDoctorByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult(Doctor);

        public Task<Admin?> GetAdminByEmailOrUsernameAsync(string input, CancellationToken ct = default)
            => Task.FromResult(Admin);

        public Task<bool> EmailExistsInAnyRoleAsync(string email, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<Customer> CreateCustomerAsync(Customer customer, CancellationToken ct = default)
            => Task.FromResult(customer);

        public Task UpdateCustomerAsync(Customer customer, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> UpdateCustomerAvatarAsync(int customerId, string? avatarUrl, CancellationToken ct = default)
        {
            LastAvatarUrl = avatarUrl;
            return Task.FromResult(true);
        }

        public Task<string?> GetCustomerAvatarAsync(int customerId, CancellationToken ct = default)
            => Task.FromResult(LastAvatarUrl);

        public Task<int> UpgradePasswordHashAsync(string email, string newPasswordHash, CancellationToken ct = default)
        {
            UpgradeCallCount++;
            LastUpgradedHash = newPasswordHash;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeEmailService : IEmailService
    {
        public Task SendOtpEmailAsync(string toEmail, string otp, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendGoogleFirstLoginEmailAsync(string toEmail, string? name, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendOrderInvoiceEmailAsync(
            string toEmail, string? customerName, string orderCode,
            IEnumerable<(string ItemName, int Quantity, decimal UnitPrice)> items,
            decimal totalAmount, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendSpaInvoiceEmailAsync(
            string toEmail, string? customerName, string bookingCode,
            IEnumerable<(string ItemName, int Quantity, decimal UnitPrice)> items,
            decimal totalAmount, string paymentStatusLabel, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendBoardingInvoiceEmailAsync(
            string toEmail, string? customerName, string bookingCode, string roomDisplayName,
            DateOnly checkIn, DateOnly checkOut, int days, int pets, string? notes,
            decimal pricePerDay, decimal totalPrice,
            string paymentMethodLabel, string paymentStatusLabel, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static (AuthService Service, FakeAuthRepository Repo) Build(FakeAuthRepository repo)
    {
        var config = new ConfigurationBuilder().Build();
        var factory = new NullHttpClientFactory();
        return (new AuthService(repo, config, new FakeEmailService(), factory), repo);
    }

    private sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }

    [Fact]
    public async Task Login_WithBcryptHash_Succeeds_AndDoesNotMigrate()
    {
        var password = "correct-horse";
        var repo = new FakeAuthRepository
        {
            Customer = new Customer
            {
                CustomerId = 5,
                Name = "Test User",
                Email = "test@example.com",
                Password = BCrypt.Net.BCrypt.HashPassword(password),
                Role = "user",
                Status = "active"
            }
        };
        var (service, _) = Build(repo);

        var result = await service.LoginAsync(new LoginRequestDto("test@example.com", password));

        Assert.Equal("test@example.com", result.Email);
        Assert.Equal(5, result.UserId);
        Assert.Equal(0, repo.UpgradeCallCount);
    }

    [Fact]
    public async Task Login_WithPlaintextSeedPassword_Succeeds_AndUpgradesToBcrypt()
    {
        var repo = new FakeAuthRepository
        {
            Customer = new Customer
            {
                CustomerId = 9,
                Name = "Seed User",
                Email = "seed@example.com",
                Password = "staff123",
                Role = "user",
                Status = "active"
            }
        };
        var (service, _) = Build(repo);

        var result = await service.LoginAsync(new LoginRequestDto("seed@example.com", "staff123"));

        Assert.Equal(9, result.UserId);
        Assert.Equal(1, repo.UpgradeCallCount);
        Assert.NotNull(repo.LastUpgradedHash);
        Assert.StartsWith("$2", repo.LastUpgradedHash);
        // Hash mới phải verify được với mật khẩu gốc.
        Assert.True(BCrypt.Net.BCrypt.Verify("staff123", repo.LastUpgradedHash!));
    }

    [Fact]
    public async Task Login_WithWrongPlaintextPassword_Fails_AndDoesNotMigrate()
    {
        var repo = new FakeAuthRepository
        {
            Customer = new Customer
            {
                CustomerId = 9,
                Name = "Seed User",
                Email = "seed@example.com",
                Password = "staff123",
                Role = "user",
                Status = "active"
            }
        };
        var (service, _) = Build(repo);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.LoginAsync(new LoginRequestDto("seed@example.com", "wrong")));

        Assert.Equal(0, repo.UpgradeCallCount);
    }

    [Fact]
    public async Task Login_WithWrongBcryptPassword_Fails()
    {
        var repo = new FakeAuthRepository
        {
            Customer = new Customer
            {
                CustomerId = 5,
                Name = "Test User",
                Email = "test@example.com",
                Password = BCrypt.Net.BCrypt.HashPassword("correct-horse"),
                Role = "user",
                Status = "active"
            }
        };
        var (service, _) = Build(repo);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.LoginAsync(new LoginRequestDto("test@example.com", "bat-battery-staple")));
    }

    [Fact]
    public async Task Login_WithPlaintextPassword_SeededOnStaffAccount_Succeeds()
    {
        var repo = new FakeAuthRepository
        {
            Staff = new Staff
            {
                StaffId = 3,
                Name = "Staff A",
                Email = "staff@petshop.com",
                Password = "staff123",
                Position = "nhân viên"
            }
        };
        var (service, _) = Build(repo);

        var result = await service.LoginAsync(new LoginRequestDto("staff@petshop.com", "staff123"));

        Assert.Equal("staff", result.Role);
        Assert.Equal(3, result.UserId);
        Assert.Equal(1, repo.UpgradeCallCount);
    }

    [Fact]
    public async Task Login_WithEmptyPassword_Throws()
    {
        var repo = new FakeAuthRepository();
        var (service, _) = Build(repo);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.LoginAsync(new LoginRequestDto("test@example.com", "")));
    }

    [Fact]
    public async Task Login_UnknownAccount_Throws()
    {
        var repo = new FakeAuthRepository();
        var (service, _) = Build(repo);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.LoginAsync(new LoginRequestDto("nobody@example.com", "whatever")));
    }

    [Fact]
    public async Task Login_InactiveAccount_Throws()
    {
        var repo = new FakeAuthRepository
        {
            Customer = new Customer
            {
                CustomerId = 5,
                Name = "Test User",
                Email = "test@example.com",
                Password = BCrypt.Net.BCrypt.HashPassword("correct-horse"),
                Role = "user",
                Status = "pending"
            }
        };
        var (service, _) = Build(repo);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.LoginAsync(new LoginRequestDto("test@example.com", "correct-horse")));
    }
}
