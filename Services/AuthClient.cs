using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services;

public sealed class AuthClient : IAuthClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AuthClient> _logger;
    private readonly UserSessionService? _userSession;

    public AuthClient(HttpClient httpClient,
        ILogger<AuthClient> logger,
        UserSessionService? userSession)
    {
        _httpClient = httpClient;
        _logger = logger;
        _userSession = userSession;
    }

    /// <summary>
    /// Tạo request đã gắn header phiên đăng nhập (X-User-Id / X-Customer-Id / X-User-Role).
    ///
    /// <para>
    /// Dùng <see cref="SessionHeader.Attach"/> (như <c>PetsClient</c> / <c>ChatClient</c>)
    /// thay vì <c>SpaAuthHeaderHandler</c>: handler do <c>IHttpClientFactory</c> tạo nằm
    /// ở scope riêng ⇒ không thấy <c>UserSessionService</c> của circuit ⇒ header
    /// không được gắn ⇒ <c>api/auth/me</c> trả 401.
    /// </para>
    /// </summary>
    private HttpRequestMessage CreateAuthedRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        return SessionHeader.Attach(request, _userSession?.CurrentUser);
    }

    public Task<FrontendApiResult<AuthResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<AuthResponseDto>("api/auth/login", request, cancellationToken);

    public Task<FrontendApiResult<AuthResponseDto>> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<AuthResponseDto>("api/auth/register", request, cancellationToken);

    // QUAN TRỌNG: đường dẫn phải khớp CHÍNH XÁC route trong AuthController.
    //   Controller: [HttpPost("send-email-otp")]  ⇒ /api/auth/send-email-otp
    //   Controller: [HttpPost("verify-email-otp")] ⇒ /api/auth/verify-email-otp
    // Trước đây client gọi "api/auth/send-otp" / "api/auth/verify-otp" ⇒ 404 ⇒
    // luồng quên mật khẩu KHÔNG BAO GIỜ chạy được (bước 1 và bước 2 đều chết).
    // Có test contract (AuthRouteContractTests) canh gác để không tái phát.
    public Task<FrontendApiResult<SimpleMessageDto>> SendOtpAsync(SendEmailOtpRequestDto request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<SimpleMessageDto>("api/auth/send-email-otp", request, cancellationToken);

    public Task<FrontendApiResult<SimpleMessageDto>> VerifyOtpAsync(VerifyEmailOtpRequestDto request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<SimpleMessageDto>("api/auth/verify-email-otp", request, cancellationToken);

    public Task<FrontendApiResult<SimpleMessageDto>> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<SimpleMessageDto>("api/auth/reset-password", request, cancellationToken);

    public Task<FrontendApiResult<AuthResponseDto>> GoogleLoginAsync(GoogleLoginRequestDto request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<AuthResponseDto>("api/auth/google-login", request, cancellationToken);

    /// <inheritdoc />
    public Task<FrontendApiResult<AuthResponseDto>> GoogleCodeLoginAsync(GoogleCodeLoginRequestDto request, CancellationToken cancellationToken = default) =>
        PostJsonAsync<AuthResponseDto>("api/auth/google-code-login", request, cancellationToken);

    /// <inheritdoc />
    public async Task<FrontendApiResult<string?>> GetAvatarAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = CreateAuthedRequest(HttpMethod.Get, "api/auth/me");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<string?>.Failure(await ReadErrorAsync(response, cancellationToken), (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<CurrentUserDto>(cancellationToken);
            return FrontendApiResult<string?>.Success(data?.AvatarUrl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<string?>.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public Task<FrontendApiResult<SimpleMessageDto>> UpdateAvatarAsync(string? avatarUrl, CancellationToken cancellationToken = default)
    {
        var payload = new UpdateAvatarRequestDto(avatarUrl);
        return PostJsonAsync<SimpleMessageDto>("api/auth/avatar", payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FrontendApiResult<DateTime?>> GetBirthDateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = CreateAuthedRequest(HttpMethod.Get, "api/auth/me");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<DateTime?>.Failure(await ReadErrorAsync(response, cancellationToken), (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<CurrentUserDto>(cancellationToken);
            return FrontendApiResult<DateTime?>.Success(data?.BirthDate);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<DateTime?>.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public Task<FrontendApiResult<SimpleMessageDto>> UpdateBirthDateAsync(DateTime? birthDate, CancellationToken cancellationToken = default)
    {
        var payload = new UpdateBirthDateRequestDto(birthDate);
        return PostJsonAsync<SimpleMessageDto>("api/auth/birth-date", payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FrontendApiResult<string?>> GetAddressAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = CreateAuthedRequest(HttpMethod.Get, "api/auth/me");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<string?>.Failure(await ReadErrorAsync(response, cancellationToken), (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<CurrentUserDto>(cancellationToken);
            return FrontendApiResult<string?>.Success(data?.Address);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<string?>.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public Task<FrontendApiResult<SimpleMessageDto>> UpdateAddressAsync(string? address, CancellationToken cancellationToken = default)
    {
        var payload = new UpdateAddressRequestDto(address);
        return PostJsonAsync<SimpleMessageDto>("api/auth/address", payload, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FrontendApiResult<SimpleMessageDto>> UpdateAddressWithPhoneAsync(
        string? address,
        string? phone,
        CancellationToken cancellationToken = default)
    {
        var payload = new UpdateAddressWithPhoneRequestDto(address, phone);
        return PostJsonAsync<SimpleMessageDto>("api/auth/address-with-phone", payload, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FrontendApiResult<string?>> GetPhoneAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = CreateAuthedRequest(HttpMethod.Get, "api/auth/me");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<string?>.Failure(await ReadErrorAsync(response, cancellationToken), (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<CurrentUserDto>(cancellationToken);
            return FrontendApiResult<string?>.Success(data?.Phone);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<string?>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Lấy thông tin client Google (Client ID + cờ GSI có dùng được ở origin hiện tại).
    /// </summary>
    public async Task<FrontendApiResult<GoogleClientInfoDto>> GetGoogleClientInfoAsync(
        string? origin,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var uri = "api/auth/google-client-id";
            if (!string.IsNullOrWhiteSpace(origin))
            {
                uri += "?origin=" + Uri.EscapeDataString(origin);
            }

            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return FrontendApiResult<GoogleClientInfoDto>.Failure(await ReadErrorAsync(response, cancellationToken), (int)response.StatusCode);

            var data = await response.Content.ReadFromJsonAsync<GoogleClientInfoDto>(cancellationToken);
            if (data is null)
                return FrontendApiResult<GoogleClientInfoDto>.Failure("Không đọc được thông tin client Google.");

            return FrontendApiResult<GoogleClientInfoDto>.Success(data);
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<GoogleClientInfoDto>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Lấy URL đăng nhập Google (Authorization Code flow) để dùng làm fallback
    /// khi GSI bị chặn vì origin chưa nằm trong <c>javascript_origins</c>.
    /// </summary>
    public async Task<FrontendApiResult<GoogleAuthorizeUrlDto>> GetGoogleAuthorizeUrlAsync(
        string? origin,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var uri = "api/auth/google-authorize-url";
            if (!string.IsNullOrWhiteSpace(origin))
            {
                uri += "?origin=" + Uri.EscapeDataString(origin);
            }

            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return FrontendApiResult<GoogleAuthorizeUrlDto>.Failure(await ReadErrorAsync(response, cancellationToken), (int)response.StatusCode);

            var data = await response.Content.ReadFromJsonAsync<GoogleAuthorizeUrlDto>(cancellationToken);
            if (data is null)
                return FrontendApiResult<GoogleAuthorizeUrlDto>.Failure("Không đọc được URL đăng nhập Google.");

            return FrontendApiResult<GoogleAuthorizeUrlDto>.Success(data);
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<GoogleAuthorizeUrlDto>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Đổi one-time token (do <c>google-callback</c> sinh) lấy thông tin user.
    /// </summary>
    public Task<FrontendApiResult<AuthResponseDto>> GoogleExchangeAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<AuthResponseDto>(
            "api/auth/google-exchange",
            new GoogleExchangeRequestDto(token),
            cancellationToken);
    }

    private async Task<FrontendApiResult<T>> PostJsonAsync<T>(string uri, object request, CancellationToken cancellationToken)
    {
        try
        {
            using var httpRequest = CreateAuthedRequest(HttpMethod.Post, uri);
            httpRequest.Content = JsonContent.Create(request);
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<T>.Failure(await ReadErrorAsync(response, cancellationToken), (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            return data is null
                ? FrontendApiResult<T>.Failure("API returned an empty response.")
                : FrontendApiResult<T>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<T>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Đọc nội dung lỗi từ response và trả về thông điệp <b>tiếng Việt dễ đọc</b>
    /// (xem <see cref="ApiErrorMessageReader"/> để biết các dạng body được hỗ trợ).
    /// </summary>
    private static Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => ApiErrorMessageReader.ReadAsync(response, cancellationToken);
}
