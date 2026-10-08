namespace Siri.Modules.Notification;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> (<c>Notification:Contact</c>).
/// <para>
/// <see cref="SupportEmail"/> is the real inbox the team is emailed at when someone submits the contact form.
/// There is no built-in address: while it is blank the message is still saved (staff can read it in the admin
/// contact list) but no email is queued, and a warning is logged — rather than mailing an address nobody
/// confirmed exists.
/// </para>
/// </summary>
public sealed class ContactOptions
{
    public const string SectionName = "Notification:Contact";

    public string SupportEmail { get; set; } = string.Empty;
}
