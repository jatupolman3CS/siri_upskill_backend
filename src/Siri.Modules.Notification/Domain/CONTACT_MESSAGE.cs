using Siri.SharedKernel;

namespace Siri.Modules.Notification.Domain;

/// <summary>
/// Represents a public customer inquiry submitted via the Contact form (P7-13).
/// </summary>
public sealed class CONTACT_MESSAGE
{
    private CONTACT_MESSAGE() { }

    public CONTACT_MESSAGE(
        Guid id,
        string name,
        string email,
        string subject,
        string message)
    {
        Id = id;
        Name = name.Trim();
        Email = email.Trim().ToLowerInvariant();
        Subject = subject.Trim();
        Message = message.Trim();
        Status = ContactMessageStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
        IsDeleted = false;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public ContactMessageStatus Status { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public Guid? ResolvedBy { get; private set; }
    public string? AdminNotes { get; private set; }

    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public void MarkResolved(Guid adminUserId, string? notes = null)
    {
        Status = ContactMessageStatus.Resolved;
        ResolvedAtUtc = DateTime.UtcNow;
        ResolvedBy = adminUserId;
        AdminNotes = notes?.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
        UpdatedBy = adminUserId;
    }
}
