namespace CodexQuotaMonitor.Wpf;

// Keeps the adaptive interval state separate from WPF timers so the rules can be tested directly.
public sealed class DynamicQuotaRefreshScheduler
{
    public const int FastSeconds = 60;
    public const int DefaultSeconds = 180;
    public const int SlowSeconds = 300;
    public const int SlowestSeconds = 600;

    private UsageSignature? _lastUsage;
    private int _unchangedCount;
    private int _changedCount;

    public int CurrentIntervalSeconds { get; private set; } = DefaultSeconds;

    /// <summary>
    /// Clears all dynamic refresh history and restores the initial 3 minute interval.
    /// </summary>
    public void Reset()
    {
        _lastUsage = null;
        _unchangedCount = 0;
        _changedCount = 0;
        CurrentIntervalSeconds = DefaultSeconds;
    }

    /// <summary>
    /// Records one successful quota read and updates the adaptive interval from usage streaks.
    /// </summary>
    public void Register(QuotaSnapshot snapshot)
    {
        // Failed reads do not prove usage stayed the same, so they do not affect streak counters.
        if (snapshot.Error is not null)
        {
            return;
        }

        var usage = UsageSignature.From(snapshot);
        if (_lastUsage is null)
        {
            _lastUsage = usage;
            CurrentIntervalSeconds = DefaultSeconds;
            return;
        }

        if (usage.Equals(_lastUsage))
        {
            // Only successful reads with identical usage signatures count toward the backoff streak.
            _unchangedCount++;
            _changedCount = 0;
            CurrentIntervalSeconds = _unchangedCount >= 5
                ? SlowestSeconds
                : _unchangedCount >= 3
                    ? SlowSeconds
                    : DefaultSeconds;
            return;
        }

        _lastUsage = usage;
        _unchangedCount = 0;
        _changedCount++;

        CurrentIntervalSeconds = _changedCount >= 5 ? FastSeconds : DefaultSeconds;
    }

    /// <summary>
    /// Calculates the next refresh time, using the earlier of the adaptive interval or quota reset time.
    /// </summary>
    public DateTimeOffset NextRefreshAt(DateTimeOffset now, QuotaSnapshot? snapshot)
    {
        var next = now.AddSeconds(CurrentIntervalSeconds);
        var nextReset = NextResetAt(snapshot, now);
        // Reset timestamps come from Codex's quota windows and should preempt the adaptive interval.
        return nextReset.HasValue && nextReset.Value < next ? nextReset.Value : next;
    }

    /// <summary>
    /// Finds the nearest future reset timestamp from the quota windows.
    /// </summary>
    private static DateTimeOffset? NextResetAt(QuotaSnapshot? snapshot, DateTimeOffset now)
    {
        if (snapshot?.Error is not null)
        {
            return null;
        }

        DateTimeOffset? next = null;
        foreach (var resetsAt in new[] { snapshot?.Secondary?.ResetsAt })
        {
            if (!resetsAt.HasValue)
            {
                continue;
            }

            var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetsAt.Value).ToLocalTime();
            if (resetTime <= now)
            {
                continue;
            }

            if (!next.HasValue || resetTime < next.Value)
            {
                next = resetTime;
            }
        }
        return next;
    }

    private sealed record UsageSignature(
        double SecondaryUsed,
        double SecondaryRemaining,
        long? SecondaryResetsAt,
        string? RateLimitReachedType)
    {
        // UpdatedAt is intentionally excluded; it changes on every read and would break equality checks.
        /// <summary>
        /// Builds the comparable usage signature used to decide whether usage changed.
        /// </summary>
        public static UsageSignature From(QuotaSnapshot snapshot) => new(
            Normalize(snapshot.Secondary?.UsedPercent),
            Normalize(snapshot.Secondary?.RemainingPercent),
            snapshot.Secondary?.ResetsAt,
            snapshot.RateLimitReachedType);

        /// <summary>
        /// Normalizes nullable percentages so tiny floating-point noise does not create false changes.
        /// </summary>
        private static double Normalize(double? value) => Math.Round(value ?? -1.0, 3);
    }
}
