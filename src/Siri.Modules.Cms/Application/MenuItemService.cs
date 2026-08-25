using Siri.Modules.Cms.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Application;

public sealed class MenuItemService(IMenuItemRepository repository)
{
    public async Task<Result<MenuItemResponse>> CreateAsync(CreateMenuItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var siblings = await repository.GetChildrenAsync(command.ParentId, cancellationToken).ConfigureAwait(false);
        var sortOrder = siblings.Count == 0 ? 0 : siblings.Max(m => m.SORT_ORDER) + 1;

        var menuItem = MENU_ITEM.Create(command.ParentId, command.Label, command.Url, sortOrder);

        repository.Add(menuItem);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(menuItem));
    }

    public async Task<Result<MenuItemResponse>> UpdateAsync(Guid id, UpdateMenuItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var menuItem = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (menuItem is null)
        {
            return Result.Failure<MenuItemResponse>(DomainError.NotFound("ไม่พบเมนู"));
        }

        menuItem.Update(command.Label, command.Url, command.IsActive);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(menuItem));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var menuItem = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (menuItem is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบเมนู"));
        }

        var children = await repository.GetChildrenAsync(id, cancellationToken).ConfigureAwait(false);
        if (children.Count > 0)
        {
            return Result.Failure(DomainError.Conflict("ไม่สามารถลบเมนูที่มีเมนูย่อยอยู่ได้"));
        }

        repository.Remove(menuItem);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<IReadOnlyList<MenuItemResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var items = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return items.Select(ToResponse).ToList();
    }

    private static MenuItemResponse ToResponse(MENU_ITEM m) =>
        new(
            m.MENU_ITEM_ID,
            m.PARENT_ID,
            m.LABEL,
            m.URL,
            m.SORT_ORDER,
            m.IS_ACTIVE);
}
