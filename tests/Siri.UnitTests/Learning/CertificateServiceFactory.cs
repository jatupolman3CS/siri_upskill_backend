using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning;
using Siri.Modules.Learning.Application;
using Siri.SharedKernel;

namespace Siri.UnitTests.Learning;

/// <summary>Builds a <see cref="CertificateService"/> with a configured public verify origin unless a test
/// deliberately overrides it.</summary>
internal static class CertificateServiceFactory
{
    public const string PublicBaseUrl = "https://learn.example.test";

    public static CertificateService Create(
        ICertificateRepository certificateRepository,
        IEnrollmentRepository enrollmentRepository,
        ICatalogPriceContract catalogPriceContract,
        IUserContactReader userContactReader,
        IClock clock,
        CertificateOptions? options = null) =>
        new(
            certificateRepository,
            enrollmentRepository,
            catalogPriceContract,
            userContactReader,
            Options.Create(options ?? new CertificateOptions { PublicBaseUrl = PublicBaseUrl }),
            clock);
}
