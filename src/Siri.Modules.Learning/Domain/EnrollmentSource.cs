namespace Siri.Modules.Learning.Domain;

/// <summary>docs/DATABASE.md's "learning" section: "Source — Purchase|Gift|Admin|Corporate (P9)".
/// <see cref="Corporate"/> is a future value for the P9 B2B module (docs/DECISIONS.md D-16) — included now
/// so the column's full value set is correct from the first migration, but nothing in this scaffold pass
/// (or the codebase generally, until P9 exists) ever produces it.</summary>
public enum EnrollmentSource
{
    Purchase,
    Gift,
    Admin,
    Corporate,
}
