using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Application;

/// <summary>
/// The pure decision "what can the platform do about recordings for this instructor" (P11-13 contract section 6). No I/O, no clock — a function of the feature
/// flag, the Live provider mode and the instructor's Google account row, so it is cheap to evaluate for a whole page of sessions and trivially unit-testable.
/// <list type="bullet">
/// <item><c>Manual</c> — the feature is off, or rooms are manual-only, or there is no connected/active account, or the account is <c>Personal</c> or still <c>Unknown</c>.</item>
/// <item><c>AutoNeedsConsent</c> — a <c>Workspace</c> account that has not granted both recording scopes.</item>
/// <item><c>Auto</c> — a <c>Workspace</c> account with both recording scopes.</item>
/// </list>
/// <para>
/// <b>Provider mode:</b> the contract says "the provider is not GoogleMeet". <see cref="LiveProviderMode.Logging"/> is the development stand-in for
/// <see cref="LiveProviderMode.GoogleMeet"/> (its fake OAuth/Meet/recording providers exist precisely so the whole flow can be exercised without Google), so only
/// <see cref="LiveProviderMode.ManualOnly"/> — never any Google call — switches the capability off.
/// </para>
/// </summary>
public static class RecordingCapabilityCalculator
{
    public static RecordingCapability Compute(bool autoImportEnabled, LiveProviderMode providerMode, INSTRUCTOR_GOOGLE_ACCOUNT? account)
    {
        if (!autoImportEnabled || providerMode == LiveProviderMode.ManualOnly)
        {
            return RecordingCapability.Manual;
        }

        if (account is not { IsActive: true })
        {
            return RecordingCapability.Manual;
        }

        if (account.AccountKind != GoogleAccountKind.Workspace)
        {
            return RecordingCapability.Manual;
        }

        return account.HasRecordingScopes ? RecordingCapability.Auto : RecordingCapability.AutoNeedsConsent;
    }

    /// <summary>The <c>recording</c> member of the Google connection status.</summary>
    public static RecordingCapabilityInfo ToInfo(bool autoImportEnabled, LiveProviderMode providerMode, INSTRUCTOR_GOOGLE_ACCOUNT? account) =>
        new(
            Compute(autoImportEnabled, providerMode, account),
            autoImportEnabled,
            ScopesGranted: account is { IsActive: true } && account.HasRecordingScopes);
}
