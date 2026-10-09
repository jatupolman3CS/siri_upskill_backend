using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Email;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Features;
using Siri.Modules.Notification.Features.CreateAnnouncement;
using Siri.Modules.Notification.Features.GetCourseAnnouncements;
using Siri.Modules.Notification.Features.GetMyNotifications;
using Siri.Modules.Notification.Features.MarkNotificationRead;
using Siri.Modules.Notification.Application;
using Siri.Modules.Notification.Infrastructure;
using Siri.Modules.Notification.Infrastructure.Delivery;

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

        // The real support inbox for contact-form messages (no built-in address). Blank = messages are saved
        // but no team email is queued (see ContactOptions).
        services.AddOptions<ContactOptions>()
            .Bind(configuration.GetSection(ContactOptions.SectionName))
            .ValidateOnStart();

        // How queued notifications are delivered (Database = the Hangfire sender polls; Kafka = relay → topic → consumers, which the
        // worker host starts via AddNotificationDelivery). Bound in every host: the sender job reads Transport to know whether to stand down.
        services.AddOptions<NotificationDeliveryOptions>()
            .Bind(configuration.GetSection(NotificationDeliveryOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NotificationDeliveryOptions>, NotificationDeliveryOptionsValidator>());
        services.AddScoped<IUnreadNotificationCounter, UnreadNotificationCounter>();

        // The one delivery routine both transports run (see EmailDeliveryHandler.DeliverAsync): the Hangfire sender job and the Kafka
        // consumer share the same Redis claim, so an email is never sent twice even if both are briefly active (rolling deploy, a host
        // still on the other transport). Redis being down is tolerated — the claim fails open.
        services.AddSingleton<IEmailDeliveryGuard, RedisEmailDeliveryGuard>();
        services.AddSingleton<IEmailSendThrottle, RedisEmailSendThrottle>();
        services.AddScoped<EmailDeliveryHandler>();

        services.AddScoped<EmailOutboxSenderJob>();
        services.AddScoped<AnnouncementDispatchJob>();
        services.AddScoped<IEmailOutbox, EmailOutbox>();
        services.AddScoped<IUserNotificationOutbox, UserNotificationOutbox>();
        services.AddScoped<IEmailOutboxHealthReader, EmailOutboxHealthReader>();

        // Handlers
        services.AddScoped<CreateAnnouncementHandler>();
        services.AddScoped<GetCourseAnnouncementsHandler>();
        services.AddScoped<Features.GetInstructorAnnouncements.GetInstructorAnnouncementsHandler>();
        services.AddScoped<GetMyNotificationsHandler>();
        services.AddScoped<MarkNotificationReadHandler>();
        services.AddScoped<Features.SubmitContactMessage.SubmitContactMessageHandler>();
        services.AddScoped<Features.GetContactMessages.GetContactMessagesHandler>();
        services.AddScoped<Features.ResolveContactMessage.ResolveContactMessageHandler>();

        // Validators
        services.AddScoped<IValidator<CreateAnnouncementCommand>, CreateAnnouncementValidator>();
        services.AddScoped<IValidator<Features.SubmitContactMessage.SubmitContactMessageCommand>, Features.SubmitContactMessage.SubmitContactMessageValidator>();

        // Repositories
        services.AddScoped<Application.IUserNotificationRepository, Infrastructure.UserNotificationRepository>();
        services.AddScoped<Application.IAnnouncementRepository, Infrastructure.AnnouncementRepository>();
        services.AddScoped<Application.IContactMessageRepository, Infrastructure.ContactMessageRepository>();
        services.AddScoped<Application.IEmailOutboxRepository, Infrastructure.EmailOutboxRepository>();

        return services;
    }

    /// <summary>Maps the Notification module's minimal API endpoints onto the host's route builder.</summary>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        NotificationEndpoints.MapNotificationEndpoints(endpoints);
        return endpoints;
    }
}
