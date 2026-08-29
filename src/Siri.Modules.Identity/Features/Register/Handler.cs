using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification.Contracts;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.Register;

/// <summary>
/// Registers a brand-new account and queues an email-confirmation message — or, if the (normalized)
/// email is already registered, does neither and returns the exact same success response anyway.
/// <para>
/// <b>Anti user-enumeration</b> (security.md's spirit — never leak account existence; the main point
/// of this feature's security review). An attacker who can tell "this email is already registered"
/// apart from "registration succeeded" can enumerate every account on the platform by trying
/// candidate addresses one at a time. So both branches below:
/// <list type="bullet">
/// <item>return the exact same <see cref="RegisterResponse"/> message and the exact same 200 status
/// — never a validation/conflict error for "already exists",</item>
/// <item>and, to also avoid a response-<em>latency</em> tell: the genuinely-new branch's dominant cost
/// by far is the real PBKDF2 password hash (ASP.NET Core Identity's hasher, ≥600k iterations per
/// security.md), so the "already exists" branch deliberately performs an equivalent throwaway hash
/// too (<see cref="BurnPasswordHashTime"/>) rather than short-circuiting immediately. This equalizes
/// the dominant cost, not every last millisecond (the new-user branch also does two extra DB writes
/// and an outbox insert) — full timing parity down to the database round-trip is a deeper hardening
/// exercise this task does not attempt; noted as a residual, minor risk.</item>
/// </list>
/// Only the genuinely-new branch writes anything: a <see cref="USER"/> row, a
/// <see cref="USER_SECURITY_TOKEN"/> (Purpose = EmailConfirmation, 24h expiry), and a queued
/// confirmation email — all added to the same <see cref="AppDbContext"/> instance and committed in
/// one <see cref="AppDbContext.SaveChangesAsync"/> call, so they succeed or fail together
/// (database.md: "การเปลี่ยนแปลงหลายตารางที่ต้อง atomic ... ต้องอยู่ใน transaction เดียว").
/// </para>
/// </summary>
public sealed class RegisterHandler(
    AppDbContext dbContext,
    IUserPasswordHasher passwordHasher,
    ISecurityTokenGenerator tokenGenerator,
    IEmailOutbox emailOutbox,
    IClock clock,
    IOptions<EmailConfirmationOptions> emailConfirmationOptions,
    ILogger<RegisterHandler> logger)
{
    private static readonly TimeSpan ConfirmationTokenLifetime = TimeSpan.FromHours(24);

    private const string SuccessMessage =
        "หากอีเมลนี้ยังไม่เคยสมัครสมาชิกมาก่อน เราได้ส่งลิงก์ยืนยันไปยังกล่องจดหมายของคุณแล้ว " +
        "กรุณาตรวจสอบอีเมล (รวมถึงโฟลเดอร์ Junk/Spam) และกดยืนยันภายใน 24 ชั่วโมง";

    public async Task<Result<RegisterResponse>> HandleAsync(RegisterCommand command, CancellationToken cancellationToken)
    {
        var trimmedEmail = command.Email.Trim();
        // Upper-invariant, matching USER.NormalizedEmail's own doc comment ("Upper-invariant form of
        // Email, used for uniqueness/lookup") — deliberately the same normalization, not a new one.
        var normalizedEmail = trimmedEmail.ToUpperInvariant();

        var alreadyExists = await dbContext.Users()
            .AsNoTracking()
            .AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyExists)
        {
            BurnPasswordHashTime(trimmedEmail, normalizedEmail, command.Password);
            return new RegisterResponse(SuccessMessage);
        }

        var user = USER.Register(
            trimmedEmail,
            normalizedEmail,
            HashPassword(trimmedEmail, normalizedEmail, command.Password),
            command.DisplayName.Trim());

        var (rawToken, tokenHash) = tokenGenerator.Generate();
        var confirmationToken = USER_SECURITY_TOKEN.Issue(
            user.Id,
            UserSecurityTokenPurpose.EmailConfirmation,
            tokenHash,
            clock.UtcNow.Add(ConfirmationTokenLifetime));

        dbContext.Users().Add(user);
        dbContext.UserSecurityTokens().Add(confirmationToken);

        var confirmationLink = BuildConfirmationLink(rawToken);
        var bodyHtml = RegisterEmailContent.Render(user.DisplayName, confirmationLink);
        emailOutbox.Enqueue(user.Email, RegisterEmailContent.Subject, bodyHtml, RegisterEmailContent.TemplateKey);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Could be the unique-index race on NormalizedEmail documented above (another request
            // registered the same email between the AnyAsync check and this insert) — or it could be
            // a genuinely unexpected failure (DB down, an FK/constraint we don't expect, ...).
            // Only the former is safe to fold into the anti-enumeration "success" response: silently
            // reporting success for a real failure would both lie to the caller (nothing was actually
            // persisted, no email was queued) and hide a real outage from anyone watching logs/metrics
            // (backend.md: exceptions for genuine system failures belong to the global handler -> 500
            // + log, never a swallowed "it's probably fine"). So re-check reality before deciding
            // which case this was.
            var emailNowExists = await dbContext.Users()
                .AsNoTracking()
                .AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken)
                .ConfigureAwait(false);

            if (!emailNowExists)
            {
                throw; // not the expected race — let the global exception handler log and 500 it
            }

            logger.LogInformation(
                "Register: unique-index race on NormalizedEmail resolved as a duplicate registration; no data written on this request, returning the standard anti-enumeration response.");

            return new RegisterResponse(SuccessMessage);
        }

        return new RegisterResponse(SuccessMessage);
    }

    /// <summary>
    /// ASP.NET Core Identity's <c>PasswordHasher&lt;TUser&gt;</c> needs a <see cref="USER"/> instance
    /// to call <see cref="IUserPasswordHasher.HashPassword"/> (its interface signature, established
    /// in P0-14 — see <c>UserPasswordHasherTests.cs</c>, which already uses this exact
    /// throwaway-instance-then-hash pattern) but does not read any of that instance's properties.
    /// Building a disposable <see cref="USER"/> purely to obtain a hash — rather than changing
    /// <see cref="USER"/>'s public API — keeps this task from touching the already-shipped P0-14
    /// domain type.
    /// </summary>
    private string HashPassword(string email, string normalizedEmail, string password)
    {
        var throwawayUser = USER.Register(email, normalizedEmail, "placeholder", "placeholder");
        return passwordHasher.HashPassword(throwawayUser, password);
    }

    private void BurnPasswordHashTime(string email, string normalizedEmail, string password) =>
        HashPassword(email, normalizedEmail, password);

    private string BuildConfirmationLink(string rawToken) =>
        $"{emailConfirmationOptions.Value.ConfirmEmailUrl}?token={Uri.EscapeDataString(rawToken)}";
}
