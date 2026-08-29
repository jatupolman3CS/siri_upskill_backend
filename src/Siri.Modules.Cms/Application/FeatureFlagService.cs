using Siri.Modules.Cms.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Application;

public sealed record FeatureFlagResponse(
    Guid Id,
    string Key,
    string Name,
    string? Description,
    bool IsEnabled,
    DateTime UpdatedAtUtc);

public sealed record UpsertFeatureFlagRequest(
    string Name,
    string? Description,
    bool IsEnabled);

public sealed class FeatureFlagService(
    IFeatureFlagRepository repository,
    IClock clock)
{
    public async Task<IReadOnlyList<FeatureFlagResponse>> GetEnabledFlagsAsync(CancellationToken cancellationToken)
    {
        var flags = await repository.GetEnabledAsync(cancellationToken).ConfigureAwait(false);
        return flags.Select(MapToResponse).ToList();
    }

    public async Task<IReadOnlyList<FeatureFlagResponse>> GetAllFlagsAsync(CancellationToken cancellationToken)
    {
        var flags = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return flags.Select(MapToResponse).ToList();
    }

    public async Task<Result<FeatureFlagResponse>> GetByKeyAsync(string key, CancellationToken cancellationToken)
    {
        var flag = await repository.GetByKeyAsync(key, cancellationToken).ConfigureAwait(false);
        if (flag is null)
        {
            return Result.Failure<FeatureFlagResponse>(DomainError.NotFound($"ไม่พบ Feature Flag: {key}"));
        }

        return Result.Success(MapToResponse(flag));
    }

    public async Task<Result<FeatureFlagResponse>> UpsertFlagAsync(
        string key,
        UpsertFeatureFlagRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetByKeyAsync(key, cancellationToken).ConfigureAwait(false);

        if (existing is not null)
        {
            existing.Update(request.Name, request.Description, request.IsEnabled, clock);
            await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success(MapToResponse(existing));
        }

        var createResult = FEATURE_FLAG.Create(key, request.Name, request.Description, request.IsEnabled, clock);
        if (createResult.IsFailure)
        {
            return Result.Failure<FeatureFlagResponse>(createResult.Error);
        }

        var newFlag = createResult.Value;
        repository.Add(newFlag);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(MapToResponse(newFlag));
    }

    public async Task<Result<FeatureFlagResponse>> ToggleFlagAsync(
        string key,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        var flag = await repository.GetByKeyAsync(key, cancellationToken).ConfigureAwait(false);
        if (flag is null)
        {
            return Result.Failure<FeatureFlagResponse>(DomainError.NotFound($"ไม่พบ Feature Flag: {key}"));
        }

        flag.Toggle(isEnabled, clock);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(MapToResponse(flag));
    }

    private static FeatureFlagResponse MapToResponse(FEATURE_FLAG f) =>
        new(f.FEATURE_FLAG_ID, f.KEY, f.NAME, f.DESCRIPTION, f.IS_ENABLED, f.UpdatedAtUtc ?? f.CreatedAtUtc);
}
