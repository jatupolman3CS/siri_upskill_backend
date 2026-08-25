using Siri.SharedKernel;

namespace Siri.Persistence.DependencyInjection;

/// <summary>
/// Bootstrap-only default <see cref="IUserContext"/> so the DI graph resolves (and
/// <see cref="Interceptors.AuditableEntityInterceptor"/> can be constructed) before the Identity
/// module exists to register a real, JWT-backed implementation. Always reports an anonymous,
/// unauthenticated caller — never a real auth mechanism.
/// Registered with <c>TryAddScoped</c> so a later, real registration (added after
/// <c>AddPersistence</c> in Program.cs, once the Identity module ships) takes precedence.
/// </summary>
internal sealed class AnonymousUserContext : IUserContext
{
    public Guid? UserId => null;

    public IReadOnlyCollection<string> Roles { get; } = [];

    public bool IsAuthenticated => false;
}
