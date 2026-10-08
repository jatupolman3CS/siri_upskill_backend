namespace Siri.Modules.Catalog.Features.SetCourseLiveSettings;

/// <summary>
/// Body of <c>PUT /api/catalog/instructor/courses/{courseId}/live-settings</c> (docs/contracts/P11-04-live-invites-ics-reminders.md §6.1).
/// <see cref="GoogleAttendeeSyncEnabled"/> is nullable so that an omitted field is a validation error rather than a silent
/// "turn it off" — the setting sends learners' e-mail addresses to Google, so it is only ever changed on an explicit value.
/// </summary>
public sealed record SetCourseLiveSettingsCommand(bool? GoogleAttendeeSyncEnabled);
