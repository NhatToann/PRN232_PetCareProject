using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PetShop.Data;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Tests;

/// <summary>
/// <b>Integration test HTTP THẬT cho luồng quên mật khẩu.</b>
///
/// <para>
/// Vì sao cần test này (sau khi user báo "không thực hiện được luồng quên mật khẩu"
/// và "lỗi thông báo sai mật khẩu"):
/// </para>
/// <list type="number">
///   <item>
///     <description>
///     Test theo kiểu "đọc source code rồi assert chuỗi" <b>không bắt được</b>
///     lỗi thật: route client gọi lệch với controller, endpoint trả 409 do
///     validation metadata đặt sai chỗ, response sai shape... Tất cả chỉ lộ ra
///     khi gọi HTTP thật vào pipeline MVC thật.
///     </description>
///   </item>
///   <item>
///     <description>
///     Test này chạy qua <see cref="WebApplicationFactory{TEntryPoint}"/> ⇒
///     <b>model binding + DataAnnotations + [ApiController] + controller + service +
///     repository + DB thật</b> đều được thực thi. Nếu DTO có
///     <c>[property: Compare]</c> sai chỗ ⇒ ASP.NET Core ném
///     <c>InvalidOperationException</c> ⇒ test FAIL (đúng như thực tế).
///     </description>
///   </item>
/// </list>
///
/// <para>
/// <b>Hạn chế:</b> test dùng chung database <c>SHOP_PET_Database</c> (giữ nguyên
/// schema gốc theo yêu cầu dự án). Mỗi test tạo 1 khách hàng với email NGẪU NHIÊN
/// và XOÁ sau khi chạy ⇒ không đụng dữ liệu seed.
/// </para>
/// </summary>
public sealed class ForgotPasswordFlowTests : IClassFixture<ForgotPasswordFlowTests.ForgotPasswordAppFactory>
{
    private readonly ForgotPasswordAppFactory _factory;

    public ForgotPasswordFlowTests(ForgotPasswordAppFactory factory)
    {
        _factory = factory;
    }

    // -----------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------

    private static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    /// <summary>
    /// Tạo khách hàng tạm trong DB để test, trả về (email, customerId).
    /// </summary>
    private async Task<(string Email, int CustomerId)> CreateTempCustomerAsync()
    {
        var email = $"forgot-flow-{Guid.NewGuid():N}@petshop-test.local";

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopPetDatabaseContext>();

        var customer = new Customer
        {
            Name = "Test Forgot Flow",
            Email = email,
            Phone = "0900000000",
            AddressCustomer = "Test",
            Password = BCrypt.Net.BCrypt.HashPassword("OldPassword123"),
            Role = "user",
            Status = "active"
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return (email, customer.CustomerId);
    }

    private async Task<Customer?> LoadCustomerAsync(int customerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopPetDatabaseContext>();

        return await db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId);
    }

    private async Task DeleteTempCustomerAsync(int customerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopPetDatabaseContext>();

        var customer = await db.Customers
            .AsTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == customerId);

        if (customer is not null)
        {
            db.Customers.Remove(customer);
            await db.SaveChangesAsync();
        }
    }

    // -----------------------------------------------------------------
    // Test chính: chạy TRỌN luồng quên mật khẩu
    // -----------------------------------------------------------------

    [Fact]
    public async Task LuongQuenMatKhau_ChayTronVoiTuDauDenCuoi()
    {
        var (email, customerId) = await CreateTempCustomerAsync();

        try
        {
            var client = _factory.CreateClient();

            // ---- BƯỚC 1: gửi OTP -------------------------------------------------
            var sendResponse = await client.PostAsync(
                "/api/auth/send-email-otp",
                JsonBody(new { email }));

            Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

            var otp = _factory.GetCapturedOtp(email);
            Assert.False(string.IsNullOrWhiteSpace(otp),
                "Bước 1: email service phải nhận được OTP để sinh ra.");
            Assert.Equal(6, otp!.Length);

            // ---- BƯỚC 2: xác minh OTP ------------------------------------------
            var verifyResponse = await client.PostAsync(
                "/api/auth/verify-email-otp",
                JsonBody(new { email, otpCode = otp }));

            Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

            var verifyBody = await verifyResponse.Content.ReadAsStringAsync();
            using var verifyDoc = JsonDocument.Parse(verifyBody);

            Assert.True(verifyDoc.RootElement.TryGetProperty("message", out var tokenElement),
                $"Bước 2: response phải có 'message' (reset token). Body thật: {verifyBody}");

            var resetToken = tokenElement.GetString();
            Assert.False(string.IsNullOrWhiteSpace(resetToken),
                $"Bước 2: reset token không được rỗng. Body thật: {verifyBody}");

            // ---- BƯỚC 3: đặt mật khẩu mới ---------------------------------------
            const string newPassword = "NewPassword456";

            var resetResponse = await client.PostAsync(
                "/api/auth/reset-password",
                JsonBody(new
                {
                    email,
                    resetToken,
                    newPassword,
                    confirmPassword = newPassword
                }));

            var resetBody = await resetResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
            Assert.DoesNotContain("409", resetBody, StringComparison.Ordinal);

            // Mật khẩu trong DB phải là hash BCrypt của mật khẩu mới.
            var updated = await LoadCustomerAsync(customerId);
            Assert.NotNull(updated);
            Assert.NotNull(updated!.Password);
            Assert.StartsWith("$2", updated.Password!);
            Assert.True(BCrypt.Net.BCrypt.Verify(newPassword, updated.Password!),
                "Mật khẩu mới phải verify được bằng BCrypt.");
            Assert.False(BCrypt.Net.BCrypt.Verify("OldPassword123", updated.Password!),
                "Mật khẩu cũ không được còn hiệu lực.");

            // Reset token + OTP phải bị xoá sau khi dùng.
            Assert.Null(updated.ResetToken);
            Assert.Null(updated.OtpCode);

            // ---- BƯỚC 4: đăng nhập bằng mật khẩu mới ----------------------------
            var loginResponse = await client.PostAsync(
                "/api/auth/login",
                JsonBody(new { email, password = newPassword }));

            var loginBody = await loginResponse.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
            Assert.Contains("\"email\"", loginBody, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await DeleteTempCustomerAsync(customerId);
        }
    }

    // -----------------------------------------------------------------
    // Test lỗi: thông báo phải ĐÚNG NGHĨA (không báo nhầm "ít nhất 6 ký tự"
    // khi mật khẩu quá dài)
    // -----------------------------------------------------------------

    [Fact]
    public async Task ResetPassword_MatKhauQuaDai_PhaiBaoLoiVeDoDaiKhongPhaiVeDoDaiNhoNhat()
    {
        var (email, customerId) = await CreateTempCustomerAsync();

        try
        {
            var client = _factory.CreateClient();

            // Nhận OTP + reset token hợp lệ để request chỉ bị validation chặn.
            await client.PostAsync("/api/auth/send-email-otp", JsonBody(new { email }));

            var otp = _factory.GetCapturedOtp(email)!;

            var verifyResponse = await client.PostAsync(
                "/api/auth/verify-email-otp",
                JsonBody(new { email, otpCode = otp }));

            var verifyBody = await verifyResponse.Content.ReadAsStringAsync();
            using var verifyDoc = JsonDocument.Parse(verifyBody);
            var resetToken = verifyDoc.RootElement.GetProperty("message").GetString()!;

            // Mật khẩu 150 ký tự (> giới hạn 100) ⇒ phải báo lỗi về giới hạn trên.
            var tooLong = new string('x', 150);

            var response = await client.PostAsync(
                "/api/auth/reset-password",
                JsonBody(new
                {
                    email,
                    resetToken,
                    newPassword = tooLong,
                    confirmPassword = tooLong
                }));

            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            // Không được xuất hiện thông báo sai "ít nhất 6 ký tự".
            Assert.DoesNotContain("ít nhất 6 ký tự", body, StringComparison.OrdinalIgnoreCase);

            // Phải có thông báo về giới hạn 100 ký tự.
            Assert.Contains("100", body, StringComparison.Ordinal);
        }
        finally
        {
            await DeleteTempCustomerAsync(customerId);
        }
    }

    [Fact]
    public async Task ResetPassword_MatKhauQuaNgan_PhaiBaoLoiItNhat6KyTu()
    {
        var (email, customerId) = await CreateTempCustomerAsync();

        try
        {
            var client = _factory.CreateClient();

            await client.PostAsync("/api/auth/send-email-otp", JsonBody(new { email }));

            var otp = _factory.GetCapturedOtp(email)!;

            var verifyResponse = await client.PostAsync(
                "/api/auth/verify-email-otp",
                JsonBody(new { email, otpCode = otp }));

            var verifyBody = await verifyResponse.Content.ReadAsStringAsync();
            using var verifyDoc = JsonDocument.Parse(verifyBody);
            var resetToken = verifyDoc.RootElement.GetProperty("message").GetString()!;

            var response = await client.PostAsync(
                "/api/auth/reset-password",
                JsonBody(new
                {
                    email,
                    resetToken,
                    newPassword = "abc",
                    confirmPassword = "abc"
                }));

            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("ít nhất 6 ký tự", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await DeleteTempCustomerAsync(customerId);
        }
    }

    [Fact]
    public async Task ResetPassword_XacNhanKhongKhop_PhaiTra400KemThongDiepViet()
    {
        var (email, customerId) = await CreateTempCustomerAsync();

        try
        {
            var client = _factory.CreateClient();

            await client.PostAsync("/api/auth/send-email-otp", JsonBody(new { email }));

            var otp = _factory.GetCapturedOtp(email)!;

            var verifyResponse = await client.PostAsync(
                "/api/auth/verify-email-otp",
                JsonBody(new { email, otpCode = otp }));

            var verifyBody = await verifyResponse.Content.ReadAsStringAsync();
            using var verifyDoc = JsonDocument.Parse(verifyBody);
            var resetToken = verifyDoc.RootElement.GetProperty("message").GetString()!;

            var response = await client.PostAsync(
                "/api/auth/reset-password",
                JsonBody(new
                {
                    email,
                    resetToken,
                    newPassword = "Abcdef12",
                    confirmPassword = "Different12"
                }));

            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("không khớp", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await DeleteTempCustomerAsync(customerId);
        }
    }

    [Fact]
    public async Task SendEmailOtp_EmailKhongTonTai_PhaiTra404KemThongDiepViet()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            "/api/auth/send-email-otp",
            JsonBody(new { email = $"khong-ton-tai-{Guid.NewGuid():N}@petshop-test.local" }));

        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("chưa đăng ký", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyEmailOtp_OtpSai_PhaiTra401()
    {
        var (email, customerId) = await CreateTempCustomerAsync();

        try
        {
            var client = _factory.CreateClient();

            var response = await client.PostAsync(
                "/api/auth/verify-email-otp",
                JsonBody(new { email, otpCode = "000000" }));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await DeleteTempCustomerAsync(customerId);
        }
    }

    /// <summary>
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> với:
    ///  - <see cref="IEmailService"/> thay bằng fake để <b>bắt được OTP</b> mà không
    ///    cần gửi mail thật (không phụ thuộc SMTP / không spam inbox).
    ///  - Không đụng Kestrel: TestServer xử lý HTTP trong bộ nhớ.
    /// </summary>
    public sealed class ForgotPasswordAppFactory : WebApplicationFactory<Program>
    {
        /// <summary>
        /// OTP đã gửi, ghi theo email để test đọc lại được.
        ///
        /// <para>
        /// Dùng dictionary tĩnh (không inject factory) vì fake email service được
        /// đăng ký vào DI container — factory không phải service nên không inject được.
        /// </para>
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> SentOtps = new();

        /// <summary>Đọc OTP đã gửi cho 1 email (null nếu chưa gửi).</summary>
        public string? GetCapturedOtp(string email)
            => SentOtps.TryGetValue(email.Trim().ToLowerInvariant(), out var otp) ? otp : null;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailService>();
                services.AddScoped<IEmailService, CapturingEmailService>();
            });
        }

        /// <summary>
        /// Email service giả — ghi lại OTP thay vì gửi mail.
        /// </summary>
        private sealed class CapturingEmailService : IEmailService
        {
            public Task SendOtpEmailAsync(string toEmail, string otp, CancellationToken ct = default)
            {
                SentOtps[toEmail.Trim().ToLowerInvariant()] = otp;
                return Task.CompletedTask;
            }

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
    }
}
