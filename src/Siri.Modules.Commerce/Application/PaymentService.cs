using Microsoft.Extensions.Options;
using Siri.Integrations.Payment;
using Siri.Integrations.Payment.Stripe;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Identity.Contracts;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>
/// Orchestrates payment creation and status checks. Uses <see cref="IPaymentMethod"/> (Stripe PromptPay/Card adapter)
/// and enforces ownership by checking the corresponding <see cref="ORDER.USER_ID"/>.
/// </summary>
public sealed class PaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentMethod _paymentMethod;
    private readonly IClock _clock;
    private readonly IUserContactReader _userContactReader;
    private readonly PaymentOptions _paymentOptions;
    private readonly StripeOptions _stripeOptions;

    public PaymentService(
        IPaymentRepository paymentRepository,
        IOrderRepository orderRepository,
        IPaymentMethod paymentMethod,
        IClock clock,
        IUserContactReader userContactReader,
        IOptions<PaymentOptions> paymentOptions,
        IOptions<StripeOptions> stripeOptions)
    {
        _paymentRepository = paymentRepository;
        _orderRepository = orderRepository;
        _paymentMethod = paymentMethod;
        _clock = clock;
        _userContactReader = userContactReader;
        _paymentOptions = paymentOptions.Value;
        _stripeOptions = stripeOptions.Value;
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

        if (!_paymentOptions.EnabledMethods.Contains(command.Method))
        {
            return Result.Failure<PaymentResponse>(DomainError.Validation(
                $"วิธีชำระเงิน {command.Method} ยังไม่เปิดใช้งานในขณะนี้ กรุณาเลือกวิธีอื่น"));
        }

        var customerEmail = await _userContactReader.GetEmailAsync(userId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            return Result.Failure<PaymentResponse>(DomainError.Validation("A customer email is required to process payment."));
        }

        // Create and confirm the PromptPay intent so the response contains a scannable QR image;
        // for Card, this only creates the PaymentIntent (Confirm=false) — the client confirms via
        // Stripe.js Payment Element.
        var intentResult = await _paymentMethod.CreatePaymentIntentAsync(
            new CreatePaymentIntentRequest(
                order.ORDER_ID,
                order.ORDER_NO,
                order.TOTAL_AMOUNT,
                order.CURRENCY,
                CustomerEmail: customerEmail,
                Method: command.Method switch
                {
                    PaymentMethod.Card => PaymentMethodType.Card,
                    _ => PaymentMethodType.PromptPay,
                }),
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

    public PaymentConfigResponse GetConfig() =>
        new(_stripeOptions.PublishableKey, _paymentOptions.EnabledMethods);

    private static PaymentResponse ToResponse(PAYMENT payment) =>
        new(payment.PAYMENT_ID, payment.ORDER_ID, payment.METHOD, payment.AMOUNT, payment.STATUS, payment.CREATED_AT_UTC);
}

public sealed record PaymentConfigResponse(string PublishableKey, IReadOnlyList<PaymentMethod> EnabledMethods);

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
