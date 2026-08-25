namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// One course inside a <see cref="BUNDLE"/> — pure composition child, constructed only through
/// <see cref="BUNDLE.AddItem"/>. Unlike every other child entity in this module, this one has no
/// surrogate id at all: docs/DATABASE.md's sketch gives it a composite primary key on
/// (<see cref="BUNDLE_ID"/>, <see cref="COURSE_ID"/>) instead — see <c>BUNDLE_ITEMConfiguration</c>.
/// </summary>
public sealed class BUNDLE_ITEM
{
    private BUNDLE_ITEM()
    {
    }

    public Guid BUNDLE_ID { get; private set; }

    /// <summary>Conceptual FK to <c>catalog.Courses.Id</c> — cross-module/schema, never a real DB FK
    /// constraint (same reasoning as <see cref="ORDER_ITEM.COURSE_ID"/>).</summary>
    public Guid COURSE_ID { get; private set; }

    internal static BUNDLE_ITEM Create(Guid bundleId, Guid courseId) =>
        new() { BUNDLE_ID = bundleId, COURSE_ID = courseId };
}
