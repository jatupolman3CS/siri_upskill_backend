using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.Integrations.Email;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Infrastructure;

namespace Siri.Modules.Notification;

/// <summary>
/// Composition root for the Notification module. Everything the module exposes to <c>Siri.Api</c>
/// (and, for the outbox sender job, to <c>Siri.Workers</c>) goes through this type — no other public
/// surface is wired into the host.
/// </summary>
public static class NotificationModule
{
    /// <summary>Registers the Notification module's services (email provider selection, the outbox
    /// sender job) into the container. Takes <paramref name="configuration"/> — unlike the other,
    /// still-empty module stubs — because it needs to bind <see cref="SmtpEmailSenderOptions"/> and
    /// read the <c>Email:Provider</c> switch (see <see cref="EmailServiceCollectionExtensions.AddEmailIntegration"/>).</summary>
    public static IServiceCollection AddNotificationModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEmailIntegration(configuration);

        // Scoped: the job depends on AppDbContext (scoped) — Hangfire's ASP.NET Core job activator
        // resolves it from a fresh DI scope per execution, same lifetime story as a request handler.
        services.AddScoped<EmailOutboxSenderJob>();

        // This module's Contracts/ surface (P0-15: Identity's Register handler is the first
        // consumer, reusing the outbox instead of building a second queuing mechanism).
        services.AddScoped<IEmailOutbox, EmailOutbox>();

        return services;
    }

    /// <summary>Maps the Notification module's minimal API endpoints onto the host's route builder.
    /// Empty for now — no HTTP-facing feature has landed yet (P0-19 only builds the outbox
    /// mechanism the later email-driven features plug into).</summary>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints;
    }
}
