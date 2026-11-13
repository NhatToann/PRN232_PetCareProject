using Microsoft.EntityFrameworkCore;
using PetShop.Data;
using PetShop.Interfaces;

namespace PetShop.Services
{
    /// <summary>
    /// Đối soát tự động các giao dịch PayOS đang <c>pending</c>.
    ///
    /// <para>
    /// <b>VÌ SAO CẦN:</b> PayOS gửi webhook về URL mà merchant đăng ký. Khi chạy
    /// ở môi trường local, URL đó là <c>http://localhost:5286/api/payos/webhook</c>
    /// — PayOS (máy chủ ngoài internet) <b>KHÔNG</b> gọi tới được ⇒ webhook không
    /// bao giờ tới ⇒ đơn đã trả tiền thật vẫn mãi ở trạng thái "Chờ thanh toán
    /// PayOS" ⇒ khách thấy đã chuyển khoản nhưng hệ thống báo chưa thanh toán.
    /// </para>
    ///
    /// <para>
    /// Service này chạy nền, mỗi chu kỳ:
    /// <list type="number">
    ///   <item>Lấy các payment <c>pending</c> còn mã đơn PayOS (giới hạn số lượng).</item>
    ///   <item>Hỏi PayOS (server → server) trạng thái thật của từng đơn.</item>
    ///   <item>Nếu đã thanh toán ⇒ dùng CHUNG đường xử lý của webhook
    ///   (<see cref="IPayOSService.HandleWebhookForStatusAsync"/>) để cập nhật
    ///   payment + booking + gửi email hoá đơn — không lệch logic với webhook.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>AN TOÀN:</b> không tin client — server tự tra DB và tự hỏi PayOS. Cũng không
    /// ghi đè trạng thái đã "paid". Mọi lỗi được nuốt + log để vòng lặp không chết.
    /// </para>
    /// </summary>
    public sealed class PayOSReconciliationService : BackgroundService
    {
        /// <summary>
        /// Chu kỳ quét (giây). Không quá ngắn để tránh gọi PayOS quá nhiều lần.
        /// </summary>
        private const int PollIntervalSeconds = 60;

        /// <summary>
        /// Số payment pending xử lý tối đa mỗi vòng (giới hạn tải).
        /// </summary>
        private const int MaxPaymentsPerCycle = 20;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PayOSReconciliationService> _logger;

        public PayOSReconciliationService(
            IServiceScopeFactory scopeFactory,
            ILogger<PayOSReconciliationService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Chờ một nhịp đầu để app khởi động xong (DI, DB) rồi mới quét.
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ReconcileOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[PayOSReconcile] Vòng đối soát lỗi: {Message}", ex.Message);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ReconcileOnceAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<ShopPetDatabaseContext>();
            var payOs = scope.ServiceProvider.GetRequiredService<IPayOSService>();

            var pending = await db.Payments
                .AsNoTracking()
                .Where(p =>
                    p.PaymentStatus == "pending" &&
                    p.PayosOrderCode != null &&
                    p.PayosOrderCode != 0)
                .OrderByDescending(p => p.PaymentId)
                .Take(MaxPaymentsPerCycle)
                .ToListAsync(ct);

            if (pending.Count == 0)
            {
                return;
            }

            foreach (var payment in pending)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var status = await payOs.GetPaymentStatusAsync(payment.PayosOrderCode!.Value, ct);
                    if (status is null || !status.IsPaid)
                    {
                        continue;
                    }

                    // Đã thanh toán thật ⇒ đi đúng đường của webhook.
                    var result = await payOs.HandleWebhookForStatusAsync(payment.PayosOrderCode!.Value, status, ct);
                    if (result.Success)
                    {
                        _logger.LogInformation(
                            "[PayOSReconcile] Payment #{PaymentId} (type={Type}, ref={Ref}) → Đã thanh toán.",
                            payment.PaymentId, payment.PaymentType, payment.ReferenceId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "[PayOSReconcile] Lỗi đối soát payment #{PaymentId}: {Message}",
                        payment.PaymentId, ex.Message);
                }
            }
        }
    }
}
