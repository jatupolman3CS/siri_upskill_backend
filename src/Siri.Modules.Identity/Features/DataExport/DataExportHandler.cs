using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.DataExport;

/// <summary>
/// Handles user personal data export per PDPA Right to Data Portability (P7-04).
/// Reads user profile, active/recent sessions, and security audits, packaging them
/// into a structured data payload.
/// </summary>
public sealed class DataExportHandler(AppDbContext dbContext, IClock clock)
{
    public async Task<Result<DataExportResponse>> HandleAsync(DataExportCommand command, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users()
            .AsNoTracking()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure<DataExportResponse>(DomainError.NotFound($"User {command.UserId} not found."));
        }

        var sessions = await dbContext.UserSessions()
            .AsNoTracking()
            .Where(s => s.UserId == command.UserId)
            .OrderByDescending(s => s.LastSeenAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var audits = await dbContext.SecurityAudits()
            .AsNoTracking()
            .Where(a => a.UserId == command.UserId)
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var profileDto = new UserExportDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.PhoneNumber,
            user.AvatarUrl,
            user.Status.ToString(),
            user.TwoFactorEnabled,
            user.CreatedAtUtc,
            user.Roles.Select(r => r.Name).ToList());

        var sessionDtos = sessions.Select(s => new UserSessionExportDto(
            s.Id,
            s.DeviceId,
            s.DeviceName,
            s.UserAgent,
            s.IpAddress,
            s.CreatedAtUtc,
            s.LastSeenAtUtc,
            s.IsActive)).ToList();

        var auditDtos = audits.Select(a => new SecurityAuditExportDto(
            a.EventType,
            a.OccurredAtUtc,
            a.IpAddress)).ToList();

        var response = new DataExportResponse(
            clock.UtcNow,
            profileDto,
            sessionDtos,
            auditDtos);

        return Result.Success(response);
    }
}
