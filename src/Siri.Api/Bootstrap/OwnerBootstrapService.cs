using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Infrastructure.Bootstrap;
using Siri.Modules.Identity.Infrastructure.Bootstrap;

namespace Siri.Api.Bootstrap;

/// <summary>
/// Gives the platform owner(s) listed in <c>Identity:Bootstrap:OwnerEmails</c> (env <c>Identity__Bootstrap__OwnerEmails__0</c>) every platform role and an
/// Approved instructor profile — the only way to get a first Admin into an EMPTY production database without anyone running SQL by hand.
/// <para>
/// <b>Why this class exists:</b> <see cref="OwnerAccountBootstrapper"/> and <see cref="OwnerInstructorProfileBootstrapper"/> were written for exactly this, but
/// nothing ever registered or ran them, so on a fresh deployment nobody could become an administrator (no categories, no instructor approval, no moderation).
/// The composition root is the right place to join them: the two classes live in different modules and may not reference each other, so this is where the
/// identity of the owner (Identity) is handed to the instructor profile (Catalog) — the same hand-off the dev seeder uses.
/// </para>
/// <para>
/// <b>Behaviour:</b> with no owner e-mail configured it does nothing at all (no timer, no query). Otherwise it checks at start and then every
/// <see cref="PollInterval"/> until every configured owner is an <em>existing, Active</em> account that now holds the roles — so the owner can register,
/// confirm the e-mail and simply sign in again a minute later, with no restart. The rules that keep this safe live in <see cref="OwnerAccountBootstrapper"/>
/// (verified/Active accounts only, never creates an account, never touches a password, idempotent, writes an audit row only when something was granted).
/// A failure (for example the migrations not applied yet) is logged and retried; it never stops the host.
/// </para>
/// </summary>
public sealed class OwnerBootstrapService(
    IServiceScopeFactory scopeFactory,
    IOptions<OwnerBootstrapOptions> options,
    ILogger<OwnerBootstrapService> logger) : BackgroundService
{
    /// <summary>Wait between checks while a configured owner has not yet got an Active account (tests set it to zero).</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuredCount = OwnerAccountBootstrapper.NormalizeEmails(options.Value.OwnerEmails).Count;
        if (configuredCount == 0)
        {
            return;
        }

        logger.LogInformation(
            "Owner bootstrap: {Count} owner account(s) configured; roles are granted as soon as each is registered and its e-mail confirmed.",
            configuredCount);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var qualified = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                if (qualified >= configuredCount)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Typically the database is not reachable / not migrated yet. Never fatal: try again on the next tick.
                logger.LogWarning(ex, "Owner bootstrap could not run ({ExceptionType}); will retry.", ex.GetType().Name);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass: grants the roles, then approves an instructor profile for each qualifying owner. Returns how many configured owners now qualify.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var owners = await scope.ServiceProvider
            .GetRequiredService<OwnerAccountBootstrapper>()
            .EnsureAllRolesAsync(cancellationToken)
            .ConfigureAwait(false);

        if (owners.Count > 0)
        {
            await scope.ServiceProvider
                .GetRequiredService<OwnerInstructorProfileBootstrapper>()
                .EnsureApprovedAsync(owners.Select(owner => new OwnerInstructorSpec(owner.UserId, owner.DisplayName)).ToArray(), cancellationToken)
                .ConfigureAwait(false);
        }

        return owners.Count;
    }
}
