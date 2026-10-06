using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.AnonymizeAccount;

/// <summary>
/// Handles account anonymization and deletion per PDPA Right to Erasure (P7-04).
/// Replaces PII with synthetic values, revokes all sessions, and marks status as Deleted.
/// </summary>
public sealed class AnonymizeAccountHandler(
    AppDbContext dbContext,
    IClock clock,
    IUserPasswordHasher passwordHasher,
    ISessionRegistry sessionRegistry,
    ILogger<AnonymizeAccountHandler> logger)
{
    private const string RevokeReason = "pdpa_account_anonymized";
    private const string AuditEventType = "account.anonymized_pdpa";
    private const string SuccessMessage = "บัญชีของคุณถูกลบและทำให้นิรนามเรียบร้อยแล้วตามสิทธิ์ PDPA";

    public async Task<Result<AnonymizeAccountResponse>> HandleAsync(
        AnonymizeAccountCommand command,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users()
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure<AnonymizeAccountResponse>(DomainError.NotFound($"USER {command.UserId} not found."));
        }

        if (user.Status == UserStatus.Deleted)
        {
            return Result.Failure<AnonymizeAccountResponse>(
                DomainError.Conflict("Account is already deleted and anonymized."));
        }

        if (!string.IsNullOrWhiteSpace(command.Password))
        {
            var verificationResult = passwordHasher.VerifyPassword(user, user.PasswordHash, command.Password);
            if (verificationResult == PasswordVerificationResult.Failed)
            {
                return Result.Failure<AnonymizeAccountResponse>(
                    DomainError.Validation("รหัสผ่านไม่ถูกต้อง"));
            }
        }

        var anonymizedEmail = $"anonymized_{user.Id:N}@deleted.siriupskill.com";
        var unmatchableHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        user.Anonymize(anonymizedEmail, "Deleted USER", unmatchableHash);

        var activeSessions = await dbContext.UserSessions()
            .Where(s => s.UserId == command.UserId && s.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var sessionIds = activeSessions.Select(s => s.Id).ToHashSet();

        var activeTokens = await dbContext.RefreshTokens()
            .Where(t => sessionIds.Contains(t.SessionId) && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var session in activeSessions)
        {
            session.Revoke(RevokeReason, clock);
        }

        foreach (var token in activeTokens)
        {
            token.Revoke(null, clock);
        }

        // Linked sign-in providers hold the provider's email and subject — personal data that is neither
        // money nor entitlement, so (unlike orders/enrollments) it is erased outright rather than kept.
        var externalLogins = await dbContext.UserExternalLogins()
            .Where(l => l.UserId == command.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.UserExternalLogins().RemoveRange(externalLogins);

        var auditDetail = $"{{\"userId\":\"{user.Id}\",\"status\":\"Deleted\",\"action\":\"PDPA Anonymize\"}}";
        dbContext.SecurityAudits().Add(SECURITY_AUDIT.Record(AuditEventType, user.Id, auditDetail, ipAddress, clock));

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var sessionId in sessionIds)
        {
            await sessionRegistry.RemoveAsync(command.UserId, sessionId, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("PDPA Anonymize: USER {UserId} successfully anonymized and deleted.", command.UserId);

        return Result.Success(new AnonymizeAccountResponse(SuccessMessage));
    }
}
