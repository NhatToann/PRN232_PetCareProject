using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho logic ảnh đại diện khách hàng (Q9 — cột <c>Customer.avatar_url</c>).
///
/// Hành vi cần bảo vệ:
///  - Chấp nhận data-URI ảnh (<c>data:image/...</c>) và URL http/https.
///  - Cho phép null/rỗng để xoá avatar.
///  - Từ chối payload nguy hiểm (<c>javascript:...</c>, <c>vbscript:</c>, ...) và chuỗi quá dài.
///  - Giá trị hợp lệ được ghi xuống repository đúng customer id.
/// </summary>
public class AuthServiceAvatarTests
{
    private sealed class FakeAuthRepository : IAuthRepository
    {
        public int LastUpdatedCustomerId { get; private set; }
        public string? LastAvatarUrl { get; private set; }
        public int UpdateAvatarCallCount { get; private set; }

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
        {
            UpdateAvatarCallCount++;
            LastUpdatedCustomerId = customerId;
            LastAvatarUrl = avatarUrl;
            return Task.FromResult(true);
        }

        public Task<string?> GetCustomerAvatarAsync(int customerId, CancellationToken ct = default)
            => Task.FromResult(LastAvatarUrl);

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

    private static (AuthService Service, FakeAuthRepository Repo) Build(FakeAuthRepository repo)
    {
        var config = new ConfigurationBuilder().Build();
        var factory = new NullHttpClientFactory();
        return (new AuthService(repo, config, new FakeEmailService(), factory), repo);
    }

    [Fact]
    public async Task UpdateAvatar_DataUri_IsPersisted()
    {
        var repo = new FakeAuthRepository();
        var (service, _) = Build(repo);

        var ok = await service.UpdateAvatarAsync(7, "data:image/png;base64,iVBORw0KGgo=");

        Assert.True(ok);
        Assert.Equal(7, repo.LastUpdatedCustomerId);
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", repo.LastAvatarUrl);
    }

    [Theory]
    [InlineData("https://example.com/avatar.png")]
    [InlineData("http://example.com/avatar.png")]
    public async Task UpdateAvatar_HttpUrl_IsPersisted(string avatarUrl)
    {
        var repo = new FakeAuthRepository();
        var (service, _) = Build(repo);

        var ok = await service.UpdateAvatarAsync(1, avatarUrl);

        Assert.True(ok);
        Assert.Equal(avatarUrl, repo.LastAvatarUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateAvatar_EmptyValue_ClearsAvatar(string? avatarUrl)
    {
        var repo = new FakeAuthRepository();
        var (service, _) = Build(repo);

        var ok = await service.UpdateAvatarAsync(1, avatarUrl);

        Assert.True(ok);
        Assert.Null(repo.LastAvatarUrl);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    public async Task UpdateAvatar_UnsupportedScheme_Throws(string avatarUrl)
    {
        var repo = new FakeAuthRepository();
        var (service, _) = Build(repo);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAvatarAsync(1, avatarUrl));

        Assert.Equal(0, repo.UpdateAvatarCallCount);
    }

    [Fact]
    public async Task UpdateAvatar_TooLong_Throws()
    {
        var repo = new FakeAuthRepository();
        var (service, _) = Build(repo);

        var tooLong = "https://example.com/" + new string('a', 600);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAvatarAsync(1, tooLong));

        Assert.Equal(0, repo.UpdateAvatarCallCount);
    }

    [Fact]
    public async Task UpdateAvatar_ExactlyMaxLength_IsAccepted()
    {
        var repo = new FakeAuthRepository();
        var (service, _) = Build(repo);

        var exact = "https://example.com/" + new string('a', 500 - "https://example.com/".Length);

        Assert.Equal(AuthService.MaxAvatarUrlLength, exact.Length);

        var ok = await service.UpdateAvatarAsync(1, exact);

        Assert.True(ok);
        Assert.Equal(exact, repo.LastAvatarUrl);
    }

    [Fact]
    public async Task UpdateAvatar_InvalidCustomerId_DoesNotTouchRepository()
    {
        var repo = new FakeAuthRepository();
        var (service, _) = Build(repo);

        var ok = await service.UpdateAvatarAsync(0, "data:image/png;base64,iVBORw0KGgo=");

        Assert.False(ok);
        Assert.Equal(0, repo.UpdateAvatarCallCount);
    }

    [Fact]
    public async Task GetAvatar_ReturnsStoredValue()
    {
        var repo = new FakeAuthRepository();
        var (service, _) = Build(repo);

        await service.UpdateAvatarAsync(3, "data:image/png;base64,AAA=");

        var avatar = await service.GetAvatarAsync(3);

        Assert.Equal("data:image/png;base64,AAA=", avatar);
    }
}
