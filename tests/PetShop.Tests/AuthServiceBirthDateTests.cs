using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PetShop.Controllers;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho logic ngày sinh khách hàng (Q22 — cột <c>Customer.birth_date</c>).
///
/// Hành vi cần bảo vệ:
///  - Ngày sinh hợp lệ được ghi xuống repository đúng customer id.
///  - Ngày sinh ở tương lai bị từ chối.
///  - Ngày sinh trước 1900-01-01 bị từ chối.
///  - null ⇒ xoá ngày sinh.
///  - customer id không hợp lệ không chạm repository.
/// </summary>
public class AuthServiceBirthDateTests
{
    private sealed class FakeAuthRepository : IAuthRepository
    {
        public int LastUpdatedCustomerId { get; private set; }
        public DateTime? LastBirthDate { get; private set; }
        public int UpdateBirthDateCallCount { get; private set; }

        public Task<Customer?> GetCustomerByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult<Customer?>(null);

        public Task<Staff?> GetStaffByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult<Staff?>(null);

        public Task<Doctor?> GetDoctorByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult<Doctor?>(null);

        public Task<Admin?> GetAdminByEmailOrUsernameAsync(string input, CancellationToken ct = default)
            => Task.FromResult<Admin?>(null);

        public Task<bool> EmailExistsInAnyRoleAsync(string email, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<Customer> CreateCustomerAsync(Customer customer, CancellationToken ct = default)
            => Task.FromResult(customer);

        public Task UpdateCustomerAsync(Customer customer, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> UpdateCustomerAvatarAsync(int customerId, string? avatarUrl, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<string?> GetCustomerAvatarAsync(int customerId, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<bool> UpdateCustomerBirthDateAsync(int customerId, DateTime? birthDate, CancellationToken ct = default)
        {
            UpdateBirthDateCallCount++;
            LastUpdatedCustomerId = customerId;
            LastBirthDate = birthDate;
            return Task.FromResult(true);
        }

        public Task<DateTime?> GetCustomerBirthDateAsync(int customerId, CancellationToken ct = default)
            => Task.FromResult(LastBirthDate);

        public Task<int> UpgradePasswordHashAsync(string email, string newPasswordHash, CancellationToken ct = default)
            => Task.FromResult(0);
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

    private sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }

    private static AuthService Build(FakeAuthRepository repo)
    {
        var config = new ConfigurationBuilder().Build();
        var factory = new NullHttpClientFactory();
        return new AuthService(repo, config, new FakeEmailService(), factory);
    }

    [Fact]
    public async Task UpdateBirthDate_ValidDate_IsPersisted()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        var ok = await service.UpdateBirthDateAsync(7, new DateTime(1995, 5, 20));

        Assert.True(ok);
        Assert.Equal(7, repo.LastUpdatedCustomerId);
        Assert.Equal(new DateTime(1995, 5, 20), repo.LastBirthDate);
    }

    [Fact]
    public async Task UpdateBirthDate_StripsTimeComponent()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        await service.UpdateBirthDateAsync(1, new DateTime(1995, 5, 20, 13, 45, 30));

        Assert.Equal(new DateTime(1995, 5, 20), repo.LastBirthDate);
    }

    [Fact]
    public async Task UpdateBirthDate_Null_ClearsBirthDate()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        var ok = await service.UpdateBirthDateAsync(1, null);

        Assert.True(ok);
        Assert.Null(repo.LastBirthDate);
    }

    [Fact]
    public async Task UpdateBirthDate_FutureDate_Throws()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateBirthDateAsync(1, DateTime.UtcNow.AddDays(1)));

        Assert.Equal(0, repo.UpdateBirthDateCallCount);
    }

    [Fact]
    public async Task UpdateBirthDate_BeforeMinimum_Throws()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateBirthDateAsync(1, AuthService.MinBirthDate.AddDays(-1)));

        Assert.Equal(0, repo.UpdateBirthDateCallCount);
    }

    [Fact]
    public async Task UpdateBirthDate_ExactlyMinimum_IsAccepted()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        var ok = await service.UpdateBirthDateAsync(1, AuthService.MinBirthDate);

        Assert.True(ok);
        Assert.Equal(AuthService.MinBirthDate, repo.LastBirthDate);
    }

    [Fact]
    public async Task UpdateBirthDate_InvalidCustomerId_DoesNotTouchRepository()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        var ok = await service.UpdateBirthDateAsync(0, new DateTime(1995, 5, 20));

        Assert.False(ok);
        Assert.Equal(0, repo.UpdateBirthDateCallCount);
    }

    [Fact]
    public async Task GetBirthDate_ReturnsStoredValue()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        await service.UpdateBirthDateAsync(3, new DateTime(1990, 1, 2));

        var birthDate = await service.GetBirthDateAsync(3);

        Assert.Equal(new DateTime(1990, 1, 2), birthDate);
    }

    /// <summary>
    /// Q20 — biên hợp lệ của <c>Products.price</c> phải khớp CHECK constraint
    /// <c>CK_Product_Price_Valid</c> trong DB: <c>[price] &gt;= 1000 AND [price] &lt;= 10000000</c>.
    /// </summary>
    [Fact]
    public void ProductPriceBounds_MatchDatabaseCheckConstraint()
    {
        Assert.Equal(1000m, ProductsTempController.MinProductPrice);
        Assert.Equal(10_000_000m, ProductsTempController.MaxProductPrice);
    }
}
