using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace PetShop.Tests;

/// <summary>
/// Bộ test bảo vệ cấu hình <b>TỐI ƯU RAM</b> của ứng dụng.
///
/// <para>
/// Bối cảnh: trong Blazor Server, service SCOPED sống suốt vòng đời circuit
/// (một tab người dùng — có thể vài giờ). Nếu scoped <c>DbContext</c> theo dõi
/// (tracking) mọi entity đã đọc, <c>ChangeTracker</c> phình dần theo thời gian
/// ⇒ RAM tăng tuyến tính ⇒ app "lag" và "phình" sau vài giờ sử dụng.
/// </para>
///
/// <para>
/// Vì vậy ứng dụng bật <see cref="QueryTrackingBehavior.NoTracking"/> làm mặc
/// định cho scoped context. Hệ quả: <b>mọi</b> đường ghi kiểu "đọc entity →
/// sửa thuộc tính → SaveChanges" <b>bắt buộc</b> phải có <c>.AsTracking()</c>
/// tường minh, nếu không thay đổi sẽ KHÔNG được ghi xuống DB.
/// </para>
///
/// <para>
/// Các test dưới đây tự động phát hiện hồi quy: quét toàn bộ file .cs trong
/// project và báo lỗi nếu phát hiện đường ghi thiếu <c>AsTracking()</c>.
/// </para>
/// </summary>
public sealed class RamOptimizationGuardTests
{
    /// <summary>
    /// Tìm thư mục gốc chứa source (project web) từ thư mục bin của test project.
    /// </summary>
    private static string FindWebProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            // Thư mục gốc workspace chứa .csproj của web app.
            if (dir.GetFiles("PetShop_PRN222.csproj").Length > 0)
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Không tìm thấy thư mục chứa PetShop_PRN222.csproj từ " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Lấy các file C# nguồn của web app (loại trừ tests, bin, obj).
    /// </summary>
    private static IEnumerable<(string File, string[] Lines)> GetSourceFiles()
    {
        var root = FindWebProjectRoot();

        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            yield return (file, File.ReadAllLines(file));
        }
    }

    // -----------------------------------------------------------------
    // 1) Cấu hình NoTracking mặc định
    // -----------------------------------------------------------------

    [Fact]
    public void ScopedDbContext_DungNoTrackingMacDinh()
    {
        // Dựng options đúng như Program.cs để kiểm tra hành vi tracking.
        var options = new DbContextOptionsBuilder<PetShop.Data.ShopPetDatabaseContext>()
            .UseSqlServer("Server=(local);Database=SHOP_PET_Database;User Id=sa;Password=123;TrustServerCertificate=true;")
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        using var db = new PetShop.Data.ShopPetDatabaseContext(options);

        Assert.Equal(QueryTrackingBehavior.NoTracking, db.ChangeTracker.QueryTrackingBehavior);
    }

    [Fact]
    public void ProgramCs_DangKyCaHaiDbContext_NoTracking()
    {
        // Cả scoped context lẫn IDbContextFactory đều phải NoTracking,
        // nếu không thì các repository đọc-only (dùng factory) vẫn phình tracker.
        var program = File.ReadAllText(
            Path.Combine(FindWebProjectRoot(), "Program.cs"));

        var noTrackingCount = Regex.Matches(
            program,
            @"UseQueryTrackingBehavior\(Microsoft\.EntityFrameworkCore\.QueryTrackingBehavior\.NoTracking\)").Count;

        Assert.True(noTrackingCount >= 2,
            $"Program.cs phải đăng ký NoTracking cho cả scoped DbContext và DbContextFactory — mới tìm thấy {noTrackingCount} chỗ.");
    }

    // -----------------------------------------------------------------
    // 2) Tất cả đường ghi phải có AsTracking (hoặc Add/Update/Remove)
    // -----------------------------------------------------------------

    [Fact]
    public void MoiDuongGhi_DeuCoAsTracking_HoacDungAddUpdateRemove()
    {
        // Regex: một "load" (First/FirstOrDefault/Single/.../Find) nằm trong vùng
        // 45 dòng trước SaveChanges, kèm một phép gán thuộc tính.
        // Nếu vùng đó KHÔNG có AsTracking và cũng KHÔNG có .Add/.Update/.Remove
        // ⇒ đường ghi phụ thuộc implicit tracking ⇒ sẽ KHÔNG được lưu khi
        //     bật NoTracking ⇒ hồi quy chức năng.
        var loadPattern = new Regex(
            @"(FirstOrDefaultAsync|FirstAsync|SingleOrDefaultAsync|SingleAsync|FindAsync|ToListAsync)\(");

        var explicitPattern = new Regex(
            @"AsTracking|\.Update\(|\.Add\(|\.Remove\(|\.AddAsync\(|\.Attach\(");

        var assignmentPattern = new Regex(
            @"^\s*\w+\.\w+\s*(\+=|-=|\*=|=)\s*[^=]",
            RegexOptions.Multiline);

        var violations = new List<string>();

        foreach (var (file, lines) in GetSourceFiles())
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("SaveChangesAsync", StringComparison.Ordinal))
                {
                    continue;
                }

                // Cửa sổ 70 dòng: một method có thể load entity ở đầu rồi SaveChanges
                // ở cuối (khoảng cách > 45 dòng nếu có nhiều nhánh xử lý).
                var start = Math.Max(0, i - 70);
                var window = string.Join("\n", lines[start..(i + 1)]);

                if (!loadPattern.IsMatch(window))
                {
                    continue; // Không có "load entity" ⇒ không phụ thuộc tracking.
                }

                if (!assignmentPattern.IsMatch(window))
                {
                    continue; // Không sửa thuộc tính ⇒ không cần tracking.
                }

                if (explicitPattern.IsMatch(window))
                {
                    continue; // Đã bảo vệ bằng AsTracking/Add/Update/Remove.
                }

                violations.Add(
                    $"{Path.GetFileName(file)}:{i + 1} — đường ghi dựa trên implicit tracking, thiếu .AsTracking()");
            }
        }

        Assert.True(violations.Count == 0,
            "Các đường ghi sau thiếu .AsTracking() (sẽ KHÔNG được ghi vào DB khi bật NoTracking):" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    // -----------------------------------------------------------------
    // 3) Cấu hình build tối ưu RAM
    // -----------------------------------------------------------------

    [Fact]
    public void DirectoryBuildProps_TatMSBuildNodeReuseVaSharedCompilation()
    {
        var propsPath = Path.Combine(
            Path.GetDirectoryName(FindWebProjectRoot())!,
            "Directory.Build.props");

        Assert.True(File.Exists(propsPath),
            "Không tìm thấy Directory.Build.props ở thư mục cha của web project.");

        var props = File.ReadAllText(propsPath);

        Assert.Contains("<MSBuildNodeReuse>false</MSBuildNodeReuse>", props, StringComparison.Ordinal);
        Assert.Contains("<UseSharedCompilation>false</UseSharedCompilation>", props, StringComparison.Ordinal);
    }

    [Fact]
    public void WebProject_GioiHanSoHeapGcServer()
    {
        var csproj = File.ReadAllText(
            Path.Combine(FindWebProjectRoot(), "PetShop_PRN222.csproj"));

        // Trên máy 12 logical core, Server GC mặc định tạo 12 heap ⇒ phí RAM.
        Assert.Contains(@"Include=""System.GC.HeapCount""", csproj, StringComparison.Ordinal);
        Assert.Contains("<ServerGarbageCollection>true</ServerGarbageCollection>", csproj, StringComparison.Ordinal);
        Assert.Contains("<ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>", csproj, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------
    // 4) Không được khởi động timer/connection lặp lại trong component
    // -----------------------------------------------------------------

    [Fact]
    public void KhongComponentNao_TaoHubConnectionRieng()
    {
        // ChatWidget + NotificationBell phải DÙNG CHUNG RealtimeHubService
        // (1 WebSocket / circuit). Nếu component nào tự new HubConnectionBuilder()
        // ⇒ mỗi circuit mở thêm 1 WebSocket ⇒ tốn RAM + độ trễ handshake.
        var offenders = new List<string>();

        foreach (var (file, lines) in GetSourceFiles())
        {
            var content = string.Join("\n", lines);
            if (content.Contains("new HubConnectionBuilder()", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        // SignalRDemo.razor là trang DEMO chủ động (tự quản lý connection) —
        // được phép; các component thật thì không.
        // RealtimeHubService.cs là nơi DUY NHẤT được phép tạo HubConnection
        // (đó chính là service dùng chung cho cả circuit).
        var realOffenders = offenders
            .Where(o => !o.Equals("SignalRDemo.razor.cs", StringComparison.OrdinalIgnoreCase))
            .Where(o => !o.Equals("RealtimeHubService.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(realOffenders.Count == 0,
            "Các component sau tự tạo HubConnection riêng (nên dùng chung RealtimeHubService): " +
            string.Join(", ", realOffenders));
    }

    // -----------------------------------------------------------------
    // 4b) RealtimeHubService: phải gán _connection TRƯỚC StartAsync
    //     (chống connection churn — 1 WebSocket/circuit)
    // -----------------------------------------------------------------

    [Fact]
    public void RealtimeHubService_GanConnection_TruocStartAsync()
    {
        // BỐI CẢNH (bug 11/11/2026):
        //   `NotificationBell` VÀ `ChatWidget` cùng nằm trong
        //   `StaffWorkspaceLayout` ⇒ cả hai cùng gọi `EnsureConnectedAsync`.
        //   Nếu `_connection` CHỈ được gán SAU `StartAsync` + `InvokeAsync`,
        //   thì lần gọi thứ hai thấy `_connection == null` ⇒ tạo connection MỚI
        //   ⇒ server log nhiều dòng "[NotificationHub] OnConnected" mà không có
        //   "JoinGroup" tương ứng ⇒ rò rỉ WebSocket, tốn RAM, circuit dễ bị đóng
        //   ⇒ client báo "Connection disconnected with error".
        //
        //   CÁCH SỬA ĐÃ ÁP DỤNG: gán `_connection` + `_joinedUserId` ngay khi
        //   tạo connection (trước `StartAsync`) ⇒ lần gọi thứ hai thấy state
        //   `Connecting` (không phải `Disconnected`) ⇒ guard trả về ⇒ 1 connection.
        var path = Path.Combine(
            FindWebProjectRoot(),
            "Services",
            "RealtimeHubService.cs");

        Assert.True(File.Exists(path), $"Không tìm thấy file {path}");

        var lines = File.ReadAllLines(path);
        var content = string.Join("\n", lines);

        var assignIndex = Array.FindIndex(lines, l =>
            l.Contains("_connection = connection", StringComparison.Ordinal));

        var startIndex = Array.FindIndex(lines, l =>
            l.Contains("await connection.StartAsync", StringComparison.Ordinal));

        Assert.True(assignIndex >= 0,
            "RealtimeHubService phải gán `_connection = connection`.");

        Assert.True(startIndex >= 0,
            "RealtimeHubService phải gọi `await connection.StartAsync(...)`.");

        Assert.True(assignIndex < startIndex,
            "RealtimeHubService phải gán `_connection = connection` TRƯỚC `StartAsync` " +
            $"(hiện tại: dòng {assignIndex + 1} vs StartAsync ở dòng {startIndex + 1}). " +
            "Nếu gán sau ⇒ NotificationBell + ChatWidget cùng gọi EnsureConnectedAsync " +
            "sẽ tạo NHIỀU connection ⇒ rò rỉ WebSocket + circuit dễ bị đóng.");

        // Guard chống churn phải chấp nhận mọi trạng thái khác `Disconnected`.
        Assert.Contains("HubConnectionState.Disconnected", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ComponentCoSubscribeEvent_PhaiCoUnsubscribe()
    {
        // Rò rỉ kinh điển của Blazor Server: subscribe vào event của service
        // scoped mà không unsubscribe ⇒ mỗi lần điều hướng lại tạo thêm handler
        // ⇒ handler mọc lên, RAM tăng, event bị gọi nhiều lần (UI lag).
        var subscribePattern = new Regex(@"^\s*\w+\.\w+\s*\+=\s*", RegexOptions.Multiline);

        var offenders = new List<string>();

        foreach (var (file, lines) in GetSourceFiles())
        {
            var name = Path.GetFileName(file);
            if (!name.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = string.Join("\n", lines);
            var subscribes = subscribePattern.Matches(content).Count;

            // Số dòng unsubscribe phải >= số dòng subscribe.
            var unsubscribes = Regex.Matches(content, @"^\s*\w+\.\w+\s*-=\s*", RegexOptions.Multiline).Count;

            if (subscribes > unsubscribes)
            {
                offenders.Add($"{name}: subscribe={subscribes}, unsubscribe={unsubscribes}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Component subscribe event nhưng không unsubscribe (rò rỉ bộ nhớ + UI lag):" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // -----------------------------------------------------------------
    // 5) Ảnh trong wwwroot không được quá nặng (RAM decode phía browser)
    // -----------------------------------------------------------------

    /// <summary>
    /// Ngưỡng: ảnh nào có chiều rộng > 1400px thì khi browser decode sẽ chiếm
    /// W*H*4 byte RAM. Ví dụ ảnh 2560x1707 ⇒ 16.7 MB RAM cho MỘT thẻ &lt;img&gt;.
    /// Nhiều thẻ trên cùng trang ⇒ vài chục MB RAM chỉ cho ảnh.
    /// </summary>
    private const int MaxImageWidth = 1400;

    /// <summary>Ngưỡng dung lượng cho file ảnh (trừ video).</summary>
    private const long MaxImageBytes = 2 * 1024 * 1024;

    [Fact]
    public void AnhTrongWwwroot_KhongQuaLon()
    {
        var imagesDir = Path.Combine(FindWebProjectRoot(), "wwwroot", "images");

        Assert.True(Directory.Exists(imagesDir),
            "Không tìm thấy thư mục wwwroot/images.");

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(imagesDir, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();

            // Video không phải bitmap ⇒ không tính vào RAM decode.
            if (ext is ".mp4" or ".mov" or ".webm" or ".ico" or ".svg")
            {
                continue;
            }

            var bytes = new FileInfo(file).Length;

            if (bytes > MaxImageBytes)
            {
                offenders.Add(
                    $"{Path.GetFileName(file)}: {bytes / 1024} KB > {MaxImageBytes / 1024} KB " +
                    "(giảm bằng scripts/optimize-images.ps1)");
            }
        }

        Assert.True(offenders.Count == 0,
            "Ảnh quá nặng trong wwwroot/images (browser phải giữ bitmap đã decode trong RAM):" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void ScriptOptimizeAnh_TonTaiVaChayDuoc()
    {
        // Script resize ảnh là công cụ bảo trì bắt buộc — phải có trong repo
        // để người khác (hoặc CI) chạy lại được sau khi thêm ảnh mới.
        var scriptPath = Path.Combine(
            Path.GetDirectoryName(FindWebProjectRoot())!,
            "scripts",
            "optimize-images.ps1");

        Assert.True(File.Exists(scriptPath),
            "Thiếu scripts/optimize-images.ps1 — dùng để resize ảnh nặng.");
    }

    // -----------------------------------------------------------------
    // 6) TỐI ƯU RAM (11/11/2026) — chặn hồi quy các thay đổi sau đây
    // -----------------------------------------------------------------

    [Fact]
    public void CircuitOptions_KhongGiuQuaNhieuCircuitDaNgat()
    {
        // BỐI CẢNH:
        //   MỖI circuit Blazor đã ngắt vẫn GIỮ NGUYÊN scope DI của nó:
        //   scoped DbContext + ChangeTracker + UserSessionService +
        //   CartStateService + RealtimeHubService + render buffer.
        //
        //   `DisconnectedCircuitMaxRetained = 100` ⇒ có thể giữ 100 circuit
        //   chết trong RAM (mỗi cái vài trăm KB – vài MB) trong suốt
        //   `DisconnectedCircuitRetentionPeriod`. Máy local của user chỉ còn
        //   ~1.6 GB RAM trống ⇒ đây là nguyên nhân chính khiến máy chậm.
        //
        //   ⇒ Giữ ở mức MẶC ĐỊNH của .NET 8 (20 circuit / 3 phút) hoặc thấp hơn.
        var program = File.ReadAllText(
            Path.Combine(FindWebProjectRoot(), "Program.cs"));

        var retained = Regex.Match(
            program, @"DisconnectedCircuitMaxRetained\s*=\s*(\d+)");

        Assert.True(retained.Success,
            "Program.cs phải cấu hình rõ DisconnectedCircuitMaxRetained.");

        var value = int.Parse(retained.Groups[1].Value);

        Assert.True(value <= 20,
            $"DisconnectedCircuitMaxRetained = {value} ⇒ giữ quá nhiều circuit đã ngắt trong RAM. " +
            "Mặc định .NET 8 là 20 — không được vượt quá (mỗi circuit giữ cả scope DI + render buffer).");

        // Thời gian giữ circuit đã ngắt cũng phải ngắn.
        var period = Regex.Match(
            program,
            @"DisconnectedCircuitRetentionPeriod\s*=\s*TimeSpan\.From(Minutes|Seconds)\((\d+)\)");

        Assert.True(period.Success,
            "Program.cs phải cấu hình rõ DisconnectedCircuitRetentionPeriod.");

        var minutes = period.Groups[1].Value == "Minutes"
            ? int.Parse(period.Groups[2].Value)
            : int.Parse(period.Groups[2].Value) / 60.0;

        Assert.True(minutes <= 3,
            $"DisconnectedCircuitRetentionPeriod = {minutes} phút ⇒ circuit chết bị giữ trong RAM quá lâu. " +
            "Không được vượt quá 3 phút (mặc định .NET 8).");
    }

    [Fact]
    public void CircuitOptions_RenderBatchKhongDuocSietQuaNho()
    {
        // ⚠️ SỬA BUG (10/11/2026) — KHÔNG được đặt nhỏ hơn mặc định (10).
        // Khi client chậm, hàng đợi render batch đầy ⇒ server CHỦ ĐỘNG ĐÓNG
        // circuit ⇒ client báo "Connection disconnected with error" ⇒ toàn bộ
        // UI Blazor Server chết, buộc user phải F5.
        var program = File.ReadAllText(
            Path.Combine(FindWebProjectRoot(), "Program.cs"));

        var match = Regex.Match(
            program, @"MaxBufferedUnacknowledgedRenderBatches\s*=\s*(\d+)");

        Assert.True(match.Success,
            "Program.cs phải cấu hình rõ MaxBufferedUnacknowledgedRenderBatches.");

        var value = int.Parse(match.Groups[1].Value);

        Assert.True(value >= 10,
            $"MaxBufferedUnacknowledgedRenderBatches = {value} < 10 (mặc định .NET 8) ⇒ " +
            "server sẽ CHỦ ĐỘNG cắt circuit khi client chậm ⇒ 'Connection disconnected'. " +
            "Không được đặt nhỏ hơn 10.");
    }

    [Fact]
    public void GcHeapHardLimit_KhongVuotQua192Mb()
    {
        // Máy local: 15.8 GB RAM nhưng chỉ còn ~1.6 GB trống (SQL Server 1.4 GB
        // + Cursor 700 MB × nhiều tiến trình + MsMpEng 368 MB).
        //
        // Trần heap cũ = 512 MB ⇒ cho phép GC nới heap tới mức máy không kham
        // nổi ⇒ OS bắt đầu swap ⇒ app lag. Nay = 192 MB.
        var csproj = File.ReadAllText(
            Path.Combine(FindWebProjectRoot(), "PetShop_PRN222.csproj"));

        // <GCHeapHardLimit>0x0C000000</GCHeapHardLimit>  = 201326592 byte = 192 MB
        var hex = Regex.Match(csproj, @"<GCHeapHardLimit>\s*0x([0-9A-Fa-f]+)\s*</GCHeapHardLimit>");
        Assert.True(hex.Success, "PetShop_PRN222.csproj phải đặt <GCHeapHardLimit>.");
        var bytes = Convert.ToInt64(hex.Groups[1].Value, 16);

        Assert.True(bytes <= 192L * 1024 * 1024,
            $"GCHeapHardLimit = {bytes / 1024 / 1024} MB — vượt quá 192 MB ⇒ heap có thể phình " +
            "trên máy chỉ còn ~1.6 GB RAM trống.");

        // runtimeconfig phải KHỚP với giá trị trong csproj (tránh mâu thuẫn).
        var runtime = Regex.Match(csproj,
            @"Include=""System\.GC\.HeapHardLimit""\s+Value=""(\d+)""");
        Assert.True(runtime.Success,
            "PetShop_PRN222.csproj phải khai báo System.GC.HeapHardLimit trong RuntimeHostConfigurationOption.");
        Assert.Equal(bytes, long.Parse(runtime.Groups[1].Value));

        // Gen0 nhỏ ⇒ GC thu hồi sớm ⇒ RSS thấp hơn.
        var gen0 = Regex.Match(csproj,
            @"Include=""System\.GC\.Gen0Size""\s+Value=""(\d+)""");
        Assert.True(gen0.Success,
            "PetShop_PRN222.csproj phải khai báo System.GC.Gen0Size.");
        Assert.True(int.Parse(gen0.Groups[1].Value) <= 32 * 1024 * 1024,
            "System.GC.Gen0Size không được vượt quá 32 MB (mặc định x64 server GC = 256 MB).");
    }

    [Fact]
    public void GrpcClient_LamSingleton_DeDungChungChannel()
    {
        // `GrpcPetCatalogClient` tạo 1 `GrpcChannel` (kết nối HTTP/2 + buffer)
        // trong constructor. Đăng ký SCOPED ⇒ mỗi circuit Blazor tạo RIÊNG
        // một channel tới chính tiến trình này ⇒ mở 10 tab = 10 kết nối HTTP/2.
        // ⇒ Phải là Singleton (channel được thiết kế để dùng lại, thread-safe).
        var program = File.ReadAllText(
            Path.Combine(FindWebProjectRoot(), "Program.cs"));

        Assert.Contains(
            "AddSingleton<PetShop.Services.GrpcPetCatalogClient>()",
            program,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "AddScoped<PetShop.Services.GrpcPetCatalogClient>()",
            program,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SessionIdleTimeout_KhongQua30Phut()
    {
        // Session data nằm trong DistributedMemoryCache (RAM server) và bị giữ
        // tới khi hết IdleTimeout, kể cả khi user đã đóng trình duyệt.
        // Mặc định .NET 8 = 8 giờ ⇒ RAM phình theo số phiên.
        var program = File.ReadAllText(
            Path.Combine(FindWebProjectRoot(), "Program.cs"));

        var match = Regex.Match(
            program, @"IdleTimeout\s*=\s*TimeSpan\.From(Minutes|Hours)\((\d+)\)");

        Assert.True(match.Success,
            "Program.cs phải cấu hình rõ Session IdleTimeout.");

        var minutes = match.Groups[1].Value == "Hours"
            ? int.Parse(match.Groups[2].Value) * 60
            : int.Parse(match.Groups[2].Value);

        Assert.True(minutes <= 30,
            $"Session IdleTimeout = {minutes} phút — dữ liệu session bị giữ trong RAM quá lâu. " +
            "Không được vượt quá 30 phút.");
    }

    [Fact]
    public void AnhTrongWwwroot_KhongVuotChieuRong1400Px()
    {
        // RAM decode phía browser = W × H × 4 byte cho MỘT thẻ <img>.
        // Ảnh rộng > 1400px ⇒ mỗi thẻ ảnh "ăn" vài MB RAM trên máy user.
        var imagesDir = Path.Combine(FindWebProjectRoot(), "wwwroot", "images");
        Assert.True(Directory.Exists(imagesDir), "Không tìm thấy wwwroot/images.");

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(imagesDir, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();

            if (ext is ".mp4" or ".mov" or ".webm" or ".ico" or ".svg")
            {
                continue;
            }

            // Đọc kích thước pixel từ header JPEG/PNG mà không cần thư viện ảnh.
            if (!TryReadPixelSize(file, out var width, out var height))
            {
                continue;
            }

            if (width > MaxImageWidth)
            {
                offenders.Add(
                    $"{Path.GetFileName(file)}: {width}x{height} (rộng > {MaxImageWidth}px) — " +
                    "chạy: dotnet run --project tools/ImageOptimizer");
            }
        }

        Assert.True(offenders.Count == 0,
            "Ảnh vượt quá " + MaxImageWidth + "px chiều rộng (RAM decode phía browser):" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void AnhTrongTrangDanhSach_PhaiCoLoadingLazy()
    {
        // TỐI ƯU RAM PHÍA BROWSER (11/11/2026):
        //   Browser giữ bitmap ĐÃ GIẢI NÉN trong RAM: W × H × 4 byte mỗi ảnh.
        //   Trang danh sách (giỏ hàng, đơn hàng, sản phẩm, spa…) có 10-30 ảnh.
        //   KHÔNG có `loading="lazy"` ⇒ browser tải + giải nén TẤT CẢ ảnh ngay
        //   ⇒ một trang danh sách có thể "ăn" hàng chục MB RAM trình duyệt.
        //
        //   `loading="lazy"` ⇒ browser chỉ tải ảnh khi sắp hiển thị ⇒ giảm mạnh
        //   RAM + băng thông. `decoding="async"` ⇒ không block thread render.
        //
        //   Danh sách file được phép thiếu lazy:
        //     - Chỉ hiển thị 1 ảnh (ảnh chính) ⇒ không cần lazy.
        //     - Avatar / logo / icon nhỏ.
        var root = FindWebProjectRoot();

        var candidates = new[]
        {
            "Components/Pages/Cart.razor",
            "Components/Pages/Orders.razor",
            "Components/Pages/Checkout.razor",
            "Components/Pages/CodConfirmation.razor",
            "Components/Pages/ProductList.razor",
            "Components/Pages/ProductInventory.razor",
            "Components/Pages/Revenue.razor",
            "Components/Pages/PetInfo.razor",
            "Components/Pages/SpaCart.razor",
            "Components/Pages/SpaBookingDetail.razor",
            "Components/Pages/SpaBookingHistory.razor",
            "Components/Pages/SpaBookingHistoryDetail.razor",
            "Components/Pages/ProductDetail.razor",
            "Components/Pages/ServiceDetail.razor",
            "Components/Shared/CartDrawer.razor",
        };

        var offenders = new List<string>();

        foreach (var relative in candidates)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                continue;
            }

            var content = File.ReadAllText(path);

            // Lấy từng thẻ <img ...> rồi kiểm tra có loading="lazy" không.
            var imgTags = Regex.Matches(content, @"<img\b[^>]*>", RegexOptions.Singleline);

            foreach (Match tag in imgTags)
            {
                var html = tag.Value;

                // Ảnh từ nguồn ngoài (unsplash/flaticon) không nằm trong wwwroot
                // ⇒ không tính (và cũng không nằm trong kho ảnh của project).
                if (html.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                    html.Contains("https://", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Ảnh chính / ảnh đơn lẻ (không có vòng lặp) — bỏ qua.
                // `fetchpriority="high"` = ảnh above-the-fold, cần tải ngay.
                if (!html.Contains("loading=\"lazy\"", StringComparison.Ordinal)
                    && !html.Contains("fetchpriority=\"high\"", StringComparison.Ordinal))
                {
                    offenders.Add($"{relative}: {TrimTag(html)}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Ảnh trong trang danh sách phải có loading=\"lazy\" để không giữ bitmap giải nén trong RAM browser:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static string TrimTag(string html)
    {
        var oneLine = html.Replace("\r", " ").Replace("\n", " ");
        return oneLine.Length <= 120 ? oneLine : oneLine[..120] + "...";
    }

    /// <summary>
    /// Đọc kích thước pixel từ header file mà KHÔNG cần thư viện giải mã ảnh
    /// (nhanh + không tốn RAM) — hỗ trợ JPEG, PNG, WebP, GIF.
    /// </summary>
    private static ushort ReadUInt16BE(BinaryReader reader)    {
        var hi = reader.ReadByte();
        var lo = reader.ReadByte();
        return (ushort)((hi << 8) | lo);
    }

    private static bool TryReadPixelSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            using var fs = File.OpenRead(path);
            using var reader = new BinaryReader(fs);

            var header = reader.ReadBytes(32);
            if (header.Length < 16)
            {
                return false;
            }

            // PNG: 8-byte signature, rồi IHDR (width/height big-endian 4 byte).
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
            {
                width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                return width > 0 && height > 0;
            }

            // WebP (RIFF....WEBP): VP8 / VP8L / VP8X.
            if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46)
            {
                if (header[12] == 0x56 && header[13] == 0x50 && header[14] == 0x38 && header[15] == 0x58)
                {
                    // VP8X: 24-bit canvas width-1 ở byte 24..26.
                    fs.Position = 24;
                    var b = reader.ReadBytes(3);
                    width = 1 + (b[0] | (b[1] << 8) | (b[2] << 16));
                    fs.Position = 27;
                    var c = reader.ReadBytes(3);
                    height = 1 + (c[0] | (c[1] << 8) | (c[2] << 16));
                    return width > 0 && height > 0;
                }

                if (header[12] == 0x56 && header[13] == 0x50 && header[14] == 0x38 && header[15] == 0x4C)
                {
                    // VP8L: 14-bit width-1 / height-1.
                    fs.Position = 21;
                    var b = reader.ReadBytes(4);
                    width = 1 + (((b[1] & 0x3F) << 8) | b[0]);
                    height = 1 + (((b[3] & 0x0F) << 10) | (b[2] << 2));
                    return width > 0 && height > 0;
                }

                // VP8 lossy: frame header.
                fs.Position = 26;
                var d = reader.ReadBytes(4);
                width = (d[1] << 8 | d[0]) & 0x3FFF;
                height = (d[3] << 8 | d[2]) & 0x3FFF;
                return width > 0 && height > 0;
            }

            // JPEG: duyệt từng segment cho tới SOFn.
            if (header[0] == 0xFF && header[1] == 0xD8)
            {
                fs.Position = 2;
                while (fs.Position < fs.Length - 9)
                {
                    int b = reader.ReadByte();
                    if (b != 0xFF) continue;

                    int marker = reader.ReadByte();
                    if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7)) continue;
                    if (marker == 0xD9 || marker == 0xDA) break;

                    var len = ReadUInt16BE(reader);
                    if (marker >= 0xC0 && marker <= 0xCF
                        && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                    {
                        reader.ReadByte(); // precision
                        height = ReadUInt16BE(reader);
                        width = ReadUInt16BE(reader);
                        return width > 0 && height > 0;
                    }

                    fs.Position += len - 2;
                }
            }

            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
