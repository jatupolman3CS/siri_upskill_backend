using FluentValidation;

namespace Siri.Modules.Live.Application;

/// <summary>
/// Request-shape guard for the "connect Google" call. An unsafe or missing <c>returnPath</c> is not an error — the service falls back
/// to the configured landing page — so the only rule is a size cap that stops absurd payloads.
/// </summary>
public sealed class GoogleConnectCommandValidator : AbstractValidator<GoogleConnectCommand>
{
    public const int MaxReturnPathInputLength = 1000;

    public GoogleConnectCommandValidator()
    {
        RuleFor(command => command.ReturnPath).MaximumLength(MaxReturnPathInputLength);
    }
}

/// <summary>
/// Request-shape guard for pasting a room link. Deliberately only a size cap: whether the value is a usable link (https, allowed host,
/// no user-info, ...) is decided by <see cref="MeetingLinkValidator"/>, the single source of those rules, which answers with the stable
/// <c>live.meeting_link_invalid</c> / <c>live.meeting_link_host_not_allowed</c> reasons the frontend maps to messages.
/// </summary>
public sealed class SetMeetingLinkCommandValidator : AbstractValidator<SetMeetingLinkCommand>
{
    public const int MaxMeetUrlInputLength = 2000;

    public SetMeetingLinkCommandValidator()
    {
        RuleFor(command => command.MeetUrl).MaximumLength(MaxMeetUrlInputLength);
    }
}
