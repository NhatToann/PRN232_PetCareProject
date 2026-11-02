using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PetShop.Interfaces;
using PetShop.Models;
using PetShop.Services;

namespace PetShop_PRN222.Components.Pages
{
    /// <summary>
    /// Pháº§n code-behind cá»§a <c>SpaCart.razor</c>.
    /// TĂ¡ch riĂªng Ä‘á»ƒ file .razor khĂ´ng vÆ°á»£t quĂ¡ 500 dĂ²ng (quy Æ°á»›c trong AGENTS.md Â§2.3).
    /// </summary>
    public partial class SpaCart
    {    private static readonly System.Globalization.CultureInfo Vi = new("vi-VN");

    [SupplyParameterFromQuery(Name = "serviceId")] private int? InitialServiceId { get; set; }

    private bool isLoading = true;
    private bool isSubmitting;
    private string? error;

    private List<PetShop.Models.PetServiceDto> Services = new();
    private List<PetShop.Models.PetWithBreedPriceDto> Pets = new();
    private readonly Dictionary<int, Dictionary<int, PetShop.Models.PetWithBreedPriceDto>> _petPricingByService = new();

    private class FormState
    {
        public string date = "";
        public string time = "";
        public HashSet<int> petIds = new();
        public string paymentMethod = "cash";
        public string note = "";
    }
    private readonly Dictionary<int, FormState> _forms = new();

    private class TimeValidationState { public bool isValid = true; public string? message; public bool isChecking; }
    private readonly Dictionary<int, TimeValidationState> TimeValidations = new();
    private readonly Dictionary<int, CancellationTokenSource> _availabilityCts = new();
    private readonly Dictionary<int, int> _availabilityVersion = new();

    private readonly Dictionary<int, string> ValidationErrors = new();

    private string sellerNote = "";

    private record SlotModalState(bool open, int? serviceId, string message, string? suggestedStart);
    private SlotModalState slotSuggestionModal = new(false, null, "", null);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
        try { await LoadAsync(); }
        catch (Exception ex) { error = $"Lỗi khởi tạo: {ex.Message}"; isLoading = false; StateHasChanged(); }
        }
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        error = null;

        try
        {
            try { await UserSession.LoadAsync(); }
            catch (Exception ex) { Logger.LineWarning($"[SpaCart.razor] LoadAsync UserSession.LoadAsync threw: {ex.GetType().Name} {ex.Message}"); }
            // ProtectedLocalStorage can fail when localStorage was set without DPAPI (e.g., tests, manual seeds)

            // Read cart from localStorage via JS interop
            int[] cartIds = await JS.InvokeAsync<int[]>("spaCart.read");
            if (InitialServiceId is int sid && sid > 0)
            {
                cartIds = (await JS.InvokeAsync<int[]>("spaCart.add", sid)).Distinct().ToArray();
            }
            cartIds = cartIds ?? Array.Empty<int>();
            cartIds = cartIds.Where(id => id > 0).Distinct().ToArray();

            if (cartIds.Length == 0)
            {
                Services = new();
                Pets = new();
                isLoading = false;
                StateHasChanged();
                return;
            }

            // Fetch each service detail (React parity: /api/pet-services/{id})
            var serviceTasks = cartIds
                .Select(id => PetServiceClient.GetDetailAsync(id))
                .ToArray();
            var serviceDetails = await Task.WhenAll(serviceTasks);
            var servicesData = new List<PetShop.Models.PetServiceDto>();
            foreach (var result in serviceDetails)
            {
                if (result.IsSuccess && result.Data is not null)
                {
                    servicesData.Add(result.Data.Service);
                }
            }
            Services = servicesData;

            // Fetch pets if customer known
            var customerId = UserSession.CurrentUser?.UserId ?? 0;
            if (customerId > 0)
            {
                var petsRes = await PetsClient.GetByCustomerAsync(customerId);
                if (petsRes.IsSuccess && petsRes.Data is not null)
                {
                    Pets = petsRes.Data.ToList();
                }
                else
                {
                    Pets = new();
                }
            }
            else
            {
                Pets = new();
            }

            // Initialize per-service form state
            _forms.Clear();
            foreach (var s in Services)
            {
                _forms[s.ServiceId] = new FormState();
            }

            // Per-service pet pricing (React parity: GET /api/pets/customer/{cid}/service/{sid}/pricing)
            _petPricingByService.Clear();
            if (customerId > 0 && Services.Count > 0)
            {
                var pricingTasks = Services
                    .Select(svc => (svc.ServiceId, Task: PetsClient.GetWithPricingAsync(customerId, svc.ServiceId)))
                    .ToList();
                var pricingResults = await Task.WhenAll(pricingTasks.Select(t => t.Task));
                for (int i = 0; i < pricingTasks.Count; i++)
                {
                    var (pricingSid, result) = (pricingTasks[i].ServiceId, pricingResults[i]);
                    if (result.IsSuccess && result.Data is not null)
                    {
                        var d = new Dictionary<int, PetShop.Models.PetWithBreedPriceDto>();
                        foreach (var p in result.Data) d[p.PetId] = p;
                        _petPricingByService[pricingSid] = d;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    private FormState GetForm(int serviceId) =>
        _forms.TryGetValue(serviceId, out var f) ? f : _forms[serviceId] = new FormState();

    private void UpdateForm(int serviceId, string? date = null, string? time = null, string? paymentMethod = null)
    {
        var f = GetForm(serviceId);
        if (date is not null) f.date = date;
        if (time is not null) f.time = time;
        if (paymentMethod is not null) f.paymentMethod = paymentMethod;

        if (date is not null || time is not null)
        {
            // Cancel any pending availability check for this service
            if (_availabilityCts.TryGetValue(serviceId, out var cts))
            {
                CancelAvailabilityCheck(cts, "đổi ngày/giờ");
                _availabilityCts.Remove(serviceId);
            }
            TimeValidations[serviceId] = new TimeValidationState();
            _availabilityVersion[serviceId] = _availabilityVersion.TryGetValue(serviceId, out var v) ? v + 1 : 1;
            _ = CheckAvailabilityAsync(serviceId);
        }
        StateHasChanged();
    }

    private async Task CheckAvailabilityAsync(int serviceId, CancellationToken cancellationToken = default)
    {
        var f = GetForm(serviceId);
        if (string.IsNullOrEmpty(f.date) || string.IsNullOrEmpty(f.time)) return;
        var service = Services.FirstOrDefault(s => s.ServiceId == serviceId);
        if (service is null) return;

        DateTime start;
        try { start = DateTime.Parse($"{f.date}T{f.time}"); }
        catch { return; }

        var expectedVersion = _availabilityVersion.TryGetValue(serviceId, out var v) ? v : 0;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        _availabilityCts[serviceId] = cts;

        var selectedPets = GetPetsForService(serviceId).Where(p => f.petIds.Contains(p.PetId)).ToList();
        var qty = Math.Min(2, Math.Max(1, selectedPets.Count > 0 ? selectedPets.Count : 1));
        var result = await SpaClient.CheckAvailabilityAsync(start, service.Duration, qty, cts.Token);
        if (cts.Token.IsCancellationRequested) return;

        // Discard stale result if a newer request was started
        if (_availabilityVersion.TryGetValue(serviceId, out var currentVersion) && currentVersion != expectedVersion) return;

        if (!result.IsSuccess || result.Data is null) return;
        var data = result.Data;
        TimeValidations[serviceId] = new TimeValidationState
        {
            isValid = data.IsAvailable,
            message = data.Message,
            isChecking = false
        };
        StateHasChanged();
    }

    private void TogglePet(int serviceId, int petId)
    {
        var f = GetForm(serviceId);
        if (f.petIds.Contains(petId)) f.petIds.Remove(petId);
        else f.petIds.Add(petId);
        if (_availabilityCts.TryGetValue(serviceId, out var cts))
        {
            CancelAvailabilityCheck(cts, "đổi danh sách thú cưng");
            _availabilityCts.Remove(serviceId);
        }
        _availabilityVersion[serviceId] = _availabilityVersion.TryGetValue(serviceId, out var v) ? v + 1 : 1;
        _ = CheckAvailabilityAsync(serviceId);
        StateHasChanged();
    }

    private async Task RemoveService(int serviceId)
    {
        await JS.InvokeVoidAsync("spaCart.remove", serviceId);
        Services.RemoveAll(s => s.ServiceId == serviceId);
        _forms.Remove(serviceId);
        TimeValidations.Remove(serviceId);
        ValidationErrors.Remove(serviceId);
        StateHasChanged();
    }

    private Dictionary<int, decimal> ServiceTotals
    {
        get
        {
            var d = new Dictionary<int, decimal>();
            foreach (var s in Services)
            {
                var f = GetForm(s.ServiceId);
                var selected = GetPetsForService(s.ServiceId).Where(p => f.petIds.Contains(p.PetId)).ToList();
                decimal total = 0;
                foreach (var p in selected) total += PriceForPet(s, p);
                d[s.ServiceId] = total;
            }
            return d;
        }
    }

    private decimal TotalPrice => ServiceTotals.Values.Sum();

    private string FirstFormPayment => Services.Count > 0 ? GetForm(Services[0].ServiceId).paymentMethod : "cash";

    private IReadOnlyList<PetShop.Models.PetWithBreedPriceDto> GetPetsForService(int serviceId)
    {
        if (!_petPricingByService.TryGetValue(serviceId, out var pricing) || pricing.Count == 0)
        {
            return Pets;
        }

        if (Pets.Count == 0)
        {
            return pricing.Values.OrderBy(p => p.PetName).ToList();
        }

        return Pets
            .Select(p => pricing.TryGetValue(p.PetId, out var priced) ? MergePetPricing(p, priced) : p)
            .ToList();
    }

    private static PetShop.Models.PetWithBreedPriceDto MergePetPricing(
        PetShop.Models.PetWithBreedPriceDto pet,
        PetShop.Models.PetWithBreedPriceDto priced) =>
        pet with
        {
            BreedId = priced.BreedId ?? pet.BreedId,
            BreedName = !string.IsNullOrWhiteSpace(priced.BreedName) ? priced.BreedName : pet.BreedName,
            PriceAdjustPercent = priced.PriceAdjustPercent,
            FinalPrice = priced.FinalPrice
        };

    private decimal PriceForPet(PetShop.Models.PetServiceDto service, PetShop.Models.PetWithBreedPriceDto pet)
    {
        if (_petPricingByService.TryGetValue(service.ServiceId, out var pricing) && pricing.TryGetValue(pet.PetId, out var p))
        {
            if (p.FinalPrice is not null) return Math.Max(0, p.FinalPrice.Value);
            if (p.PriceAdjustPercent is not null) return Math.Max(0, service.Price * p.PriceAdjustPercent.Value / 100m);
        }
        if (pet.FinalPrice is not null) return Math.Max(0, pet.FinalPrice.Value);
        if (pet.PriceAdjustPercent is not null) return Math.Max(0, service.Price * pet.PriceAdjustPercent.Value / 100m);
        return service.Price;
    }

    private decimal? GetPctForPet(PetShop.Models.PetServiceDto service, PetShop.Models.PetWithBreedPriceDto pet)
    {
        if (_petPricingByService.TryGetValue(service.ServiceId, out var pricing) && pricing.TryGetValue(pet.PetId, out var p))
            return p.PriceAdjustPercent;
        return pet.PriceAdjustPercent;
    }

    private string TodayIso => DateTime.Today.ToString("yyyy-MM-dd");

    private (List<(string value, string label)> morning, List<(string value, string label)> afternoon) GetSlots(int durationMin)
    {
        var d = Math.Max(1, durationMin);
        var morning = new List<(string, string)>();
        var afternoon = new List<(string, string)>();
        for (int t = 7 * 60; t < 12 * 60; t += 30)
        {
            if (t + d <= 12 * 60)
            {
                var h = t / 60; var m = t % 60;
                var label = $"{h:00}:{m:00}";
                morning.Add((label, label));
            }
        }
        for (int t = 13 * 60; t < 18 * 60; t += 30)
        {
            if (t + d <= 18 * 60)
            {
                var h = t / 60; var m = t % 60;
                var label = $"{h:00}:{m:00}";
                afternoon.Add((label, label));
            }
        }
        return (morning, afternoon);
    }

    private static readonly Dictionary<string, string> ThumbMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["spa cao cấp"] = "/images/spa-caocap1.jpg",
        ["tắm + vệ sinh cơ bản"] = "/images/bath1.png",
        ["cắt tỉa lông chuyên nghiệp"] = "/images/cut1.jpg",
        ["vệ sinh răng miệng"] = "/images/teeth1.jpg",
    };

    private static string GetThumb(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "/images/placeholder.jpg";
        if (ThumbMap.TryGetValue(name, out var url)) return url;
        var lower = name.ToLowerInvariant();
        if (lower.Contains("tắm") || lower.Contains("bath") || lower.Contains("vệ sinh cơ bản")) return "/images/bath1.png";
        if (lower.Contains("cắt") || lower.Contains("tỉa")) return "/images/cut1.jpg";
        if (lower.Contains("spa") || lower.Contains("cao cấp")) return "/images/spa-caocap1.jpg";
        if (lower.Contains("răng") || lower.Contains("miệng")) return "/images/teeth1.jpg";
        return "/images/placeholder.jpg";
    }

    private async Task SubmitAsync()
    {
        if (isSubmitting) return;
        isSubmitting = true;
        error = null;
        var payosIds = new List<int>();
        bool payosSelected = false;
        var customerId = UserSession.CurrentUser?.UserId ?? 0;

        foreach (var service in Services)
        {
            var f = GetForm(service.ServiceId);
            if (string.IsNullOrEmpty(f.date) || string.IsNullOrEmpty(f.time))
            {
                ValidationErrors[service.ServiceId] = "Vui lòng chọn ngày và giờ.";
                isSubmitting = false;
                StateHasChanged();
                return;
            }
            if (f.petIds.Count == 0)
            {
                ValidationErrors[service.ServiceId] = "Vui lòng chọn thú cưng.";
                isSubmitting = false;
                StateHasChanged();
                return;
            }
            ValidationErrors.Remove(service.ServiceId);

            if (f.paymentMethod == "payos") payosSelected = true;
            var selected = GetPetsForService(service.ServiceId).Where(p => f.petIds.Contains(p.PetId)).ToList();
            DateTime start;
            try { start = DateTime.Parse($"{f.date}T{f.time}"); }
            catch { continue; }
            var req = new PetShop.Models.CreateSpaBookingRequest(
                selected.Select(p => p.PetId).ToList(),
                start,
                string.IsNullOrWhiteSpace(sellerNote) ? f.note : sellerNote,
                new List<PetShop.Models.SpaCartItemRequest> { new(service.ServiceId, 1) },
                f.paymentMethod);
            var createRes = await SpaClient.CreateAsync(customerId, req);
            if (!createRes.IsSuccess || createRes.Data is null)
            {
                isSubmitting = false;
                slotSuggestionModal = new SlotModalState(true, service.ServiceId, createRes.Error ?? "Khung giờ đã đủ 2 slot.", null);
                StateHasChanged();
                return;
            }
            var booking = createRes.Data;
            var ids = booking.BookingIds?.Any() == true ? booking.BookingIds : new List<int> { booking.BookingId };
            payosIds.AddRange(ids);
        }

        if (payosSelected && payosIds.Count > 0)
        {
            var firstId = payosIds[0];
            var returnUrl = Navigation.ToAbsoluteUri($"/spa-invoice?payos=success&bookingId={firstId}&bookingIds={string.Join(',', payosIds)}").ToString();
            var cancelUrl = Navigation.ToAbsoluteUri($"/spa-invoice?payos=cancel&bookingId={firstId}&bookingIds={string.Join(',', payosIds)}").ToString();
            var payRes = await SpaClient.CreateSpaPayOSPaymentAsync(firstId, payosIds, returnUrl, cancelUrl);
            isSubmitting = false;
            if (payRes.IsSuccess && payRes.Data is not null && !string.IsNullOrWhiteSpace(payRes.Data.CheckoutUrl))
            {
                await JS.InvokeVoidAsync("spaCart.clear");
                Navigation.NavigateTo(payRes.Data.CheckoutUrl, forceLoad: true);
                return;
            }
            error = payRes.Error ?? "Không nhận được link PayOS.";
            StateHasChanged();
            return;
        }

        await JS.InvokeVoidAsync("spaCart.clear");
        isSubmitting = false;
        Navigation.NavigateTo("/spa-booking-history", forceLoad: true);
    }

    private void CloseSuggestionModal() => slotSuggestionModal = new(false, null, "", null);

    private async Task ApplySuggested()
    {
        var s = slotSuggestionModal.suggestedStart;
        if (slotSuggestionModal.serviceId is int sid && !string.IsNullOrEmpty(s))
        {
            DateTime dt;
            if (DateTime.TryParse(s, out dt))
            {
                var f = GetForm(sid);
                f.date = dt.ToString("yyyy-MM-dd");
                f.time = dt.ToString("HH:mm");
                ValidationErrors.Remove(sid);
                error = null;
                if (_availabilityCts.TryGetValue(sid, out var cts))
                {
                    CancelAvailabilityCheck(cts, "áp dụng ngày gợi ý");
                    _availabilityCts.Remove(sid);
                }
                _availabilityVersion[sid] = _availabilityVersion.TryGetValue(sid, out var v) ? v + 1 : 1;
                await CheckAvailabilityAsync(sid);
            }
        }
        slotSuggestionModal = new(false, null, "", null);
    }

    private static string FormatSuggestionDisplay(string iso)
    {
        if (DateTime.TryParse(iso, out var dt))
            return dt.ToString("dd/MM/yyyy HH:mm", Vi);
        return iso;
    }

    /// <summary>
    /// Hủy và giải phóng token hủy của một lượt kiểm tra khả dụng.
    /// Bọc try/catch + log vì <c>Cancel()</c>/<c>Dispose()</c> có thể ném
    /// <see cref="ObjectDisposedException"/> nếu token đã bị giải phóng trước đó.
    /// </summary>
    private void CancelAvailabilityCheck(CancellationTokenSource cts, string context)
    {
        try
        {
            cts.Cancel();
            cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            Logger.LineWarning($"[SpaCart] CancellationTokenSource đã được giải phóng trước đó ({context}).");
        }
        catch (Exception ex)
        {
            Logger.LineError(ex, $"[SpaCart] Không huỷ được lượt kiểm tra khả dụng ({context}).");
        }
    }

    public void Dispose()
    {
        foreach (var cts in _availabilityCts.Values)
        {
            CancelAvailabilityCheck(cts, "Dispose");
        }
        _availabilityCts.Clear();
    }
    }
}
