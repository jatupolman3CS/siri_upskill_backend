using System.Reflection;

namespace Siri.ArchitectureTests;

/// <summary>
/// Central catalog of the assemblies these architecture rules check, keyed by root namespace, so
/// adding a new module/project means updating one place instead of hunting through every rule.
/// </summary>
internal static class ModuleAssemblyCatalog
{
    public sealed record ModuleInfo(string RootNamespace, Assembly Assembly);

    public static readonly IReadOnlyList<ModuleInfo> Modules =
    [
        new("Siri.Modules.Identity", typeof(Siri.Modules.Identity.IdentityModule).Assembly),
        new("Siri.Modules.Catalog", typeof(Siri.Modules.Catalog.CatalogModule).Assembly),
        new("Siri.Modules.Media", typeof(Siri.Modules.Media.MediaModule).Assembly),
        new("Siri.Modules.Learning", typeof(Siri.Modules.Learning.LearningModule).Assembly),
        new("Siri.Modules.Commerce", typeof(Siri.Modules.Commerce.CommerceModule).Assembly),
        new("Siri.Modules.Payout", typeof(Siri.Modules.Payout.PayoutModule).Assembly),
        new("Siri.Modules.Cms", typeof(Siri.Modules.Cms.CmsModule).Assembly),
        new("Siri.Modules.Community", typeof(Siri.Modules.Community.CommunityModule).Assembly),
        new("Siri.Modules.Notification", typeof(Siri.Modules.Notification.NotificationModule).Assembly),
        new("Siri.Modules.Analytics", typeof(Siri.Modules.Analytics.AnalyticsModule).Assembly),
    ];

    /// <summary>Every project assembly except Siri.Persistence, keyed by root namespace / project name.</summary>
    public static readonly IReadOnlyDictionary<string, Assembly> NonPersistenceAssemblies = BuildNonPersistenceAssemblies();

    private static IReadOnlyDictionary<string, Assembly> BuildNonPersistenceAssemblies()
    {
        var assemblies = Modules.ToDictionary(m => m.RootNamespace, m => m.Assembly);

        assemblies["Siri.SharedKernel"] = typeof(Siri.SharedKernel.Result).Assembly;
        assemblies["Siri.Api"] = typeof(Program).Assembly;
        assemblies["Siri.Workers"] = typeof(Siri.Workers.RecurringJobsRegistration).Assembly;
        assemblies["Siri.Integrations.Video"] = typeof(Siri.Integrations.Video.IVideoProvider).Assembly;
        assemblies["Siri.Integrations.Payment"] = typeof(Siri.Integrations.Payment.IPaymentVerifier).Assembly;
        assemblies["Siri.Integrations.Storage"] = typeof(Siri.Integrations.Storage.IFileStorage).Assembly;
        assemblies["Siri.Integrations.Email"] = typeof(Siri.Integrations.Email.IEmailSender).Assembly;

        return assemblies;
    }
}
