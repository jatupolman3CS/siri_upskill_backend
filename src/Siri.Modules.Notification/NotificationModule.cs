using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.Integrations.Email;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Features;
using Siri.Modules.Notification.Features.CreateAnnouncement;
using Siri.Modules.Notification.Features.GetCourseAnnouncements;
using Siri.Modules.Notification.Features.GetMyNotifications;
using Siri.Modules.Notification.Features.MarkNotificationRead;
using Siri.Modules.Notification.Infrastructure;

namespace Siri.Modules.Notification;

/// <summary>
/// Composition root for the Notification module.
/// </summary>
public static class NotificationModule
{
    /// <summary>Registers the Notification module's services into the container.</summary>
    public static IServiceCollection AddNotificationModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEmailIntegration(configuration);

        services.AddScoped<EmailOutboxSenderJob>();
        services.AddScoped<IEmailOutbox, EmailOutbox>();

        // Handlers
        services.AddScoped<CreateAnnouncementHandler>();
        services.AddScoped<GetCourseAnnouncementsHandler>();
        services.AddScoped<Features.GetInstructorAnnouncements.GetInstructorAnnouncementsHandler>();
        services.AddScoped<GetMyNotificationsHandler>();
        services.AddScoped<MarkNotificationReadHandler>();

        // Validators
        services.AddScoped<IValidator<CreateAnnouncementCommand>, CreateAnnouncementValidator>();

        return services;
    }

    /// <summary>Maps the Notification module's minimal API endpoints onto the host's route builder.</summary>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        NotificationEndpoints.MapNotificationEndpoints(endpoints);
        return endpoints;
    }
}
