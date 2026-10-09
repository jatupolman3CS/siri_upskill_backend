using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Api.Bootstrap;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Identity.Infrastructure.Bootstrap;
using Siri.Persistence;

namespace Siri.IntegrationTests;

/// <summary>
/// Regression for "nobody can become the first administrator of an empty production database": the classes that grant the owner every role were never
/// registered or run, so a fresh deployment had no way to reach the admin pages (categories, instructor approval, course moderation) at all. These run the
/// real composition root against a real database and prove the owner e-mail setting now does what it says — and nothing more.
/// <para>Requires Docker like every test in this collection (or the local-services recipe in <c>ExternalTestServices</c>).</para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class OwnerBootstrapIntegrationTests
{
    private readonly ContainersFixture _containers;

    public OwnerBootstrapIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    private static string NewOwnerEmail() => $"owner-{Guid.NewGuid():N}@example.test";

    /// <summary>A host configured with the given owner e-mail(s) exactly as production is (<c>Identity__Bootstrap__OwnerEmails__N</c>), migrated, with the host's
    /// own copy of the hosted service stopped so the test's service is the only one acting.</summary>
    private async Task<SiriApiFactory> StartHostAsync(params string[] ownerEmails)
    {
        var settings = new Dictionary<string, string?>();
        for (var index = 0; index < ownerEmails.Length; index++)
        {
            settings[$"Identity:Bootstrap:OwnerEmails:{index}"] = ownerEmails[index];
        }

        var factory = new SiriApiFactory(_containers, settings);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        }

        foreach (var hosted in factory.Services.GetServices<IHostedService>().OfType<OwnerBootstrapService>())
        {
            await hosted.StopAsync(CancellationToken.None);
        }

        return factory;
    }

    private static OwnerBootstrapService NewService(SiriApiFactory factory, string[] ownerEmails) =>
        new(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OwnerBootstrapOptions { OwnerEmails = ownerEmails }),
            NullLogger<OwnerBootstrapService>.Instance)
        {
            PollInterval = TimeSpan.FromMilliseconds(100),
        };

    [Fact]
    public async Task RunOnce_ConfiguredActiveOwner_GetsEveryRoleAndAnApprovedInstructorProfile()
    {
        var owner = new TestUserBuilder().WithEmail(NewOwnerEmail()).WithDisplayName("Owner (bootstrap test)");
        var bystander = new TestUserBuilder();
        await using var factory = await StartHostAsync(owner.Email);
        Guid ownerId;
        Guid bystanderId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            ownerId = (await owner.BuildAsync(scope.ServiceProvider)).Id;
            bystanderId = (await bystander.BuildAsync(scope.ServiceProvider)).Id;
        }

        var qualified = await NewService(factory, [owner.Email]).RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, qualified);
        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var rolesOfOwner = await db.Users().Where(u => u.Id == ownerId).SelectMany(u => u.Roles).Select(r => r.Name).ToListAsync();
        Assert.Equal(
            new[] { ROLE.AdminName, ROLE.InstructorName, ROLE.LearnerName, ROLE.SuperAdminName }.Order(),
            rolesOfOwner.Order());

        var profile = await db.InstructorProfiles().SingleAsync(p => p.UserId == ownerId);
        Assert.Equal(InstructorApplicationStatus.Approved, profile.Status);

        // Nobody else is elevated by someone else's configuration.
        Assert.Empty(await db.Users().Where(u => u.Id == bystanderId).SelectMany(u => u.Roles).ToListAsync());
        Assert.False(await db.InstructorProfiles().AnyAsync(p => p.UserId == bystanderId));
    }

    [Fact]
    public async Task RunOnce_CalledTwice_IsIdempotentAndAuditsTheGrantOnce()
    {
        var owner = new TestUserBuilder().WithEmail(NewOwnerEmail());
        await using var factory = await StartHostAsync(owner.Email.ToUpperInvariant()); // matching is case-insensitive
        Guid ownerId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            ownerId = (await owner.BuildAsync(scope.ServiceProvider)).Id;
        }

        var service = NewService(factory, [owner.Email]);
        await service.RunOnceAsync(CancellationToken.None);
        await service.RunOnceAsync(CancellationToken.None);

        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(4, await db.Users().Where(u => u.Id == ownerId).SelectMany(u => u.Roles).CountAsync());
        Assert.Equal(1, await db.InstructorProfiles().CountAsync(p => p.UserId == ownerId));
        Assert.Equal(
            1,
            await db.SecurityAudits().CountAsync(a => a.UserId == ownerId && a.EventType == OwnerAccountBootstrapper.AuditEventType));
    }

    [Fact]
    public async Task RunOnce_OwnerWhoNeverConfirmedTheEmail_GetsNothing()
    {
        var squatter = new TestUserBuilder().WithEmail(NewOwnerEmail()).WithUnconfirmedEmail();
        await using var factory = await StartHostAsync(squatter.Email);
        Guid squatterId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            squatterId = (await squatter.BuildAsync(scope.ServiceProvider)).Id;
        }

        var qualified = await NewService(factory, [squatter.Email]).RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, qualified);
        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Users().Where(u => u.Id == squatterId).SelectMany(u => u.Roles).ToListAsync());
        Assert.False(await db.InstructorProfiles().AnyAsync(p => p.UserId == squatterId));
    }

    [Fact]
    public async Task Service_OwnerRegistersAfterTheHostStarted_IsElevatedWithoutARestartAndThenStops()
    {
        var owner = new TestUserBuilder().WithEmail(NewOwnerEmail());
        await using var factory = await StartHostAsync(owner.Email);
        var service = NewService(factory, [owner.Email]);

        await service.StartAsync(CancellationToken.None);
        try
        {
            // The configured owner does not exist yet: the service keeps watching instead of giving up.
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            Assert.False(service.ExecuteTask!.IsCompleted);

            Guid ownerId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                ownerId = (await owner.BuildAsync(scope.ServiceProvider)).Id;
            }

            await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30));

            await using var verify = factory.Services.CreateAsyncScope();
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(4, await db.Users().Where(u => u.Id == ownerId).SelectMany(u => u.Roles).CountAsync());
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Service_NoOwnerConfigured_DoesNothingAndEndsImmediately()
    {
        await using var factory = await StartHostAsync();
        var someone = new TestUserBuilder();
        Guid someoneId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            someoneId = (await someone.BuildAsync(scope.ServiceProvider)).Id;
        }

        var service = NewService(factory, []);
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Users().Where(u => u.Id == someoneId).SelectMany(u => u.Roles).ToListAsync());
    }

    [Fact]
    public async Task TheApisCompositionRoot_RegistersTheOwnerBootstrapHostedService()
    {
        await using var factory = new SiriApiFactory(_containers);
        await using (var migrate = factory.Services.CreateAsyncScope())
        {
            await migrate.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        }

        Assert.Single(factory.Services.GetServices<IHostedService>().OfType<OwnerBootstrapService>());
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OwnerAccountBootstrapper>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<Siri.Modules.Catalog.Infrastructure.Bootstrap.OwnerInstructorProfileBootstrapper>());
    }
}
