using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// A discount code redeemable against an <see cref="ORDER"/>, scoped to all courses, a single category,
/// course, or bundle (see <see cref="SCOPE"/>/<see cref="SCOPE_REF_ID"/>).
/// <para>
/// Naming: SCREAMING_SNAKE_CASE class/properties, DB table/columns — the same brand-new-module
/// exception documented in full on <see cref="ORDER"/>'s doc comment (docs/DECISIONS.md D-17).
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.IAuditable"/>/<see cref="Siri.Persistence.Conventions.ISoftDelete"/>:
/// docs/DATABASE.md's column sketch for this table has no Created/UpdatedAtUtc at all —
/// <see cref="STARTS_AT_UTC"/>/<see cref="ENDS_AT_UTC"/> are the business-meaningful window, and
/// <see cref="IS_ACTIVE"/> is the soft-disable switch (an admin turning a code off is a status flag, not
/// a delete).
/// </para>
/// <para>
/// <b>Deliberately no in-memory redemption-count method</b>: database.md is explicit — "นับโควตา (promo
/// code, ที่นั่ง) ต้อง atomic ที่ระดับ SQL ห้ามอ่านมาเช็คใน memory แล้วค่อยเขียน". A domain method that
/// incremented <see cref="REDEEMED_COUNT"/> on this in-memory instance and relied on a normal
/// <c>SaveChangesAsync</c> to persist it would be exactly the read-then-write race the rule forbids
/// (two concurrent redemptions could both read the same count and both "succeed" past
/// <see cref="MAX_REDEMPTIONS"/>). The real redemption increment belongs to a raw
/// <c>UPDATE ... SET REDEEMED_COUNT = REDEEMED_COUNT + 1 WHERE ... AND REDEEMED_COUNT &lt; MAX_REDEMPTIONS</c>
/// at the repository layer instead — see <c>IPromoCodeRepository.TryRedeemAsync</c>'s doc comment, left
/// as a stub for whoever implements the real checkout flow.
/// </para>
/// </summary>
public sealed class PROMO_CODE
{
    private PROMO_CODE()
    {
    }

    public Guid PROMO_CODE_ID { get; private set; }
    public string CODE { get; private set; } = string.Empty;
    public PromoCodeDiscountType DISCOUNT_TYPE { get; private set; }

    /// <summary>A THB amount when <see cref="DISCOUNT_TYPE"/> is <see cref="PromoCodeDiscountType.Fixed"/>,
    /// or a 0-100 percentage when <see cref="PromoCodeDiscountType.Percentage"/> — which interpretation
    /// applies is entirely driven by <see cref="DISCOUNT_TYPE"/>, this column does not encode it itself.</summary>
    public decimal DISCOUNT_VALUE { get; private set; }

    public int MAX_REDEMPTIONS { get; private set; }

    /// <summary>Only ever changed via the atomic repository-level update described in this class's own
    /// doc comment — never assign to this property directly from application code.</summary>
    public int REDEEMED_COUNT { get; private set; }

    public int MAX_PER_USER { get; private set; }
    public decimal MIN_ORDER_AMOUNT { get; private set; }
    public DateTime STARTS_AT_UTC { get; private set; }
    public DateTime ENDS_AT_UTC { get; private set; }
    public PromoCodeScope SCOPE { get; private set; }

    /// <summary>Conceptual FK, meaning depends on <see cref="SCOPE"/>: a <c>catalog.Categories.Id</c>,
    /// <c>catalog.Courses.Id</c>, or this module's own <see cref="BUNDLE"/>.<see cref="BUNDLE.BUNDLE_ID"/>.
    /// <c>null</c> when <see cref="SCOPE"/> is <see cref="PromoCodeScope.AllCourses"/>. Polymorphic and
    /// partly cross-module, so — same as every other polymorphic/cross-module reference in this scaffold
    /// pass — never a real DB FK constraint.</summary>
    public Guid? SCOPE_REF_ID { get; private set; }

    public bool IS_ACTIVE { get; private set; }

    /// <summary>EF concurrency token (SQL Server <c>rowversion</c>) — this table is read-modify-written
    /// by admins editing terms while redemptions may be happening concurrently, same reasoning
    /// <c>Course.RowVersion</c> documents.</summary>
    public byte[] ROW_VERSION { get; private set; } = [];

    public static PROMO_CODE Create(
        string code,
        PromoCodeDiscountType discountType,
        decimal discountValue,
        int maxRedemptions,
        int maxPerUser,
        decimal minOrderAmount,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        PromoCodeScope scope,
        Guid? scopeRefId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (discountValue <= 0) throw new ArgumentOutOfRangeException(nameof(discountValue), discountValue, "discountValue must be positive.");
        if (maxRedemptions <= 0) throw new ArgumentOutOfRangeException(nameof(maxRedemptions), maxRedemptions, "maxRedemptions must be positive.");
        if (maxPerUser <= 0) throw new ArgumentOutOfRangeException(nameof(maxPerUser), maxPerUser, "maxPerUser must be positive.");
        if (minOrderAmount < 0) throw new ArgumentOutOfRangeException(nameof(minOrderAmount), minOrderAmount, "minOrderAmount cannot be negative.");
        if (endsAtUtc <= startsAtUtc) throw new ArgumentOutOfRangeException(nameof(endsAtUtc), endsAtUtc, "endsAtUtc must be after startsAtUtc.");
        if (scope != PromoCodeScope.AllCourses && scopeRefId is null)
        {
            throw new ArgumentException("scopeRefId is required unless scope is AllCourses.", nameof(scopeRefId));
        }

        return new PROMO_CODE
        {
            PROMO_CODE_ID = UuidV7.NewId(), CODE = code, DISCOUNT_TYPE = discountType, DISCOUNT_VALUE = discountValue,
            MAX_REDEMPTIONS = maxRedemptions, REDEEMED_COUNT = 0, MAX_PER_USER = maxPerUser, MIN_ORDER_AMOUNT = minOrderAmount,
            STARTS_AT_UTC = startsAtUtc, ENDS_AT_UTC = endsAtUtc, SCOPE = scope, SCOPE_REF_ID = scopeRefId, IS_ACTIVE = true,
        };
    }

    public void Deactivate() => IS_ACTIVE = false;

    public void Activate() => IS_ACTIVE = true;
}
