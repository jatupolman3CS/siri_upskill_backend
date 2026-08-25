using Microsoft.EntityFrameworkCore;
using Siri.Modules.Cms.Application;
using Siri.Modules.Cms.Domain;
using Siri.Persistence;

namespace Siri.Modules.Cms.Infrastructure;

public sealed class MenuItemRepository(AppDbContext dbContext) : IMenuItemRepository
{
    public Task<MENU_ITEM?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.MenuItems().FirstOrDefaultAsync(m => m.MENU_ITEM_ID == id, cancellationToken);

    public async Task<IReadOnlyList<MENU_ITEM>> GetAllAsync(CancellationToken cancellationToken) =>
        await dbContext.MenuItems()
            .OrderBy(m => m.SORT_ORDER)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<MENU_ITEM>> GetChildrenAsync(Guid? parentId, CancellationToken cancellationToken) =>
        await dbContext.MenuItems()
            .Where(m => m.PARENT_ID == parentId)
            .OrderBy(m => m.SORT_ORDER)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(MENU_ITEM menuItem) => dbContext.MenuItems().Add(menuItem);

    public void Remove(MENU_ITEM menuItem) => dbContext.MenuItems().Remove(menuItem);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
