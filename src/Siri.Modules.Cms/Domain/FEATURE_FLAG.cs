using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Domain;

public sealed class FEATURE_FLAG : IAuditable
{
    public Guid FEATURE_FLAG_ID { get; private set; }
    public string KEY { get; private set; } = string.Empty;
    public string NAME { get; private set; } = string.Empty;
    public string? DESCRIPTION { get; private set; }
    public bool IS_ENABLED { get; private set; }

    // ---- IAuditable ---------------------------------------------------------------------------
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc
    {
        get => CreatedAtUtc;
        set => CreatedAtUtc = value;
    }

    Guid? IAuditable.CreatedBy
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }

    DateTime? IAuditable.UpdatedAtUtc
    {
        get => UpdatedAtUtc;
        set => UpdatedAtUtc = value;
    }

    Guid? IAuditable.UpdatedBy
    {
        get => UpdatedBy;
        set => UpdatedBy = value;
    }

    private FEATURE_FLAG() { }

    public static Result<FEATURE_FLAG> Create(
        string key,
        string name,
        string? description,
        bool isEnabled,
        IClock clock)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Failure<FEATURE_FLAG>(DomainError.Validation("Key cannot be empty."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<FEATURE_FLAG>(DomainError.Validation("Name cannot be empty."));
        }

        var normalizedKey = key.Trim().ToLowerInvariant();

        return Result.Success(new FEATURE_FLAG
        {
            FEATURE_FLAG_ID = Guid.NewGuid(),
            KEY = normalizedKey,
            NAME = name.Trim(),
            DESCRIPTION = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            IS_ENABLED = isEnabled,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow,
        });
    }

    public void Update(string name, string? description, bool isEnabled, IClock clock)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            NAME = name.Trim();
        }

        DESCRIPTION = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IS_ENABLED = isEnabled;
        UpdatedAtUtc = clock.UtcNow;
    }

    public void Toggle(bool isEnabled, IClock clock)
    {
        IS_ENABLED = isEnabled;
        UpdatedAtUtc = clock.UtcNow;
    }
}
