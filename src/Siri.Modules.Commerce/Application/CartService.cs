using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>Cart application service managing learner shopping cart lifecycle.</summary>
public sealed class CartService(ICartRepository cartRepository, IClock clock)
{
    public async Task<Result<CartResponse>> GetMyCartAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cart = await cartRepository.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (cart is null)
        {
            cart = CART.Create(userId, clock);
            await cartRepository.AddAsync(cart, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(ToResponse(cart));
    }

    public async Task<Result<CartResponse>> AddItemAsync(Guid userId, AddCartItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var cart = await cartRepository.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (cart is null)
        {
            cart = CART.Create(userId, clock);
            await cartRepository.AddAsync(cart, cancellationToken).ConfigureAwait(false);
        }

        // Avoid adding duplicate items
        if (!cart.CART_ITEMS.Any(i => i.ITEM_TYPE == command.ItemType && i.REF_ID == command.RefId))
        {
            cart.AddItem(command.ItemType, command.RefId, clock);
            await cartRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(ToResponse(cart));
    }

    public async Task<Result<CartResponse>> RemoveItemAsync(Guid userId, Guid cartItemId, CancellationToken cancellationToken)
    {
        var cart = await cartRepository.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (cart is null)
        {
            return Result.Failure<CartResponse>(DomainError.NotFound("ไม่พบตะกร้าสินค้า"));
        }

        try
        {
            cart.RemoveItem(cartItemId, clock);
            await cartRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return Result.Failure<CartResponse>(DomainError.NotFound("ไม่พบรายการในตะกร้าสินค้า"));
        }

        return Result.Success(ToResponse(cart));
    }

    private static CartResponse ToResponse(CART cart) =>
        new(
            cart.CART_ID,
            cart.USER_ID,
            cart.CART_ITEMS.Select(i => new CartItemResponse(i.CART_ITEM_ID, i.ITEM_TYPE, i.REF_ID, i.ADDED_AT_UTC)).ToList(),
            cart.UPDATED_AT_UTC);
}

public sealed record CartResponse(Guid Id, Guid UserId, IReadOnlyList<CartItemResponse> Items, DateTime UpdatedAtUtc);

public sealed record CartItemResponse(Guid Id, CartItemType ItemType, Guid RefId, DateTime AddedAtUtc);

public sealed record AddCartItemCommand(CartItemType ItemType, Guid RefId);
