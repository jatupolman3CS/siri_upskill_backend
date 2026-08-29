using Microsoft.EntityFrameworkCore;
using Siri.Modules.Cms.Domain;
using Siri.Persistence;

namespace Siri.Modules.Cms.Infrastructure;

public static class AppDbContextCmsExtensions
{
    public static DbSet<BANNER> Banners(this AppDbContext context) => context.Set<BANNER>();

    public static DbSet<MENU_ITEM> MenuItems(this AppDbContext context) => context.Set<MENU_ITEM>();

    public static DbSet<POST> Posts(this AppDbContext context) => context.Set<POST>();

    public static DbSet<REDIRECT> Redirects(this AppDbContext context) => context.Set<REDIRECT>();

    public static DbSet<FEATURE_FLAG> FeatureFlags(this AppDbContext context) => context.Set<FEATURE_FLAG>();
}
