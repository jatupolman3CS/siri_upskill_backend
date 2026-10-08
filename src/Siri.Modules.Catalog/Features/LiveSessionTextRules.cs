using FluentValidation;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features;

/// <summary>
/// Validation rule for the free-text fields of a live session (title, description, cancel reason). Those are shown on the PUBLIC course page and mailed to
/// every learner, so a room link pasted into one would hand the capability URL to anyone — the room link belongs in the Live module's meeting-link
/// endpoint, behind the join gate. Rejects with <see cref="MeetingLinkText.ContainsLinkReason"/> as the FluentValidation error code
/// (<c>ValidationActionFilter</c> turns a dotted error code into the response's stable <c>reason</c>). The detection is
/// <see cref="MeetingLinkText"/> (SharedKernel) — shared with the Live module, so Catalog needs no reference to it.
/// </summary>
internal static class LiveSessionTextRules
{
    public static IRuleBuilderOptions<T, string?> NoMeetingLink<T>(this IRuleBuilder<T, string?> ruleBuilder) =>
        ruleBuilder
            .Must(text => !MeetingLinkText.ContainsLink(text))
            .WithErrorCode(MeetingLinkText.ContainsLinkReason)
            .WithMessage("{PropertyName} must not contain a meeting-room link (Google Meet, Zoom, Teams); add the room link through the meeting-link setting instead.");
}
