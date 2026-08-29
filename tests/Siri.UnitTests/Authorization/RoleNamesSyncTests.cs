using Siri.Modules.Identity.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Authorization;

/// <summary>
/// Verifies that <see cref="RoleNames"/> constants in <see cref="Siri.SharedKernel"/>
/// match <see cref="ROLE"/> constants in <c>Siri.Modules.Identity.Domain</c> exactly (task X-5).
/// If either side is altered or drifts, this test will fail immediately.
/// </summary>
public sealed class RoleNamesSyncTests
{
    [Theory]
    [InlineData(RoleNames.Learner, ROLE.LearnerName, "Learner")]
    [InlineData(RoleNames.Instructor, ROLE.InstructorName, "Instructor")]
    [InlineData(RoleNames.Admin, ROLE.AdminName, "Admin")]
    [InlineData(RoleNames.SuperAdmin, ROLE.SuperAdminName, "SuperAdmin")]
    public void RoleNames_MatchesIdentityRoleConstantsAndExpectedLiterals(
        string sharedKernelRole,
        string identityRole,
        string expectedLiteral)
    {
        Assert.Equal(expectedLiteral, sharedKernelRole);
        Assert.Equal(expectedLiteral, identityRole);
        Assert.Equal(sharedKernelRole, identityRole);
    }

    [Fact]
    public void RoleNames_AllFieldsMatchIdentityRoleNameConstants()
    {
        var sharedRoleFields = typeof(RoleNames)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetValue(null)!);

        var identityRoleFields = typeof(ROLE)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string) && f.Name.EndsWith("Name"))
            .ToDictionary(f => f.Name.Replace("Name", string.Empty), f => (string)f.GetValue(null)!);

        Assert.NotEmpty(sharedRoleFields);
        Assert.Equal(sharedRoleFields.Count, identityRoleFields.Count);

        foreach (var (roleKey, sharedValue) in sharedRoleFields)
        {
            Assert.True(identityRoleFields.TryGetValue(roleKey, out var identityValue), $"Identity ROLE is missing constant for '{roleKey}'");
            Assert.Equal(sharedValue, identityValue);
        }
    }
}
