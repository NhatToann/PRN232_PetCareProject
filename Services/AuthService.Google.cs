using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services
{
    /// <summary>
    /// Pháº§n Ä‘Äƒng nháº­p báº±ng Google OAuth (IdToken / AccessToken).
    /// Xem <see cref="AuthService"/> Ä‘á»ƒ pháº§n Ä‘Äƒng kĂ½, Ä‘Äƒng nháº­p báº±ng máº­t kháº©u, OTP vĂ  Ä‘áº·t láº¡i máº­t kháº©u.
    /// </summary>
    public sealed partial class AuthService
    {
        public async Task<AuthResponseDto> LoginWithGoogleAsync(GoogleLoginRequestDto request, CancellationToken ct = default)
        {
            var hasIdToken = !string.IsNullOrWhiteSpace(request.IdToken);
            var hasAccessToken = !string.IsNullOrWhiteSpace(request.AccessToken);

            if (!hasIdToken && !hasAccessToken)
            {
                throw new ArgumentException("Cần cung cấp IdToken hoặc AccessToken.");
            }

            string? email = null;
            string? googleSubject = null;
            string? name = null;
            bool? emailVerified = null;

            if (hasIdToken)
            {
                var payload = await ValidateGoogleIdTokenAsync(request.IdToken!);
                email = payload.Email;
                googleSubject = payload.Subject;
                name = payload.Name;
                emailVerified = payload.EmailVerified;
            }
            else
            {
                (email, googleSubject, name, emailVerified) = await GetGoogleProfileFromAccessTokenAsync(request.AccessToken!, ct);
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                throw new UnauthorizedAccessException("Không lấy được email từ Google.");
            }

            if (emailVerified.HasValue && emailVerified.Value == false)
            {
                throw new UnauthorizedAccessException("Email Google chưa được xác minh.");
            }

            email = NormalizeEmail(email);

            var customer = await _authRepository.GetCustomerByEmailAsync(email, ct);

            var isFirstGoogleLogin = customer is null;

            if (customer is null)
            {
                customer = new Customer
                {
                    Name = name,
                    Email = email,
                    GoogleId = googleSubject,
                    Password = null,
                    Role = "user",
                    Status = "active"
                };

                customer = await _authRepository.CreateCustomerAsync(customer, ct);
            }
            else
            {
                customer.GoogleId ??= googleSubject;
                customer.Name = string.IsNullOrWhiteSpace(customer.Name) ? name : customer.Name;
                customer.Status = "active";
                await _authRepository.UpdateCustomerAsync(customer, ct);
            }

            if (isFirstGoogleLogin)
            {
                await _emailService.SendGoogleFirstLoginEmailAsync(customer.Email!, customer.Name, ct);
            }

            return MapToAuthResponse(customer);
        }

        private async Task<GoogleJsonWebSignature.Payload> ValidateGoogleIdTokenAsync(string idToken)
        {
            try
            {
                var audiences = GetGoogleAudiences();

                if (audiences.Count == 0)
                {
                    throw new InvalidOperationException("Google Login chưa cấu hình đúng. Hãy set GoogleAuth:ClientId hoặc GoogleAuth:ClientIds bằng Client ID thật từ Google Cloud Console.");
                }

                return await GoogleJsonWebSignature.ValidateAsync(idToken,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = audiences
                    });
            }
            catch (InvalidJwtException ex)
            {
                throw new UnauthorizedAccessException($"Google IdToken không hợp lệ: {ex.Message}");
            }
            catch (Newtonsoft.Json.JsonReaderException ex)
            {
                // Token không phải JWT (sai định dạng) ⇒ thư viện Google ném JsonReaderException
                // thay vì InvalidJwtException. Không bắt thì API trả 500 thay vì 401.
                throw new UnauthorizedAccessException($"Google IdToken không hợp lệ: {ex.Message}");
            }
        }

        private async Task<(string? Email, string? Subject, string? Name, bool? EmailVerified)> GetGoogleProfileFromAccessTokenAsync(string accessToken, CancellationToken ct)
        {
            // Dùng IHttpClientFactory để không tạo mới HttpClient (socket leak) mỗi lần đăng nhập Google.
            var httpClient = _httpClientFactory.CreateClient(nameof(AuthService));
            httpClient.BaseAddress = new Uri("https://www.googleapis.com/");
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await httpClient.GetAsync("oauth2/v3/userinfo", ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new UnauthorizedAccessException("Google AccessToken không hợp lệ hoặc đã hết hạn.");
            }

            var json = await response.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? email = root.TryGetProperty("email", out var emailElement) ? emailElement.GetString() : null;
            string? subject = root.TryGetProperty("sub", out var subElement) ? subElement.GetString() : null;
            string? name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            bool? emailVerified = root.TryGetProperty("email_verified", out var emailVerifiedElement)
                ? emailVerifiedElement.GetBoolean()
                : null;

            return (email, subject, name, emailVerified);
        }


        private List<string> GetGoogleAudiences()
        {
            var audiences = new List<string>();

            var singleClientId = _configuration["GoogleAuth:ClientId"]?.Trim();
            if (!string.IsNullOrWhiteSpace(singleClientId) && !IsPlaceholder(singleClientId))
            {
                audiences.Add(singleClientId);
            }

            var clientIdsCsv = _configuration["GoogleAuth:ClientIds"]?.Trim();
            if (!string.IsNullOrWhiteSpace(clientIdsCsv))
            {
                foreach (var id in clientIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!string.IsNullOrWhiteSpace(id) && !IsPlaceholder(id) && !audiences.Contains(id, StringComparer.OrdinalIgnoreCase))
                    {
                        audiences.Add(id);
                    }
                }
            }

            return audiences;
        }
    }
}
