namespace Siri.SharedKernel;

/// <summary>
/// Centralized role names for authorization checks across all modules (task X-5).
/// Defines canonical string constants for the four platform-defined roles:
/// <see cref="Learner"/>, <see cref="Instructor"/>, <see cref="Admin"/>, and <see cref="SuperAdmin"/>.
/// Kept in <see cref="Siri.SharedKernel"/> so any module can reference role names safely
/// without raw magic strings or circular dependencies on <c>Siri.Modules.Identity</c>.
/// </summary>
public static class RoleNames
{
    public const string Learner = "Learner";

    public const string Instructor = "Instructor";

    public const string Admin = "Admin";

    public const string SuperAdmin = "SuperAdmin";
}
