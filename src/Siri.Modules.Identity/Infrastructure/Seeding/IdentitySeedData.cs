using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Infrastructure.Seeding;

/// <summary>
/// Builds the fixed dev/test bootstrap account list for task P0-37: one Admin (email/password from
/// configuration) plus a small, clearly-fake set of Learner/Instructor test accounts. Pure function
/// of <see cref="SeedOptions"/> — no database, clock, or hashing involved — so the data set itself is
/// unit-testable on its own (backend.md: "Unit test: domain logic ... บังคับ"); <see cref="IdentitySeeder"/>
/// is what actually persists it.
/// <para>
/// <b>Why these emails/names are unambiguous test data:</b> every non-Admin account uses the
/// <c>@example.test</c> domain — RFC 2606's IANA-reserved-for-testing TLD, the exact same convention
/// this codebase's own integration tests already use (see <c>RegisterAndConfirmEmailTests.cs</c>'s
/// <c>$"...@example.test"</c> literals) — with a <c>N.seed@</c> local-part convention, and every
/// display name carries an explicit "(Seed)"/"ทดสอบ" marker. Nobody could mistake these for real user
/// data, and no real mailbox anywhere can ever receive mail addressed to them.
/// </para>
/// <para>
/// The Admin account's email/display name/password are the only configurable pieces (a real deploy
/// may want a real staff email as the actual Admin) — see <see cref="SeedOptions"/>'s own doc comment
/// for why its fields are not eagerly validated like every other Options type in this module.
/// </para>
/// </summary>
public static class IdentitySeedData
{
    public static IReadOnlyList<SeedUserSpec> BuildUsers(SeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return
        [
            new SeedUserSpec(options.AdminEmail, "SIRI UpSkill Admin (Seed)", Role.AdminId, options.AdminPassword),

            new SeedUserSpec("learner1.seed@example.test", "ผู้เรียนทดสอบ 1 (Seed)", Role.LearnerId, options.TestUserPassword),
            new SeedUserSpec("learner2.seed@example.test", "ผู้เรียนทดสอบ 2 (Seed)", Role.LearnerId, options.TestUserPassword),
            new SeedUserSpec("learner3.seed@example.test", "ผู้เรียนทดสอบ 3 (Seed)", Role.LearnerId, options.TestUserPassword),

            new SeedUserSpec("instructor1.seed@example.test", "ผู้สอนทดสอบ 1 (Seed)", Role.InstructorId, options.TestUserPassword),
            new SeedUserSpec("instructor2.seed@example.test", "ผู้สอนทดสอบ 2 (Seed)", Role.InstructorId, options.TestUserPassword),
            new SeedUserSpec("instructor3.seed@example.test", "ผู้สอนทดสอบ 3 (Seed)", Role.InstructorId, options.TestUserPassword),
            new SeedUserSpec("instructor4.seed@example.test", "ผู้สอนทดสอบ 4 (Seed)", Role.InstructorId, options.TestUserPassword),
            new SeedUserSpec("instructor5.seed@example.test", "ผู้สอนทดสอบ 5 (Seed)", Role.InstructorId, options.TestUserPassword),
        ];
    }
}
