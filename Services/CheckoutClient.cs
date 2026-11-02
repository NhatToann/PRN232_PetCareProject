using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using PetShop.Interfaces;
using PetShop.Models;

namespace PetShop.Services;

public sealed class CheckoutClient : ICheckoutClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CheckoutClient> _logger;

    public CheckoutClient(HttpClient httpClient,
        ILogger<CheckoutClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<FrontendApiResult<FrontendCheckoutResponse>> CheckoutAsync(FrontendCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("api/checkout", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FrontendApiResult<FrontendCheckoutResponse>.Failure(await ReadErrorAsync(response, cancellationToken), (int)response.StatusCode);
            }

            var data = await response.Content.ReadFromJsonAsync<FrontendCheckoutResponse>(cancellationToken);
            return data is null
                ? FrontendApiResult<FrontendCheckoutResponse>.Failure("Checkout API returned an empty response.")
                : FrontendApiResult<FrontendCheckoutResponse>.Success(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LineError(ex, ex.Message);
            return FrontendApiResult<FrontendCheckoutResponse>.Failure(ex.Message);
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body) ? $"API returned {(int)response.StatusCode}." : body;
    }
}
