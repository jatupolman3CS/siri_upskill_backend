using FluentValidation;

namespace Siri.Modules.Commerce.Application;

public sealed class ResolvePaymentOpsValidator : AbstractValidator<ResolvePaymentOpsRequest>
{
    public ResolvePaymentOpsValidator()
    {
        RuleFor(x => x.Action)
            .IsInEnum()
            .WithMessage("การดำเนินการต้องเป็นค่าที่ระบบรองรับ (Refund, ReopenAndFulfillOrder, GrantAccessOnly, Dismiss)");

        When(x => x.Action == PaymentOpsResolutionAction.Dismiss, () =>
        {
            RuleFor(x => x.Note)
                .NotEmpty()
                .WithMessage("ต้องระบุเหตุผลในการยกเลิกรายการ");
        });
    }
}

public sealed class DismissPaymentOpsValidator : AbstractValidator<DismissPaymentOpsRequest>
{
    public DismissPaymentOpsValidator()
    {
        RuleFor(x => x.Note)
            .NotEmpty()
            .WithMessage("ต้องระบุเหตุผลในการยกเลิกรายการ");
    }
}
