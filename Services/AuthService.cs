using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services
{
    public sealed partial class AuthService : IAuthService
    {
        private readonly IAuthRepository _authRepository;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;
        private readonly IHttpClientFactory _httpClientFactory;

        public AuthService(
            IAuthRepository authRepository,
            IConfiguration configuration,
            IEmailService emailService,
            IHttpClientFactory httpClientFactory)
        {
            _authRepository = authRepository;
            _configuration = configuration;
            _emailService = emailService;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, CancellationToken ct = default)
        {
            var email = NormalizeEmail(request.Email);

            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email không được để trống.");
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                throw new ArgumentException("Mật khẩu không được để trống.");
            }

            var existing = await _authRepository.GetCustomerByEmailAsync(email, ct);
            if (existing is not null)
            {
                throw new InvalidOperationException("Email đã tồn tại.");
            }

            var customer = new Customer
            {
                Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
                Email = email,
                Password = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
                AddressCustomer = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
                Role = "user",
                Status = "inactive"
            };

            var created = await _authRepository.CreateCustomerAsync(customer, ct);

            var otp = GenerateOtp();
            created.OtpCode = otp;
            created.OtpExpiry = DateTime.UtcNow.AddMinutes(5);
            await _authRepository.UpdateCustomerAsync(created, ct);

            await _emailService.SendOtpEmailAsync(created.Email!, otp, ct);

            return MapToAuthResponse(created);
        }

        public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default)
        {
            var identity = (request.Email ?? string.Empty).Trim();
            var normalizedEmail = NormalizeEmail(request.Email);
            var password = request.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(password))
            {
                throw new UnauthorizedAccessException("Email hoặc mật khẩu không hợp lệ.");
            }

            var staff = await _authRepository.GetStaffByEmailAsync(normalizedEmail, ct);
            if (staff is not null && await VerifyAndUpgradeAsync(password, staff.Password, normalizedEmail, ct))
            {
                var role = string.Equals(staff.Position, "admin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(staff.Position, "quản lý", StringComparison.OrdinalIgnoreCase)
                    ? "admin"
                    : "staff";

                return new AuthResponseDto(
                    staff.StaffId,
                    staff.Name,
                    staff.Email ?? normalizedEmail,
                    role,
                    "active"
                );
            }

            var doctor = await _authRepository.GetDoctorByEmailAsync(normalizedEmail, ct);
            if (doctor is not null && await VerifyAndUpgradeAsync(password, doctor.Password, normalizedEmail, ct))
            {
                return new AuthResponseDto(
                    doctor.DoctorId,
                    doctor.Name,
                    doctor.Email,
                    "doctor",
                    "active"
                );
            }

            var admin = await _authRepository.GetAdminByEmailOrUsernameAsync(identity, ct);
            if (admin is not null && await VerifyAndUpgradeAsync(password, admin.Password, normalizedEmail, ct))
            {
                return new AuthResponseDto(
                    admin.AdminId,
                    admin.Name ?? admin.Username,
                    admin.Email ?? normalizedEmail,
                    "admin",
                    "active"
                );
            }

            var customer = await _authRepository.GetCustomerByEmailAsync(normalizedEmail, ct);
            if (customer is null || string.IsNullOrWhiteSpace(customer.Password))
            {
                throw new UnauthorizedAccessException("Email hoặc mật khẩu không đúng.");
            }

            if (!await VerifyAndUpgradeAsync(password, customer.Password, normalizedEmail, ct))
            {
                throw new UnauthorizedAccessException("Email hoặc mật khẩu không đúng.");
            }

            if (!string.Equals(customer.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Tài khoản chưa xác thực email. Vui lòng nhập OTP đã gửi qua email.");
            }

            return MapToAuthResponse(customer);
        }

        public async Task SendEmailOtpAsync(SendEmailOtpRequestDto request, CancellationToken ct = default)
        {
            var email = NormalizeEmail(request.Email);
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email không được để trống.");
            }

            var hasAccount = await _authRepository.EmailExistsInAnyRoleAsync(email, ct);
            if (!hasAccount)
            {
                throw new KeyNotFoundException("Email chưa đăng ký tài khoản.");
            }

            var customer = await _authRepository.GetCustomerByEmailAsync(email, ct)
                ?? throw new KeyNotFoundException("Email chưa có tài khoản khách hàng để nhận OTP.");

            var otp = GenerateOtp();
            customer.OtpCode = otp;
            customer.OtpExpiry = DateTime.UtcNow.AddMinutes(5);
            await _authRepository.UpdateCustomerAsync(customer, ct);

            await _emailService.SendOtpEmailAsync(email, otp, ct);
        }

        public async Task<SimpleMessageDto> VerifyEmailOtpAsync(VerifyEmailOtpRequestDto request, CancellationToken ct = default)
        {
            var email = NormalizeEmail(request.Email);
            var otp = request.OtpCode?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(otp))
            {
                throw new ArgumentException("Email và mã OTP không được để trống.");
            }

            var customer = await _authRepository.GetCustomerByEmailAsync(email, ct)
                ?? throw new KeyNotFoundException("Không tìm thấy tài khoản với email này.");

            ValidateOtp(customer, otp);

            customer.Status = "active";
            customer.ResetToken = GenerateResetToken();
            customer.ResetTokenExpiry = DateTime.UtcNow.AddMinutes(5);
            await _authRepository.UpdateCustomerAsync(customer, ct);

            return new SimpleMessageDto(customer.ResetToken ?? string.Empty);
        }

        public async Task ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken ct = default)
        {
            var email = NormalizeEmail(request.Email);
            var resetToken = request.ResetToken?.Trim() ?? string.Empty;
            var otp = request.OtpCode?.Trim() ?? string.Empty;
            var newPassword = request.NewPassword ?? string.Empty;
            var confirmPassword = request.ConfirmPassword ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email không được để trống.");
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                throw new ArgumentException("Mật khẩu mới không được để trống.");
            }

            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            {
                throw new ArgumentException("Mật khẩu xác nhận không khớp.");
            }

            var customer = await _authRepository.GetCustomerByEmailAsync(email, ct)
                ?? throw new KeyNotFoundException("Không tìm thấy tài khoản với email này.");

            var usedResetToken = false;
            if (!string.IsNullOrWhiteSpace(resetToken))
            {
                ValidateResetToken(customer, resetToken);
                usedResetToken = true;
            }
            else if (!string.IsNullOrWhiteSpace(otp))
            {
                ValidateOtp(customer, otp);
            }
            else
            {
                throw new ArgumentException("Cần cung cấp reset token hoặc OTP.");
            }

            customer.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
            ConsumeOtp(customer);
            if (usedResetToken)
            {
                ConsumeResetToken(customer);
            }
            await _authRepository.UpdateCustomerAsync(customer, ct);
        }

        private static void ValidateOtp(Customer customer, string otp)
        {
            if (string.IsNullOrWhiteSpace(customer.OtpCode) || customer.OtpExpiry is null)
            {
                throw new UnauthorizedAccessException("Mã OTP không hợp lệ hoặc chưa được gửi.");
            }

            if (customer.OtpExpiry < DateTime.UtcNow)
            {
                throw new UnauthorizedAccessException("Mã OTP đã hết hạn.");
            }

            if (!string.Equals(customer.OtpCode, otp, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("Mã OTP không đúng.");
            }
        }

        private static void ConsumeOtp(Customer customer)
        {
            customer.OtpCode = null;
            customer.OtpExpiry = null;
        }

        private static void ValidateResetToken(Customer customer, string resetToken)
        {
            if (string.IsNullOrWhiteSpace(customer.ResetToken) || customer.ResetTokenExpiry is null)
            {
                throw new UnauthorizedAccessException("Reset token không hợp lệ hoặc chưa được cấp.");
            }

            if (customer.ResetTokenExpiry < DateTime.UtcNow)
            {
                throw new UnauthorizedAccessException("Reset token đã hết hạn.");
            }

            if (!string.Equals(customer.ResetToken, resetToken, StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException("Reset token không đúng.");
            }
        }

        private static void ConsumeResetToken(Customer customer)
        {
            customer.ResetToken = null;
            customer.ResetTokenExpiry = null;
        }


        private static string GenerateOtp()
        {
            var random = new Random();
            return random.Next(100000, 999999).ToString();
        }

        private static string GenerateResetToken()
        {
            return Guid.NewGuid().ToString("N");
        }

        private static string NormalizeEmail(string? email)
        {
            return (email ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static bool IsPlaceholder(string value)
        {
            return value.Contains("YOUR_", StringComparison.OrdinalIgnoreCase)
                   || value.Contains("YOUR-", StringComparison.OrdinalIgnoreCase)
                   || value.Contains("example", StringComparison.OrdinalIgnoreCase)
                   || value.Contains("placeholder", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Kiểm tra mật khẩu và tự nâng cấp hash nếu tài khoản đang lưu mật khẩu dạng chữ đơn giản.
        ///
        /// Migrate-on-login: dữ liệu seed của dự án học có mật khẩu plaintext (vd "staff123").
        /// Thay vì giữ nhánh so sánh plaintext vĩnh viễn, ta cho đăng nhập một lần rồi
        /// ghi lại bằng hash BCrypt — sau đó mọi lần đăng nhập sau đều qua BCrypt.
        /// </summary>
        private async Task<bool> VerifyAndUpgradeAsync(
            string password,
            string? storedPassword,
            string normalizedEmail,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(storedPassword))
            {
                return false;
            }

            var normalizedStored = storedPassword.Trim();

            // Đã là hash BCrypt → kiểm tra thẳng.
            if (normalizedStored.StartsWith("$2", StringComparison.Ordinal))
            {
                try
                {
                    return BCrypt.Net.BCrypt.Verify(password, normalizedStored);
                }
                catch (BCrypt.Net.SaltParseException)
                {
                    // Hash lưu trong DB không đúng định dạng BCrypt → không khớp.
                    return false;
                }
            }

            // Chưa hash: chỉ chấp nhận nếu mật khẩu khớp, rồi nâng cấp ngay.
            if (!string.Equals(password, normalizedStored, StringComparison.Ordinal))
            {
                return false;
            }

            var upgraded = await _authRepository.UpgradePasswordHashAsync(
                normalizedEmail,
                BCrypt.Net.BCrypt.HashPassword(password),
                ct);

            return upgraded > 0;
        }

        private static AuthResponseDto MapToAuthResponse(Customer customer)
        {
            return new AuthResponseDto(
                customer.CustomerId,
                customer.Name ?? string.Empty,
                customer.Email ?? string.Empty,
                customer.Role,
                customer.Status ?? string.Empty,
                customer.AvatarUrl
            );
        }
    }
}