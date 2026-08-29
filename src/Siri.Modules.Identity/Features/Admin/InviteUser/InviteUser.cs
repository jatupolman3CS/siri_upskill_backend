using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.Admin.InviteUser;

public sealed record InviteUserCommand(
    string Email,
    string DisplayName,
    string ROLE);

public sealed record InviteUserResult(
    Guid Id,
    string Email,
    string DisplayName,
    string ROLE,
    UserStatus Status,
    DateTime CreatedAtUtc);

public sealed class InviteUserValidator : AbstractValidator<InviteUserCommand>
{
    private static readonly string[] AllowedRoles = ["Learner", "Student", "Instructor", "Admin", "SuperAdmin"];

    public InviteUserValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("กรุณากรอกอีเมล")
            .EmailAddress().WithMessage("รูปแบบอีเมลไม่ถูกต้อง")
            .MaximumLength(256);

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("กรุณากรอกชื่อแสดง")
            .MaximumLength(100);

        RuleFor(x => x.ROLE)
            .NotEmpty().WithMessage("กรุณาระบุ ROLE")
            .Must(r => AllowedRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
            .WithMessage("ROLE ไม่ถูกต้อง");
    }
}

public sealed class InviteUserHandler(
    AppDbContext dbContext,
    IUserPasswordHasher passwordHasher,
    IClock clock)
{
    public async Task<Result<InviteUserResult>> HandleAsync(
        Guid adminUserId,
        InviteUserCommand command,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var trimmedEmail = command.Email.Trim();
        var normalizedEmail = trimmedEmail.ToUpperInvariant();

        var exists = await dbContext.Users()
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return Result.Failure<InviteUserResult>(DomainError.Conflict("อีเมลนี้มีบัญชีในระบบแล้ว"));
        }

        var normalizedRoleName = command.ROLE.Trim().Equals("Student", StringComparison.OrdinalIgnoreCase)
            ? "Learner"
            : command.ROLE.Trim();

        var role = await dbContext.Roles()
            .FirstOrDefaultAsync(r => r.Name.ToUpper() == normalizedRoleName.ToUpper(), cancellationToken)
            .ConfigureAwait(false);

        if (role is null)
        {
            return Result.Failure<InviteUserResult>(DomainError.Validation($"ไม่พบ ROLE '{command.ROLE}' ในระบบ"));
        }

        // Generate a random temporary password
        var tempPassword = $"Tmp!{Guid.NewGuid():N}"[..16] + "A1@";
        var throwawayUser = USER.Register(trimmedEmail, normalizedEmail, "placeholder", command.DisplayName.Trim());
        var passwordHash = passwordHasher.HashPassword(throwawayUser, tempPassword);

        var user = USER.Register(trimmedEmail, normalizedEmail, passwordHash, command.DisplayName.Trim());
        user.ConfirmEmail(clock);
        user.AssignRole(role);

        dbContext.Users().Add(user);

        var audit = SECURITY_AUDIT.Record(
            "Admin.InviteUser",
            user.Id,
            $"{{\"invitedBy\":\"{adminUserId}\",\"role\":\"{role.Name}\"}}",
            ipAddress,
            clock);
        dbContext.SecurityAudits().Add(audit);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new InviteUserResult(
            user.Id,
            user.Email,
            user.DisplayName,
            role.Name,
            user.Status,
            user.CreatedAtUtc));
    }
}
