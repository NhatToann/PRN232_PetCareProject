using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;
using System.ComponentModel.DataAnnotations;

namespace PetShop.Tests;

/// <summary>
/// Test contract cho luồng QUÊN MẬT KHẨU.
///
/// <para>
/// Bối cảnh — lỗi thật đã xảy ra:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///     <c>AuthClient</c> gọi <c>api/auth/send-otp</c> và <c>api/auth/verify-otp</c>
///     nhưng <c>AuthController</c> chỉ có <c>send-email-otp</c> / <c>verify-email-otp</c>
///     ⇒ cả 2 bước đầu của luồng quên mật khẩu trả <b>404</b> ⇒ user không
///     thể hoàn tất luồng.
///     </description>
///   </item>
///   <item>
///     <description>
///     <c>[property: Compare("NewPassword")]</c> trên <c>ResetPasswordRequestDto</c>
///     (record có primary constructor) ⇒ ASP.NET Core ném
///     <c>InvalidOperationException</c> ⇒ endpoint <c>reset-password</c> trả
///     <b>409</b> với body rác ⇒ bước 3 (đặt mật khẩu mới) không bao giờ chạy.
///     </description>
///   </item>
///   <item>
///     <description>
///     <c>[StringLength(100, MinimumLength = 6)]</c> gộp cả 2 ràng buộc vào 1
///     message ⇒ mật khẩu <b>dài quá 100</b> ký tự cũng báo nhầm
///     "phải có ít nhất 6 ký tự" ⇒ thông báo lỗi sai, gây khó hiểu.
///     </description>
///   </item>
/// </list>
///
/// <para>
/// Các test dưới đây kiểm tra <b>hành vi thật</b> (route + validation + exception
/// của ASP.NET Core), không chỉ so khớp chuỗi trong source.
/// </para>
/// </summary>
public sealed class AuthRouteContractTests
{
    // -----------------------------------------------------------------
    // 1) Route phải tồn tại thật trong AuthController
    // -----------------------------------------------------------------

    /// <summary>
    /// Trích xuất mọi route HTTP của <c>AuthController</c> (prefix + template).
    /// </summary>
    private static HashSet<string> GetAuthControllerRoutes()
    {
        var controllerType = typeof(PetShop.Controllers.AuthController);

        var prefix = controllerType
            .GetCustomAttributes<RouteAttribute>()
            .Select(a => a.Template)
            .FirstOrDefault() ?? string.Empty;

        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var method in controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var attr in method.GetCustomAttributes<HttpMethodAttribute>())
            {
                var template = attr.Template ?? string.Empty;
                routes.Add($"/{prefix}/{template}".Replace("//", "/").TrimEnd('/'));
            }
        }

        return routes;
    }

    /// <summary>
    /// Đọc các string literal bắt đầu bằng <c>"api/"</c> từ IL của các method
    /// trong <c>AuthClient</c>. Dùng <c>Module.ResolveString</c> ⇒ không cần
    /// package decompiler.
    /// </summary>
    private static List<string> GetAuthClientApiPaths()
    {
        var clientType = typeof(PetShop.Services.AuthClient);
        var paths = new List<string>();

        foreach (var method in clientType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            paths.AddRange(ExtractApiPathsFromMethod(method));
        }

        return paths;
    }

    private static IEnumerable<string> ExtractApiPathsFromMethod(MethodInfo method)
    {
        var body = method.GetMethodBody();
        if (body is null)
        {
            yield break;
        }

        var il = body.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        for (var i = 0; i < il.Length - 4; i++)
        {
            if (il[i] != 0x72) // ldstr
            {
                continue;
            }

            var token = BitConverter.ToInt32(il, i + 1);

            string? value;
            try
            {
                value = method.Module.ResolveString(token);
            }
            catch
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(value)
                && value.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            {
                yield return value;
            }
        }
    }

    [Fact]
    public void AuthClient_MoiEndpointTrongLuongQuenMatKhau_DuocControllerHoTro()
    {
        var routes = GetAuthControllerRoutes();
        var clientPaths = GetAuthClientApiPaths();

        Assert.NotEmpty(clientPaths);

        // Chuẩn hoá: "api/auth/send-otp" -> "/api/auth/send-otp"
        var normalized = clientPaths
            .Select(p => (p.StartsWith('/') ? p : "/" + p).TrimEnd('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = normalized
            .Where(p => !routes.Contains(p))
            .ToList();

        Assert.True(missing.Count == 0,
            "AuthClient gọi endpoint KHÔNG tồn tại trong AuthController: " +
            string.Join(", ", missing) +
            " | Routes controller: " + string.Join(", ", routes.OrderBy(r => r)));
    }

    [Fact]
    public void AuthController_PhaiCoCacRouteQuenMatKhau()
    {
        var routes = GetAuthControllerRoutes();

        Assert.Contains("/api/auth/send-email-otp", routes);
        Assert.Contains("/api/auth/verify-email-otp", routes);
        Assert.Contains("/api/auth/reset-password", routes);
    }

    [Fact]
    public void AuthClient_KhongDuocGoiRouteCuTenSai()
    {
        // Regression cụ thể của bug đã gặp.
        var clientPaths = GetAuthClientApiPaths();

        Assert.DoesNotContain("api/auth/send-otp", clientPaths);
        Assert.DoesNotContain("api/auth/verify-otp", clientPaths);
    }

    // -----------------------------------------------------------------
    // 2) DTO không được có metadata validation trên primary constructor
    //    (gây InvalidOperationException lúc runtime ⇒ 409)
    // -----------------------------------------------------------------

    [Fact]
    public void ResetPasswordRequestDto_KhongCoCompareTrenPrimaryConstructor()
    {
        // [property: Compare(...)] trên record primary constructor bị ASP.NET Core
        // ném InvalidOperationException lúc khởi tạo model ⇒ endpoint 409.
        var property = typeof(ResetPasswordRequestDto).GetProperty(nameof(ResetPasswordRequestDto.ConfirmPassword));

        Assert.NotNull(property);

        var compareAttrs = property!
            .GetCustomAttributes<CompareAttribute>()
            .ToList();

        Assert.True(compareAttrs.Count == 0,
            "ResetPasswordRequestDto.ConfirmPassword KHÔNG được dùng [Compare] " +
            "trên primary constructor của record (gây 409). " +
            "So khớp NewPassword/ConfirmPassword đã được kiểm tra trong AuthService.");
    }

    [Fact]
    public void RegisterRequestDto_KhongCoCompareTrenPrimaryConstructor()
    {
        var property = typeof(RegisterRequestDto).GetProperty(nameof(RegisterRequestDto.Password));

        Assert.NotNull(property);
        Assert.Empty(property!.GetCustomAttributes<CompareAttribute>());
    }

    /// <summary>
    /// Regression: ASP.NET Core ném InvalidOperationException khi record có
    /// primary constructor mang validation metadata gắn trên <b>property</b>
    /// (vd <c>[property: Compare(...)]</c>) mà constructor parameter tương ứng
    /// không có ⇒ endpoint trả 409 với body rác thay vì xử lý yêu cầu.
    /// </summary>
    [Fact]
    public void ValidationAttribute_TrenProperty_KhongDuocPhepNeuRecordCoPrimaryConstructor()
    {
        var offenders = new List<string>();

        foreach (var recordType in new[]
                 {
                     typeof(RegisterRequestDto),
                     typeof(LoginRequestDto),
                     typeof(SendEmailOtpRequestDto),
                     typeof(VerifyEmailOtpRequestDto),
                     typeof(ResetPasswordRequestDto),
                 })
        {
            var ctor = recordType.GetConstructors().FirstOrDefault();
            Assert.NotNull(ctor);

            var parameterNames = ctor!.GetParameters()
                .Select(p => p.Name!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var property in recordType.GetProperties())
            {
                if (!parameterNames.Contains(property.Name))
                {
                    // Property không phải tham số constructor ⇒ không sao.
                    continue;
                }

                var propertyAttrs = property.GetCustomAttributes<ValidationAttribute>().ToList();
                if (propertyAttrs.Count == 0)
                {
                    continue;
                }

                var parameter = ctor.GetParameters()
                    .First(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));

                var parameterAttrs = parameter.GetCustomAttributes<ValidationAttribute>().ToList();

                if (parameterAttrs.Count == 0)
                {
                    offenders.Add(
                        $"{recordType.Name}.{property.Name} có " +
                        $"{propertyAttrs.Count} ValidationAttribute trên property nhưng 0 trên constructor parameter " +
                        "⇒ ASP.NET Core sẽ ném InvalidOperationException (409).");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "ValidationAttribute đặt sai chỗ (gây 409 cho endpoint):" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // -----------------------------------------------------------------
    // 3) Message validation phải phân biệt "quá ngắn" và "quá dài"
    // -----------------------------------------------------------------

    /// <summary>
    /// Lấy <see cref="StringLengthAttribute"/> gắn trên <b>constructor parameter</b>
    /// của record (đúng nơi ASP.NET Core đọc validation metadata cho record
    /// có primary constructor).
    /// </summary>
    private static StringLengthAttribute? GetStringLengthOnConstructorParameter(
        Type recordType,
        string parameterName)
    {
        var ctor = recordType.GetConstructors().FirstOrDefault();
        Assert.NotNull(ctor);

        var parameter = ctor!.GetParameters()
            .FirstOrDefault(p => string.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(parameter);

        return parameter!.GetCustomAttributes<StringLengthAttribute>().FirstOrDefault();
    }

    /// <summary>
    /// Lấy <see cref="MinLengthAttribute"/> gắn trên constructor parameter.
    /// </summary>
    private static MinLengthAttribute? GetMinLengthOnConstructorParameter(
        Type recordType,
        string parameterName)
    {
        var ctor = recordType.GetConstructors().FirstOrDefault();
        Assert.NotNull(ctor);

        var parameter = ctor!.GetParameters()
            .FirstOrDefault(p => string.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(parameter);

        return parameter!.GetCustomAttributes<MinLengthAttribute>().FirstOrDefault();
    }

    [Fact]
    public void DoDaiMatKhau_TachRiengMinLengthVaMaxLength()
    {
        // [StringLength(100, MinimumLength = 6, ErrorMessage = "...")] dùng chung
        // 1 message cho cả min & max ⇒ mật khẩu 150 ký tự cũng báo nhầm
        // "phải có ít nhất 6 ký tự" ⇒ thông báo lỗi SAI.
        //
        // Cách đúng: [MinLength(6)] (riêng) + [StringLength(100)] (riêng)
        // ⇒ mỗi ràng buộc có 1 message đúng nghĩa.
        var maxLength = GetStringLengthOnConstructorParameter(
            typeof(RegisterRequestDto),
            nameof(RegisterRequestDto.Password));

        var minLength = GetMinLengthOnConstructorParameter(
            typeof(RegisterRequestDto),
            nameof(RegisterRequestDto.Password));

        Assert.NotNull(maxLength);
        Assert.NotNull(minLength);

        Assert.Equal(100, maxLength!.MaximumLength);
        Assert.Equal(6, minLength!.Length);

        // Message của max-length KHÔNG được nói về min-length.
        Assert.DoesNotContain("ít nhất", maxLength.ErrorMessage!);

        // Message của min-length KHÔNG được nói về max-length.
        Assert.DoesNotContain("100", minLength.ErrorMessage!);
    }

    [Fact]
    public void ResetPasswordRequestDto_CoThongBaoChoCaHaiLoi()
    {
        foreach (var field in new[]
                 {
                     nameof(ResetPasswordRequestDto.NewPassword),
                     nameof(ResetPasswordRequestDto.ConfirmPassword),
                 })
        {
            var maxLength = GetStringLengthOnConstructorParameter(typeof(ResetPasswordRequestDto), field);
            var minLength = GetMinLengthOnConstructorParameter(typeof(ResetPasswordRequestDto), field);

            Assert.NotNull(maxLength);
            Assert.NotNull(minLength);

            Assert.Equal(100, maxLength!.MaximumLength);
            Assert.Equal(6, minLength!.Length);

            Assert.DoesNotContain("ít nhất", maxLength.ErrorMessage!);
            Assert.DoesNotContain("100", minLength.ErrorMessage!);
        }
    }

    // -----------------------------------------------------------------
    // 4) Model validation phải thực sự chạy đúng
    // -----------------------------------------------------------------

    /// <summary>
    /// Validate model GIỐNG HỆT cách ASP.NET Core làm cho record có primary
    /// constructor: đọc <see cref="ValidationAttribute"/> trên <b>constructor
    /// parameter</b> rồi gọi <c>GetValidationResult</c> cho từng giá trị.
    ///
    /// <para>
    /// LƯU Ý: <c>Validator.TryValidateObject</c> chỉ validate <b>property</b> ⇒
    /// với record có primary constructor nó trả về rỗng ⇒ test sẽ pass giả.
    /// Đây chính là lý do phải mô phỏng đúng cơ chế của framework.
    /// </para>
    /// </summary>
    private static IList<ValidationResult> ValidateModelLikeAspNetCore(object model)
    {
        var results = new List<ValidationResult>();
        var type = model.GetType();

        var ctor = type.GetConstructors().FirstOrDefault();
        if (ctor is null)
        {
            return results;
        }

        var parameters = ctor.GetParameters();

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];

            var property = type.GetProperty(
                parameter.Name!,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            var value = property?.GetValue(model);

            var context = new ValidationContext(model, serviceProvider: null, items: null);

            foreach (var attribute in parameter.GetCustomAttributes<ValidationAttribute>())
            {
                var result = attribute.GetValidationResult(value, context);
                if (result is null)
                {
                    continue;
                }

                // Tự tạo ValidationResult để có MemberNames chính xác
                // (GetValidationResult với ValidationContext không có MemberName
                //  ⇒ trả về result không có MemberNames ⇒ test không phân biệt được
                //  field nào bị lỗi).
                var message = result.ErrorMessage ?? attribute.FormatErrorMessage("Giá trị không hợp lệ.");

                results.Add(new ValidationResult(message, new[] { parameter.Name! }));
            }
        }

        return results;
    }

    [Fact]
    public void ResetPasswordRequestDto_MatKhauQuaDai_BaoLoiDungNghia()
    {
        var dto = new ResetPasswordRequestDto(
            "a@b.com", null, null,
            new string('x', 150),
            new string('x', 150));

        var errors = ValidateModelLikeAspNetCore(dto);

        var newPasswordError = errors
            .FirstOrDefault(e => e.MemberNames.Contains(nameof(ResetPasswordRequestDto.NewPassword)));

        Assert.NotNull(newPasswordError);
        Assert.Contains("100", newPasswordError!.ErrorMessage);
        Assert.DoesNotContain("ít nhất 6 ký tự", newPasswordError.ErrorMessage!);
    }

    [Fact]
    public void ResetPasswordRequestDto_MatKhauQuaNgan_BaoLoiDungNghia()
    {
        var dto = new ResetPasswordRequestDto(
            "a@b.com", null, null, "abc", "abc");

        var errors = ValidateModelLikeAspNetCore(dto);

        Assert.Contains(errors, e =>
            e.MemberNames.Contains(nameof(ResetPasswordRequestDto.NewPassword)) &&
            e.ErrorMessage!.Contains("6"));
    }

    [Fact]
    public void RegisterRequestDto_MatKhauHopLe_KhongCoLoi()
    {
        var dto = new RegisterRequestDto(
            "Nguyễn Văn A", "a@b.com", "Abcdef12", "0900000000", "Đà Nẵng");

        var errors = ValidateModelLikeAspNetCore(dto);

        Assert.True(errors.Count == 0,
            "Mật khẩu hợp lệ không được sinh lỗi validation: " +
            string.Join(" | ", errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void RegisterRequestDto_MatKhauQuaDai_BaoLoiDungNghia()
    {
        var dto = new RegisterRequestDto(
            "Nguyễn Văn A", "a@b.com", new string('x', 150), null, null);

        var errors = ValidateModelLikeAspNetCore(dto);

        var passwordError = errors
            .FirstOrDefault(e => e.MemberNames.Contains(nameof(RegisterRequestDto.Password)));

        Assert.NotNull(passwordError);
        Assert.Contains("100", passwordError!.ErrorMessage!);
        Assert.DoesNotContain("ít nhất 6 ký tự", passwordError.ErrorMessage!);
    }

    // -----------------------------------------------------------------
    // 5) Service layer: so khớp mật khẩu phải được kiểm tra
    // -----------------------------------------------------------------

    [Fact]
    public async Task ResetPassword_MatKhauXacNhanKhongKhop_ThrowsArgumentException()
    {
        var repo = new FakeAuthRepository
        {
            Customer = new Customer
            {
                CustomerId = 1,
                Name = "Test",
                Email = "test@example.com",
                Password = BCrypt.Net.BCrypt.HashPassword("oldpass"),
                Role = "user",
                Status = "active"
            }
        };

        var service = BuildService(repo);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ResetPasswordAsync(new ResetPasswordRequestDto(
                "test@example.com", null, "123456", "Abcdef12", "Different12")));

        Assert.Contains("không khớp", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetPassword_ThanhCong_LuuHashBcryptVaXoaOtp()
    {
        var repo = new FakeAuthRepository
        {
            Customer = new Customer
            {
                CustomerId = 1,
                Name = "Test",
                Email = "test@example.com",
                Password = BCrypt.Net.BCrypt.HashPassword("oldpass"),
                OtpCode = "123456",
                OtpExpiry = DateTime.UtcNow.AddMinutes(5),
                Role = "user",
                Status = "active"
            }
        };

        var service = BuildService(repo);

        await service.ResetPasswordAsync(new ResetPasswordRequestDto(
            "test@example.com", null, "123456", "Abcdef12", "Abcdef12"));

        Assert.NotNull(repo.UpdatedCustomer);
        Assert.StartsWith("$2", repo.UpdatedCustomer!.Password);
        Assert.True(BCrypt.Net.BCrypt.Verify("Abcdef12", repo.UpdatedCustomer.Password!));
        // OTP phải bị xoá sau khi dùng (không dùng lại được).
        Assert.Null(repo.UpdatedCustomer.OtpCode);
        Assert.Null(repo.UpdatedCustomer.OtpExpiry);
    }

    [Fact]
    public async Task ResetPassword_OtpSai_ThrowsUnauthorized()
    {
        var repo = new FakeAuthRepository
        {
            Customer = new Customer
            {
                CustomerId = 1,
                Name = "Test",
                Email = "test@example.com",
                Password = BCrypt.Net.BCrypt.HashPassword("oldpass"),
                OtpCode = "111111",
                OtpExpiry = DateTime.UtcNow.AddMinutes(5),
                Role = "user",
                Status = "active"
            }
        };

        var service = BuildService(repo);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ResetPasswordAsync(new ResetPasswordRequestDto(
                "test@example.com", null, "999999", "Abcdef12", "Abcdef12")));
    }

    [Fact]
    public async Task ResetPassword_ThieuResetTokenVaOtp_ThrowsArgument()
    {
        var repo = new FakeAuthRepository
        {
            Customer = new Customer
            {
                CustomerId = 1,
                Name = "Test",
                Email = "test@example.com",
                Password = BCrypt.Net.BCrypt.HashPassword("oldpass"),
                Role = "user",
                Status = "active"
            }
        };

        var service = BuildService(repo);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ResetPasswordAsync(new ResetPasswordRequestDto(
                "test@example.com", null, null, "Abcdef12", "Abcdef12")));
    }

    // -----------------------------------------------------------------
    // Hạ tầng
    // -----------------------------------------------------------------

    private static PetShop.Services.AuthService BuildService(FakeAuthRepository repo)
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<PetShop.Services.AuthService>.Instance;

        return new PetShop.Services.AuthService(
            repo,
            config,
            new FakeEmailService(),
            new NullHttpClientFactory(),
            logger);
    }

    private sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new HttpClient();
    }

    /// <summary>
    /// Fake repository ghi lại entity cuối cùng được <c>UpdateCustomerAsync</c>
    /// để test có thể kiểm tra hash BCrypt + việc xoá OTP sau khi reset.
    /// </summary>
    private sealed class FakeAuthRepository : PetShop.Interfaces.IAuthRepository
    {
        public Customer? Customer { get; set; }

        /// <summary>Entity được ghi lần gọi UpdateCustomerAsync gần nhất.</summary>
        public Customer? UpdatedCustomer { get; private set; }

        public Task<Customer?> GetCustomerByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult(Customer);

        public Task<Staff?> GetStaffByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult<Staff?>(null);

        public Task<Doctor?> GetDoctorByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult<Doctor?>(null);

        public Task<Admin?> GetAdminByEmailOrUsernameAsync(string input, CancellationToken ct = default)
            => Task.FromResult<Admin?>(null);

        public Task<bool> EmailExistsInAnyRoleAsync(string email, CancellationToken ct = default)
            => Task.FromResult(Customer is not null);

        public Task<Customer> CreateCustomerAsync(Customer customer, CancellationToken ct = default)
            => Task.FromResult(customer);

        public Task UpdateCustomerAsync(Customer customer, CancellationToken ct = default)
        {
            UpdatedCustomer = customer;
            return Task.CompletedTask;
        }

        public Task<bool> UpdateCustomerAvatarAsync(int customerId, string? avatarUrl, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<string?> GetCustomerAvatarAsync(int customerId, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<bool> UpdateCustomerBirthDateAsync(int customerId, DateTime? birthDate, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<DateTime?> GetCustomerBirthDateAsync(int customerId, CancellationToken ct = default)
            => Task.FromResult<DateTime?>(null);

        public Task<bool> UpdateCustomerAddressAsync(int customerId, string? address, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<string?> GetCustomerAddressAsync(int customerId, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<bool> UpdateCustomerPhoneAsync(int customerId, string? phone, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<string?> GetCustomerPhoneAsync(int customerId, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<int> UpgradePasswordHashAsync(string email, string newPasswordHash, CancellationToken ct = default)
            => Task.FromResult(1);
    }

    /// <summary>Fake email service — không gửi mail thật.</summary>
    private sealed class FakeEmailService : PetShop.Interfaces.IEmailService
    {
        public Task SendOtpEmailAsync(string toEmail, string otp, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendGoogleFirstLoginEmailAsync(string toEmail, string? name, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendOrderInvoiceEmailAsync(
            string toEmail,
            string? customerName,
            string orderCode,
            IEnumerable<(string ItemName, int Quantity, decimal UnitPrice)> items,
            decimal totalAmount,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendSpaInvoiceEmailAsync(
            string toEmail,
            string? customerName,
            string bookingCode,
            IEnumerable<(string ItemName, int Quantity, decimal UnitPrice)> items,
            decimal totalAmount,
            string paymentStatusLabel,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendBoardingInvoiceEmailAsync(
            string toEmail,
            string? customerName,
            string bookingCode,
            string roomDisplayName,
            DateOnly checkIn,
            DateOnly checkOut,
            int days,
            int pets,
            string? notes,
            decimal pricePerDay,
            decimal totalPrice,
            string paymentMethodLabel,
            string paymentStatusLabel,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
