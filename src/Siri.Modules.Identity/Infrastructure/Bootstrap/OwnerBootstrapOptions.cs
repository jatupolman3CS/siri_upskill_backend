namespace Siri.Modules.Identity.Infrastructure.Bootstrap;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Identity:Bootstrap") — the platform
/// owner's email address(es). At API startup every <b>existing, Active</b> account with one of these
/// emails is given all four platform roles (Learner, Instructor, Admin, SuperAdmin) and an Approved
/// instructor profile, so the owner can use the buyer, instructor and admin areas without anyone
/// running SQL against the production database (see <see cref="OwnerAccountBootstrapper"/>).
/// <para>
/// Empty by default — nobody gets elevated unless an operator lists them. The list is a plain
/// (non-secret) setting; override it per environment with
/// <c>Identity__Bootstrap__OwnerEmails__0=owner@example.com</c>. It only ever <em>adds</em> roles: removing
/// an email later does not take roles away (use the admin users page for that).
/// </para>
/// </summary>
public sealed class OwnerBootstrapOptions
{
    public const string SectionName = "Identity:Bootstrap";

    public string[] OwnerEmails { get; set; } = [];
}
