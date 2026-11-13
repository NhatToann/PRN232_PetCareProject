using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop.Tests;

/// <summary>
/// Unit test cho logic địa chỉ giao hàng của khách hàng (cột <c>Customer.address_Customer</c>).
///
/// Hành vi cần bảo vệ:
///  - Địa chỉ hợp lệ được ghi xuống repository đúng customer id.
///  - Địa chỉ được trim khoảng trắng thừa.
///  - null / rỗng / khoảng trắng ⇒ xoá địa chỉ.
///  - Địa chỉ dài hơn 255 ký tự (giới hạn cột NVARCHAR(255)) bị từ chối.
///  - customer id không hợp lệ không chạm repository.
/// </summary>
public class AuthServiceAddressTests
{
    private sealed class FakeAuthRepository : IAuthRepository
    {
        public int LastUpdatedCustomerId { get; private set; }
        public string? LastAddress { get; private set; }
        public int UpdateAddressCallCount { get; private set; }
        public string? LastPhone { get; private set; }
        public int UpdatePhoneCallCount { get; private set; }

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
            => Task.FromResult(true);

        public Task<DateTime?> GetCustomerBirthDateAsync(int customerId, CancellationToken ct = default)
            => Task.FromResult<DateTime?>(null);

        public Task<bool> UpdateCustomerAddressAsync(int customerId, string? address, CancellationToken ct = default)
        {
            UpdateAddressCallCount++;
            LastUpdatedCustomerId = customerId;
            LastAddress = address;
            return Task.FromResult(true);
        }

        public Task<string?> GetCustomerAddressAsync(int customerId, CancellationToken ct = default)
            => Task.FromResult(LastAddress);

        public Task<bool> UpdateCustomerPhoneAsync(int customerId, string? phone, CancellationToken ct = default)
        {
            UpdatePhoneCallCount++;
            LastPhone = phone;
            return Task.FromResult(true);
        }

        public Task<string?> GetCustomerPhoneAsync(int customerId, CancellationToken ct = default)
            => Task.FromResult(LastPhone);

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
        return new AuthService(repo, config, new FakeEmailService(), factory, new NullLogger<AuthService>());
    }

    [Fact]
    public async Task UpdateAddress_ValidAddress_IsPersisted()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        var ok = await service.UpdateAddressAsync(7, "123 Nguyễn Văn A, Phường Hải Châu I, Quận Hải Châu, Đà Nẵng");

        Assert.True(ok);
        Assert.Equal(7, repo.LastUpdatedCustomerId);
        Assert.Equal("123 Nguyễn Văn A, Phường Hải Châu I, Quận Hải Châu, Đà Nẵng", repo.LastAddress);
    }

    [Fact]
    public async Task UpdateAddress_TrimsSurroundingWhitespace()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        await service.UpdateAddressAsync(1, "  Đà Nẵng  ");

        Assert.Equal("Đà Nẵng", repo.LastAddress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateAddress_EmptyValue_ClearsAddress(string? value)
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        var ok = await service.UpdateAddressAsync(1, value);

        Assert.True(ok);
        Assert.Null(repo.LastAddress);
    }

    [Fact]
    public async Task UpdateAddress_TooLong_Throws()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        var tooLong = new string('a', AuthService.MaxAddressLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAddressAsync(1, tooLong));

        Assert.Equal(0, repo.UpdateAddressCallCount);
    }

    [Fact]
    public async Task UpdateAddress_ExactlyMaxLength_IsAccepted()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        var exact = new string('a', AuthService.MaxAddressLength);

        var ok = await service.UpdateAddressAsync(1, exact);

        Assert.True(ok);
        Assert.Equal(exact, repo.LastAddress);
    }

    [Fact]
    public async Task UpdateAddress_InvalidCustomerId_DoesNotTouchRepository()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        var ok = await service.UpdateAddressAsync(0, "Đà Nẵng");

        Assert.False(ok);
        Assert.Equal(0, repo.UpdateAddressCallCount);
    }

    [Fact]
    public async Task GetAddress_ReturnsStoredValue()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        await service.UpdateAddressAsync(3, "Số 1, Phường Mỹ An, Quận Ngũ Hành Sơn, Đà Nẵng");

        var address = await service.GetAddressAsync(3);

        Assert.Equal("Số 1, Phường Mỹ An, Quận Ngũ Hành Sơn, Đà Nẵng", address);
    }

    // -------------------------------------------------------------------
    // Địa chỉ + số điện thoại (cột Customer.phone)
    //
    // VÌ SAO CẦN: cột address_Customer không chứa số điện thoại ⇒ nếu chỉ lưu
    // chuỗi địa chỉ thì khi đọc lại để điền sẵn form thanh toán, trường "Số điện
    // thoại" trống ⇒ form không hợp lệ ⇒ khách phải nhập lại từ đầu.
    // -------------------------------------------------------------------

    [Fact]
    public async Task UpdateAddressWithPhone_SavesBothAddressAndPhone()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        var ok = await service.UpdateAddressWithPhoneAsync(7, "123 Nguyễn Huệ, Đà Nẵng", "0912345678");

        Assert.True(ok);
        Assert.Equal("123 Nguyễn Huệ, Đà Nẵng", repo.LastAddress);
        Assert.Equal("0912345678", repo.LastPhone);
        Assert.Equal(1, repo.UpdatePhoneCallCount);
    }

    [Fact]
    public async Task UpdateAddressWithPhone_TrimsPhone()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        await service.UpdateAddressWithPhoneAsync(1, "Đà Nẵng", "  0912345678  ");

        Assert.Equal("0912345678", repo.LastPhone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateAddressWithPhone_EmptyPhone_KeepsExistingPhone(string? phone)
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        var ok = await service.UpdateAddressWithPhoneAsync(1, "Đà Nẵng", phone);

        Assert.True(ok);
        // Phone rỗng ⇒ KHÔNG ghi đè số điện thoại đang có.
        Assert.Equal(0, repo.UpdatePhoneCallCount);
        Assert.Null(repo.LastPhone);
    }

    [Fact]
    public async Task UpdateAddressWithPhone_InvalidCustomerId_DoesNotTouchRepository()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        var ok = await service.UpdateAddressWithPhoneAsync(0, "Đà Nẵng", "0912345678");

        Assert.False(ok);
        Assert.Equal(0, repo.UpdateAddressCallCount);
        Assert.Equal(0, repo.UpdatePhoneCallCount);
    }

    [Fact]
    public async Task UpdateAddressWithPhone_TooLongAddress_Throws()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        var tooLong = new string('a', AuthService.MaxAddressLength + 1);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateAddressWithPhoneAsync(1, tooLong, "0912345678"));

        Assert.Equal(0, repo.UpdateAddressCallCount);
    }

    [Fact]
    public async Task GetPhone_ReturnsStoredValue()
    {
        var repo = new FakeAuthRepository();
        var service = Build(repo);

        await service.UpdateAddressWithPhoneAsync(3, "Đà Nẵng", "0901234567");

        var phone = await service.GetPhoneAsync(3);

        Assert.Equal("0901234567", phone);
    }
}
