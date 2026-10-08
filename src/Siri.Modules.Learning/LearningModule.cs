using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Infrastructure;

namespace Siri.Modules.Learning;

/// <summary>
/// Composition root for the Learning module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// <para>
/// Two entity clusters, scaffolded in parallel by different tasks/agents, both Repository+Service
/// (docs/DECISIONS.md D-17): Quiz/Assignment (<c>QUIZ</c>/<c>QUIZ_ATTEMPT</c>/<c>ASSIGNMENT</c>/
/// <c>ASSIGNMENT_SUBMISSION</c>) and Enrollment/Progress/Certificate (<c>ENROLLMENT</c>/
/// <c>EPISODE_PROGRESS</c>/<c>WATCH_EVENT</c>/<c>CERTIFICATE</c>). Each cluster's endpoint files are fully
/// self-contained (their own <c>MapGroup</c> + policy — see e.g. <c>QuizEndpoints</c>'s/
/// <c>EnrollmentEndpoints</c>'s own doc comments), so this file only ever needs one <c>services.AddScoped</c>
/// block and one <c>endpoints.Map*Endpoints()</c> line added per cluster.
/// </para>
/// </summary>
public static class LearningModule
{
    /// <summary>Registers the Learning module's services (handlers, options, infrastructure) into the container.</summary>
    public static IServiceCollection AddLearningModule(this IServiceCollection services)
    {
        // Certificate PDFs embed a QR code that must point at the REAL public site (no built-in domain).
        // Bound from the host's IConfiguration via DI so this method's signature (and every host/test that
        // calls it) stays unchanged. A missing value does not stop the host from booting — the certificate
        // PDF endpoints answer 503 certificate.public_url_not_configured and ProductionConfigurationGuard
        // refuses to start Production without it.
        services.AddOptions<CertificateOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                configuration.GetSection(CertificateOptions.SectionName).Bind(options);
                options.PublicBaseUrl = CertificateOptions.ResolvePublicBaseUrl(options.PublicBaseUrl, configuration);
            });

        // --- Quiz/Assignment cluster (Repository+Service — docs/DECISIONS.md D-17) ---
        services.AddScoped<IQuizRepository, QuizRepository>();
        services.AddScoped<QuizService>();
        services.AddScoped<IQuizAttemptRepository, QuizAttemptRepository>();
        services.AddScoped<QuizAttemptService>();
        services.AddScoped<IAssignmentRepository, AssignmentRepository>();
        services.AddScoped<AssignmentService>();
        services.AddScoped<IAssignmentSubmissionRepository, AssignmentSubmissionRepository>();
        services.AddScoped<AssignmentSubmissionService>();

        services.AddScoped<IValidator<CreateQuizRequest>, CreateQuizRequestValidator>();
        services.AddScoped<IValidator<AddQuizQuestionRequest>, AddQuizQuestionRequestValidator>();
        services.AddScoped<IValidator<AddQuizOptionRequest>, AddQuizOptionRequestValidator>();
        services.AddScoped<IValidator<StartQuizAttemptRequest>, StartQuizAttemptRequestValidator>();
        services.AddScoped<IValidator<SubmitQuizAnswerRequest>, SubmitQuizAnswerRequestValidator>();
        services.AddScoped<IValidator<CreateAssignmentRequest>, CreateAssignmentRequestValidator>();
        services.AddScoped<IValidator<UpdateAssignmentRequest>, UpdateAssignmentRequestValidator>();
        services.AddScoped<IValidator<SubmitAssignmentRequest>, SubmitAssignmentRequestValidator>();
        services.AddScoped<IValidator<GradeAssignmentSubmissionRequest>, GradeAssignmentSubmissionRequestValidator>();

        // --- Enrollment/Progress/Certificate cluster (Repository+Service — docs/DECISIONS.md D-17) ---
        services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
        services.AddScoped<EnrollmentService>();
        services.AddScoped<IEpisodeProgressRepository, EpisodeProgressRepository>();
        services.AddScoped<EpisodeProgressService>();
        services.AddScoped<IWatchEventRepository, WatchEventRepository>();
        services.AddScoped<ICertificateRepository, CertificateRepository>();
        services.AddScoped<CertificateService>();

        services.AddScoped<IValidator<CreateEnrollmentCommand>, CreateEnrollmentValidator>();
        services.AddScoped<IValidator<UpdateEnrollmentProgressCommand>, UpdateEnrollmentProgressValidator>();
        services.AddScoped<IValidator<UpsertEpisodeProgressCommand>, UpsertEpisodeProgressValidator>();
        services.AddScoped<IValidator<IssueCertificateCommand>, IssueCertificateValidator>();

        // --- Cross-module contracts ---
        services.AddScoped<Contracts.ILearningAccessContract, Infrastructure.Contracts.LearningAccessContract>();
        services.AddScoped<Catalog.Contracts.IEpisodeAccessReader, Infrastructure.Contracts.LearningAccessContract>();
        services.AddScoped<Catalog.Contracts.ILearningEnrollmentChecker, Infrastructure.Contracts.LearningAccessContract>();
        // Catalog's recount of COURSES.ENROLLMENT_COUNT asks here how many enrollments of a course count (it never reads Learning's tables).
        services.AddScoped<Catalog.Contracts.ILearningEnrollmentCounter, Infrastructure.Contracts.LearningEnrollmentCounter>();
        services.AddScoped<Contracts.ILearningAnalyticsContract, Infrastructure.Contracts.LearningAnalyticsContract>();
        services.AddScoped<Notification.Contracts.IAnnouncementRecipientResolver, Infrastructure.Contracts.AnnouncementRecipientResolver>();

        return services;
    }

    /// <summary>Maps the Learning module's minimal API endpoints onto the host's route builder.</summary>
    public static IEndpointRouteBuilder MapLearningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // --- Quiz/Assignment cluster (Repository+Service — docs/DECISIONS.md D-17) ---
        endpoints.MapQuizEndpoints();
        endpoints.MapQuizAttemptEndpoints();
        endpoints.MapAssignmentEndpoints();
        endpoints.MapAssignmentSubmissionEndpoints();

        // --- Enrollment/Progress/Certificate cluster (Repository+Service — docs/DECISIONS.md D-17) ---
        endpoints.MapEnrollmentEndpoints();
        endpoints.MapEpisodeProgressEndpoints();
        endpoints.MapCertificateEndpoints();

        return endpoints;
    }
}
