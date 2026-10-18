// SPDX-License-Identifier: MIT
namespace Riegel.Tests.TestSupport;

/// <summary>Uhr mit fester Zeit für deterministische Tests (z. B. <c>RiegelServices.Create(platform, new FixedClock(...))</c>).</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    /// <summary>2026-10-18T09:00:00Z, der Auswertungstag der Beispieldaten.</summary>
    public static DateTimeOffset Default { get; } = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    public FixedClock() : this(Default) { }

    public DateTimeOffset Now { get; private set; } = now.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <summary>Uhr vorstellen.</summary>
    public void Advance(TimeSpan by) => Now = Now.Add(by);
}
