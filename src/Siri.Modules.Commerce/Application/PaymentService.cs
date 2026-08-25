using Siri.Integrations.Payment;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>
/// Orchestrates payment creation and status checks. Uses <see cref="IPaymentMethod"/> (Stripe PromptPay adapter)
/// and enforces ownership by checking the corresponding <see cref="ORDER.USER_ID"/>.
/// </summary>
public sealed class PaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentMethod _paymentMethod;
    private readonly IClock _clock;

    public PaymentService(
        IPaymentRepository paymentRepository,
        IOrderRepository orderRepository,
        IPaymentMethod paymentMethod,
        IClock clock)
    {
        _paymentRepository = paymentRepository;
        _orderRepository = orderRepository;
        _paymentMethod = paymentMethod;
        _clock = clock;
    }

    public async Task<Result<PaymentResponse>> GetByIdAsync(Guid userId, Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            return Result.Failure<PaymentResponse>(DomainError.NotFound("ไม่พบรายการชำระเงินที่ระบุ"));
        }

        var order = await _orderRepository.GetByIdAsync(payment.ORDER_ID, cancellationToken).ConfigureAwait(false);
        if (order is null || order.USER_ID != userId)
        {
            return Result.Failure<PaymentResponse>(DomainError.NotFound("ไม่พบรายการชำระเงินที่ระบุ"));
        }

        return ToResponse(payment);
    }

    public async Task<Result<PaymentResponse>> CreateAsync(Guid userId, CreatePaymentCommand command, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(command.OrderId, cancellationToken).ConfigureAwait(false);
        if (order is null || order.USER_ID != userId)
        {
            return Result.Failure<PaymentResponse>(DomainError.NotFound("ไม่พบคำสั่งซื้อที่ระบุ"));
        }

        if (order.STATUS == OrderStatus.Paid || order.TOTAL_AMOUNT <= 0m)
        {
            return Result.Failure<PaymentResponse>(DomainError.Validation("คำสั่งซื้อนี้ได้รับการชำระเงินหรือเป็นรายการฟรีเรียบร้อยแล้ว ไม่จำเป็นต้องสร้างรายการชำระเงิน"));
        }

        if (order.STATUS is not (OrderStatus.Pending or OrderStatus.AwaitingPayment))
        {
            return Result.Failure<PaymentResponse>(DomainError.Validation($"ไม่สามารถสร้างการชำระเงินสำหรับคำสั่งซื้อในสถานะ {order.STATUS} ได้"));
        }

        // Create PaymentIntent with Stripe
        var intentResult = await _paymentMethod.CreatePaymentIntentAsync(
            new CreatePaymentIntentRequest(
                order.ORDER_ID,
                order.ORDER_NO,
                order.TOTAL_AMOUNT,
                order.CURRENCY),
            cancellationToken).ConfigureAwait(false);

        if (intentResult.IsFailure)
        {
            return Result.Failure<PaymentResponse>(intentResult.Error);
        }

        var payment = PAYMENT.Create(
            order.ORDER_ID,
            command.Method,
            intentResult.Value.PaymentIntentId,
            order.TOTAL_AMOUNT,
            _clock);

        if (order.STATUS == OrderStatus.Pending)
        {
            order.MarkAwaitingPayment();
        }

        await _paymentRepository.AddAsync(payment, cancellationToken).ConfigureAwait(false);

        return new PaymentResponse(
            payment.PAYMENT_ID,
            payment.ORDER_ID,
            payment.METHOD,
            payment.AMOUNT,
            payment.STATUS,
            payment.CREATED_AT_UTC,
            intentResult.Value.ClientSecret,
            intentResult.Value.QrCodeUrl,
            intentResult.Value.QrCodeData);
    }

    private static PaymentResponse ToResponse(PAYMENT payment) =>
        new(payment.PAYMENT_ID, payment.ORDER_ID, payment.METHOD, payment.AMOUNT, payment.STATUS, payment.CREATED_AT_UTC);
}

public sealed record PaymentResponse(
    Guid Id,
    Guid OrderId,
    PaymentMethod Method,
    decimal Amount,
    PaymentStatus Status,
    DateTime CreatedAtUtc,
    string? ClientSecret = null,
    string? QrCodeUrl = null,
    string? QrCodeData = null);

public sealed record CreatePaymentCommand(Guid OrderId, PaymentMethod Method);
