using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging;

namespace PetShop.Services;

/// <summary>
/// Theo dõi vòng đời của từng circuit Blazor Server (1 circuit = 1 tab người dùng).
///
/// <para>
/// <b>Vì sao cần (chẩn đoán lỗi "Connection disconnected"):</b>
/// Khi circuit bị đóng, Blazor Server <b>KHÔNG</b> ghi log mặc định ở mức
/// <c>Information</c> ⇒ rất khó biết vì sao UI chết. Lỗi client-side thường thấy là:
///
/// <code>
/// Error: Connection disconnected with error 'Error: Server returned an error on close:
/// Connection closed with an error.'
/// </code>
///
/// Lỗi này xảy ra khi server chủ động đóng circuit, thường do:
/// <list type="bullet">
///   <item>hàng đợi render batch đầy (client chậm) — xem
///         <c>CircuitOptions.MaxBufferedUnacknowledgedRenderBatches</c>;</item>
///   <item>circuit bị loại khỏi bộ nhớ đệm khi reconnect (sau khi người dùng
///         F5 hoặc đổi trang nhiều lần).</item>
/// </list>
///
/// Handler này ghi lại thời điểm mở/đóng circuit ⇒ khi user báo "nút bấm không
/// phản hồi", đọc log là biết ngay có bao nhiêu circuit bị đóng và đóng vào lúc nào.
///
/// <para>
/// <b>Không</b> thay đổi hành vi runtime — chỉ log thêm.
/// </para>
/// </summary>
public sealed class CircuitDiagnosticsHandler : CircuitHandler
{
    private readonly ILogger<CircuitDiagnosticsHandler> _logger;

    /// <summary>Số circuit đang sống (được cập nhật theo thời gian thực).</summary>
    private static int _liveCircuits;

    public CircuitDiagnosticsHandler(ILogger<CircuitDiagnosticsHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>Số circuit hiện đang hoạt động (dùng để chẩn đoán rò rỉ circuit).</summary>
    public static int LiveCircuits => Volatile.Read(ref _liveCircuits);

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var count = Interlocked.Increment(ref _liveCircuits);
        _logger.Line($"[Circuit] Mở circuit {circuit.Id} (đang sống: {count}).");
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.Line($"[Circuit] Kết nối lại thành công cho circuit {circuit.Id}.");
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        // Mất kết nối KHÔNG đồng nghĩa circuit chết — Blazor sẽ thử reconnect
        // trong vòng DisconnectedCircuitRetentionPeriod. Ghi mức Debug để không
        // làm nhiễu log.
        _logger.LogDebug("[Circuit] Mất kết nối circuit {CircuitId} — đang thử reconnect.", circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var count = Interlocked.Decrement(ref _liveCircuits);
        _logger.Line($"[Circuit] Đóng circuit {circuit.Id} (đang sống: {count}).");
        return Task.CompletedTask;
    }
}
