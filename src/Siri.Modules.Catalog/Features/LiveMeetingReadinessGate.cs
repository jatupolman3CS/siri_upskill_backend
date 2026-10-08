using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features;

/// <summary>
/// Publish gate for Live/Hybrid courses (task P11-03, docs/contracts/P11-03-live-module-google-meetings.md §4.3):
/// every <em>future, scheduled</em> session must already have a usable online room before the course can enter
/// review or be approved — otherwise learners could buy a course whose classes have nowhere to happen.
/// <para>
/// It lives in the two handlers' pipeline (shared through this helper) and not in <c>COURSE.Publish</c> because the
/// room state belongs to <c>Siri.Modules.Live</c> — another aggregate in another module — which the domain must not
/// know about; these two handlers are the only places a course becomes sellable. Deliberately <b>not</b> enforced at
/// checkout: a published course whose instructor adds a class later (or whose Google token expires) must not stop
/// selling; the join gate answers 503 and the instructor is alerted instead.
/// </para>
/// <para>
/// Needs <see cref="COURSE.LiveSessions"/> loaded (callers <c>.Include</c> it). Past and cancelled sessions never
/// count. An <see cref="DeliveryFormat.OnDemand"/> course never reaches the reader.
/// </para>
/// </summary>
public static class LiveMeetingReadinessGate
{
    public const string NotReadyReason = "live.meetings_not_ready";

    public static async Task<Result> ValidateAsync(
        COURSE course,
        ILiveMeetingReadinessReader readiness,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(course);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(clock);

        if (course.DeliveryFormat == DeliveryFormat.OnDemand)
        {
            return Result.Success();
        }

        var now = clock.UtcNow;
        var futureSessions = course.LiveSessions
            .Where(s => s.Status == CourseLiveSessionStatus.Scheduled && s.StartsAtUtc > now)
            .OrderBy(s => s.StartsAtUtc)
            .ThenBy(s => s.Id)
            .Select(s => s.Id)
            .ToArray();

        if (futureSessions.Length == 0)
        {
            return Result.Success();
        }

        var notReady = (await readiness.GetSessionsWithoutUsableMeetingAsync(futureSessions, cancellationToken).ConfigureAwait(false))
            .ToHashSet();
        if (notReady.Count == 0)
        {
            return Result.Success();
        }

        // Report in schedule order so the instructor sees the earliest affected class first.
        var orderedNotReady = futureSessions.Where(notReady.Contains).ToArray();

        return Result.Failure(
            DomainError.Validation($"คาบสอนสด {orderedNotReady.Length} คาบยังไม่มีลิงก์ห้องประชุมที่ใช้งานได้ — เชื่อม Google Calendar หรือวางลิงก์ห้องประชุมให้ครบก่อนส่งตรวจสอบหรืออนุมัติคอร์ส")
                .WithReason(NotReadyReason, new Dictionary<string, object?> { ["sessionIds"] = orderedNotReady }));
    }
}
