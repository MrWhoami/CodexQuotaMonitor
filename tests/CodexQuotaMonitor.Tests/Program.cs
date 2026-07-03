using System.Text.Json;
using CodexQuotaMonitor.Wpf;

var tests = new (string Name, Action Body)[]
{
    ("quota JSON-RPC response parsing", TestQuotaParsing),
    ("context usage row parsing", TestContextParsing),
    ("settings defaults, JSON load, CLI override, corrupt fallback", TestSettings),
    ("dynamic quota refresh scheduler", TestDynamicQuotaRefreshScheduler),
    ("formatting helpers", TestFormatting),
    ("taskbar placement", TestTaskbarPlacement),
    ("argument handling", TestArguments)
};

var passed = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
        passed++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
        return 1;
    }
}

Console.WriteLine($"{passed}/{tests.Length} tests passed");
return 0;

static void TestQuotaParsing()
{
    using var document = JsonDocument.Parse("""
        {
          "rateLimits": {
            "limitId": "test-limit",
            "limitName": "Test Limit",
            "planType": "plus",
            "primary": {
              "usedPercent": 57.25,
              "windowDurationMins": 300,
              "resetsAt": 4102444800
            },
            "secondary": {
              "usedPercent": 21,
              "windowDurationMins": 10080,
              "resetsAt": 4102448400
            }
          }
        }
        """);

    var snapshot = QuotaReader.ParseRateLimitResult(document.RootElement);
    Equal(null, snapshot.Error, "quota error");
    Equal("test-limit", snapshot.LimitId, "limit id");
    Near(42.75, snapshot.Primary!.RemainingPercent!.Value, 0.001, "primary remaining");
    Near(79.0, snapshot.Secondary!.RemainingPercent!.Value, 0.001, "secondary remaining");
}

static void TestContextParsing()
{
    var snapshot = ContextReader.TryParseContextRow(
        "event.kind=response.completed model=gpt-5 input_token_count=100 cached_token_count=20 output_token_count=5 reasoning_token_count=3 event.timestamp=2026-07-01T08:00:00Z conversation.id=abc",
        new Dictionary<string, ModelWindow>
        {
            ["gpt-5"] = new(200, 50)
        });

    Equal(null, snapshot!.Error, "context error");
    Equal("gpt-5", snapshot.Model, "context model");
    Equal(100, snapshot.InputTokens, "context input tokens");
    Equal(200, snapshot.ContextWindow, "context window");
    Equal(100, snapshot.EffectiveWindow, "context effective window");
    Near(100.0, snapshot.UsedPercent!.Value, 0.001, "context used percent");
    Near(0.0, snapshot.RemainingPercent!.Value, 0.001, "context remaining percent");
}

static void TestSettings()
{
    var tempDir = Path.Combine(Path.GetTempPath(), "codex-quota-native-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    var path = Path.Combine(tempDir, "settings.json");
    try
    {
        var defaults = SettingsStore.Load(path);
        Equal(180, defaults.QuotaInterval, "default quota interval");

        File.WriteAllText(path, """
            {
              "quota_interval": 60,
              "quota_interval_dynamic": true,
              "no_tray": true,
              "window_width": 320,
              "red_threshold": 10,
              "amber_threshold": 25
            }
            """);
        var loaded = SettingsStore.Load(path);
        Equal(60, loaded.QuotaInterval, "loaded quota interval");
        Equal(true, loaded.QuotaIntervalDynamic, "loaded dynamic quota interval");
        Equal(true, loaded.NoTray, "loaded no tray");

        var cli = CliOptions.Parse(["--quota-interval", "300", "--tray"]);
        var merged = SettingsStore.ApplyCliOverrides(loaded, cli);
        Equal(300, merged.QuotaInterval, "cli quota override");
        Equal(false, merged.QuotaIntervalDynamic, "cli quota interval disables dynamic");
        Equal(false, merged.NoTray, "cli tray override");

        File.WriteAllText(path, "{ broken json");
        var fallback = SettingsStore.Load(path);
        Equal(180, fallback.QuotaInterval, "corrupt JSON fallback");
    }
    finally
    {
        Directory.Delete(tempDir, recursive: true);
    }
}

static void TestDynamicQuotaRefreshScheduler()
{
    var scheduler = new DynamicQuotaRefreshScheduler();
    var unchanged = Quota(40, 10);

    scheduler.Register(unchanged);
    Equal(180, scheduler.CurrentIntervalSeconds, "initial dynamic interval");

    scheduler.Register(unchanged);
    scheduler.Register(unchanged);
    Equal(180, scheduler.CurrentIntervalSeconds, "two unchanged interval");

    scheduler.Register(unchanged);
    Equal(300, scheduler.CurrentIntervalSeconds, "three unchanged interval");

    scheduler.Register(unchanged);
    scheduler.Register(unchanged);
    Equal(600, scheduler.CurrentIntervalSeconds, "five unchanged interval");

    scheduler.Register(Quota(41, 10));
    Equal(180, scheduler.CurrentIntervalSeconds, "changed resets interval");

    for (var used = 42; used <= 46; used++)
    {
        scheduler.Register(Quota(used, 10));
    }
    Equal(60, scheduler.CurrentIntervalSeconds, "five changed interval");

    scheduler.Reset();
    scheduler.Register(Quota(80, 10));
    Equal(60, scheduler.CurrentIntervalSeconds, "low primary remaining interval");

    scheduler.Register(Quota(81, 10));
    Equal(180, scheduler.CurrentIntervalSeconds, "low primary remaining restores after update");

    var now = DateTimeOffset.FromUnixTimeSeconds(4_102_444_000);
    var reset = now.AddMinutes(2);
    var next = scheduler.NextRefreshAt(now, Quota(50, 20, reset.ToUnixTimeSeconds()));
    Equal(reset.ToUniversalTime(), next.ToUniversalTime(), "reset time preempts interval");
}

static void TestFormatting()
{
    Equal("abc", Formatting.Truncate("abc", 10), "truncate short");
    Equal("abcdefg...", Formatting.Truncate("abcdefghijk", 10), "truncate long");
    Equal("--", Formatting.RemainingText(null), "remaining missing");
    Equal("43", Formatting.RemainingText(42.75), "remaining percent");
}

static void TestTaskbarPlacement()
{
    var rect = new NativeMethods.RECT
    {
        Left = 0,
        Top = 1032,
        Right = 1920,
        Bottom = 1080
    };
    var placement = TaskbarPlacementCalculator.Compute(3, rect, 260, 48, 1920, 1080);
    Equal(0, placement.X, "bottom x");
    Equal(1032, placement.Y, "bottom y");
    Equal(260, placement.Width, "bottom width");
    Equal(48, placement.Height, "bottom height");
}

static void TestArguments()
{
    var options = CliOptions.Parse([
        "--check",
        "--codex-home", "C:\\CodexHome\\.codex",
        "--codex-exe", "C:\\Tools\\codex.exe",
        "--quota-interval", "600",
        "--no-tray"
    ]);
    Equal(true, options.Check, "check flag");
    Equal(false, options.Once, "once flag");
    Equal("C:\\CodexHome\\.codex", options.CodexHome, "codex home");
    Equal("C:\\Tools\\codex.exe", options.CodexExe, "codex exe");
    Equal(600, options.QuotaInterval, "quota interval");
    Equal(true, options.NoTray, "no tray");

    var tray = CliOptions.Parse(["--tray"]);
    Equal(false, tray.NoTray, "tray override");
}

static QuotaSnapshot Quota(double primaryUsed, double secondaryUsed, long? primaryResetsAt = null)
{
    return new QuotaSnapshot(
        Primary: new LimitWindow("5h", primaryUsed, 100.0 - primaryUsed, 300, primaryResetsAt),
        Secondary: new LimitWindow("Week", secondaryUsed, 100.0 - secondaryUsed, 10080));
}

static void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}

static void Near(double expected, double actual, double tolerance, string label)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}
