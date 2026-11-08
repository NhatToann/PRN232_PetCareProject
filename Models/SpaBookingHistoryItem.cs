using System.Text.Json.Serialization;

namespace PetShop.Models;

/// <summary>
/// Một dòng trong <c>localStorage.spaBookingHistory</c> — cùng cấu trúc với
/// <c>Projectojt/frontend/src/pages/SpaBookingHistoryPage.jsx</c> (nguồn OJT).
///
/// Tên field giữ nguyên theo nguồn (React đọc <c>item.id</c>, <c>item.bookingId</c>,
/// <c>item.serviceName</c>, <c>item.pet.petName</c>, <c>item.date</c>, <c>item.time</c>,
/// <c>item.total</c>, <c>item.status</c>, <c>item.paymentMethod</c>).
/// </summary>
public sealed class SpaBookingHistoryItem
{
    /// <summary>Id của dòng lịch sử (dùng làm route param <c>historyId</c>).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Mã booking.</summary>
    [JsonPropertyName("bookingId")]
    public int? BookingId { get; set; }

    /// <summary>Tên dịch vụ spa.</summary>
    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; set; }

    /// <summary>Tổng tiền của dòng.</summary>
    [JsonPropertyName("total")]
    public decimal Total { get; set; }

    /// <summary>Trạng thái lịch đặt.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Phương thức thanh toán ("payos" hoặc "cash").</summary>
    [JsonPropertyName("paymentMethod")]
    public string? PaymentMethod { get; set; }

    /// <summary>Ngày hẹn (chuỗi do nguồn ghi vào localStorage).</summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    /// <summary>Giờ hẹn (chuỗi do nguồn ghi vào localStorage).</summary>
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    /// <summary>Thời điểm tạo lịch đặt.</summary>
    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    /// <summary>Thú cưng đi kèm — nguồn lưu dạng <c>{ petName }</c>.</summary>
    [JsonPropertyName("pet")]
    public SpaBookingHistoryPet? Pet { get; set; }

    /// <summary>Tên thú cưng (rút gọn từ <see cref="Pet"/>).</summary>
    [JsonIgnore]
    public string? PetName => Pet?.PetName;
}

/// <summary>Thú cưng đi kèm một dòng lịch sử đặt spa.</summary>
public sealed class SpaBookingHistoryPet
{
    /// <summary>Tên thú cưng.</summary>
    [JsonPropertyName("petName")]
    public string? PetName { get; set; }
}
