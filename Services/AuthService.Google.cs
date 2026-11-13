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

            return await CompleteGoogleLoginAsync(email, googleSubject, name, emailVerified, ct);
        }

        /// <summary>
        /// Đăng nhập Google bằng authorization code (OAuth 2.0 Authorization Code flow).
        ///
        /// <para>
        /// GSI (Google Identity Services) chỉ hoạt động từ origin nằm trong
        /// <c>javascript_origins</c> của OAuth client. Nếu user mở app ở origin khác
        /// (vd <c>https://localhost:7083</c>) thì GSI trả 403
        /// <c>"The given origin is not allowed for the given client ID."</c> và không
        /// thể đăng nhập. Flow code chỉ phụ thuộc <c>redirect_uris</c> ⇒ hoạt động từ
        /// mọi origin.
        /// </para>
        /// </summary>
        public async Task<AuthResponseDto> LoginWithGoogleCodeAsync(GoogleCodeLoginRequestDto request, CancellationToken ct = default)
        {
            var clientId = (_configuration["GoogleAuth:ClientId"] ?? string.Empty).Trim();
            var clientSecret = (_configuration["GoogleAuth:ClientSecret"] ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("Google Login chưa cấu hình đúng. Hãy set GoogleAuth:ClientId và GoogleAuth:ClientSecret trong Google Cloud Console.");
            }

            var redirectUri = ResolveRedirectUri(request.Origin);

            // 1) Đổi authorization code lấy access token + id token.
            var tokenResponse = await ExchangeCodeForTokensAsync(clientId, clientSecret, request.Code, redirectUri, ct);

            // 2) Ưu tiên id_token (đã có sẵn thông tin profile) — chỉ cần validate audience.
            if (!string.IsNullOrWhiteSpace(tokenResponse.IdToken))
            {
                var payload = await ValidateGoogleIdTokenAsync(tokenResponse.IdToken!);
                return await CompleteGoogleLoginAsync(payload.Email, payload.Subject, payload.Name, payload.EmailVerified, ct);
            }

            // 3) Không có id_token → gọi userinfo bằng access token.
            if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                throw new UnauthorizedAccessException("Google không trả về token nào hợp lệ.");
            }

            var (email, subject, name, emailVerified) = await GetGoogleProfileFromAccessTokenAsync(tokenResponse.AccessToken!, ct);
            return await CompleteGoogleLoginAsync(email, subject, name, emailVerified, ct);
        }

        /// <summary>
        /// Xác định <c>redirect_uri</c> dùng khi đổi code lấy token.
        ///
        /// <para>
        /// Google so khớp <c>redirect_uri</c> theo thứ tự ưu tiên:
        /// ① khớp chính xác → ② khớp theo scheme+host+port → ③ khớp theo scheme+host
        /// (bỏ port) → ④ khớp theo path.
        /// </para>
        ///
        /// <para>
        /// Origin do client gửi lên có thể là <c>https://localhost:7083</c> (chưa đăng ký)
        /// nên <b>không</b> dùng nó làm redirect_uri. Thay vào đó luôn dùng giá trị
        /// <c>GoogleAuth:RedirectUri</c> đã cấu hình (đăng ký trong OAuth client).
        /// </para>
        /// </summary>
        private string ResolveRedirectUri(string? origin)
        {
            var configured = (_configuration["GoogleAuth:RedirectUri"] ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            if (!string.IsNullOrWhiteSpace(origin))
            {
                return origin.TrimEnd('/');
            }

            throw new InvalidOperationException("Google Login chưa cấu hình redirect_uri. Hãy set GoogleAuth:RedirectUri.");
        }

        private async Task<GoogleTokenResponse> ExchangeCodeForTokensAsync(
            string clientId,
            string clientSecret,
            string code,
            string redirectUri,
            CancellationToken ct)
        {
            var httpClient = _httpClientFactory.CreateClient(nameof(AuthService));
            httpClient.BaseAddress = new Uri("https://oauth2.googleapis.com/");

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            });

            using var response = await httpClient.PostAsync("token", content, ct);

            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new UnauthorizedAccessException($"Google không chấp nhận authorization code: {ExtractGoogleError(json)}");
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            string? idToken = root.TryGetProperty("id_token", out var it) ? it.GetString() : null;

            return new GoogleTokenResponse(accessToken, idToken);
        }

        private static string ExtractGoogleError(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("error_description", out var desc) &&
                    desc.GetString() is { Length: > 0 } message)
                {
                    return message;
                }

                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("error", out var err) &&
                    err.GetString() is { Length: > 0 } code)
                {
                    return code;
                }
            }
            catch (JsonException)
            {
                // Không parse được ⇒ trả raw body (đã cắt ngắn).
            }

            return json.Length > 300 ? json[..300] : json;
        }

        private sealed record GoogleTokenResponse(string? AccessToken, string? IdToken);

        /// <summary>
        /// Phần chung của mọi luồng đăng nhập Google: kiểm tra email, tìm/tạo
        /// khách hàng, gửi email lần đầu và trả về <see cref="AuthResponseDto"/>.
        /// </summary>
        private async Task<AuthResponseDto> CompleteGoogleLoginAsync(
            string? email,
            string? googleSubject,
            string? name,
            bool? emailVerified,
            CancellationToken ct)
        {
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
                // TỐI ƯU ĐỘ TRỄ (chống lag khi đăng nhập Google):
                //
                // Trước đây gửi email SMTP được `await` ĐỒNG BỘ trong luồng đăng nhập.
                // MailKit mở kết nối SMTP + STARTTLS ⇒ 1-10 giây ⇒ thread pool
                // bị giữ ⇒ toàn bộ request khác (kể cả các request của circuit
                // Blazor đang render) phải chờ ⇒ hiện tượng "đăng nhập Google rất lag".
                //
                // Nay: đẩy sang luồng nền (fire-and-forget) ⇒ đăng nhập trả về ngay.
                // Lỗi gửi email KHÔNG được làm hỏng đăng nhập (chỉ log).
                _ = SendFirstLoginEmailInBackgroundAsync(customer.Email!, customer.Name);
            }

            return MapToAuthResponse(customer);
        }

        /// <summary>
        /// Gửi email "chào mừng lần đăng nhập Google đầu tiên" ở LUỒNG NỀN.
        ///
        /// <para>
        /// Mục đích: không chặn người dùng đăng nhập vài giây chờ SMTP.
        /// Mọi lỗi đều bị nuốt kèm log — đăng nhập vẫn thành công.
        /// </para>
        /// </summary>
        private async Task SendFirstLoginEmailInBackgroundAsync(string email, string? name)
        {
            try
            {
                await _emailService.SendGoogleFirstLoginEmailAsync(email, name ?? string.Empty, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[AuthService.Google] Gửi email chào mừng nền thất bại (không ảnh hưởng đăng nhập): {Error}", ex.Message);
            }
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
