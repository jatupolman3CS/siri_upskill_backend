using Siri.Modules.Cms.Application;
using Siri.Modules.Cms.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Cms;

public sealed class CmsServiceTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeBannerRepository : IBannerRepository
    {
        public readonly Dictionary<Guid, BANNER> Banners = [];

        public Task<BANNER?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Banners.TryGetValue(id, out var b) ? b : null);

        public Task<IReadOnlyList<BANNER>> GetByPlacementAsync(string placement, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BANNER>>(Banners.Values.Where(b => b.PLACEMENT == placement).OrderBy(b => b.SORT_ORDER).ToList());

        public Task<IReadOnlyList<BANNER>> GetActiveByPlacementAsync(string placement, DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BANNER>>(Banners.Values.Where(b => b.PLACEMENT == placement && b.IS_ACTIVE).OrderBy(b => b.SORT_ORDER).ToList());

        public Task<PagedResult<BANNER>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(PagedResult<BANNER>.Create(Banners.Values.Skip((page - 1) * pageSize).Take(pageSize).ToList(), Banners.Count, page, pageSize));

        public void Add(BANNER banner) => Banners[banner.BANNER_ID] = banner;

        public void Remove(BANNER banner) => Banners.Remove(banner.BANNER_ID);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePostRepository : IPostRepository
    {
        public readonly Dictionary<Guid, POST> Posts = [];

        public Task<POST?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Posts.TryGetValue(id, out var p) ? p : null);

        public Task<POST?> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken) =>
            Task.FromResult(Posts.Values.FirstOrDefault(p => p.SLUG == slug && p.STATUS == PostStatus.Published));

        public Task<bool> SlugExistsAsync(string slug, Guid? excludePostId, CancellationToken cancellationToken) =>
            Task.FromResult(Posts.Values.Any(p => p.SLUG == slug && (!excludePostId.HasValue || p.POST_ID != excludePostId.Value)));

        public Task<PagedResult<POST>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(PagedResult<POST>.Create(Posts.Values.Skip((page - 1) * pageSize).Take(pageSize).ToList(), Posts.Count, page, pageSize));

        public Task<PagedResult<POST>> GetPublishedPagedAsync(int page, int pageSize, CancellationToken cancellationToken)
        {
            var published = Posts.Values.Where(p => p.STATUS == PostStatus.Published).ToList();
            return Task.FromResult(PagedResult<POST>.Create(published.Skip((page - 1) * pageSize).Take(pageSize).ToList(), published.Count, page, pageSize));
        }

        public void Add(POST post) => Posts[post.POST_ID] = post;

        public void Remove(POST post) => Posts.Remove(post.POST_ID);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeMenuItemRepository : IMenuItemRepository
    {
        public readonly Dictionary<Guid, MENU_ITEM> Items = [];

        public Task<MENU_ITEM?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.TryGetValue(id, out var m) ? m : null);

        public Task<IReadOnlyList<MENU_ITEM>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MENU_ITEM>>(Items.Values.OrderBy(m => m.SORT_ORDER).ToList());

        public Task<IReadOnlyList<MENU_ITEM>> GetChildrenAsync(Guid? parentId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MENU_ITEM>>(Items.Values.Where(m => m.PARENT_ID == parentId).OrderBy(m => m.SORT_ORDER).ToList());

        public void Add(MENU_ITEM menuItem) => Items[menuItem.MENU_ITEM_ID] = menuItem;

        public void Remove(MENU_ITEM menuItem) => Items.Remove(menuItem.MENU_ITEM_ID);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeRedirectRepository : IRedirectRepository
    {
        public readonly Dictionary<Guid, REDIRECT> Redirects = [];

        public Task<REDIRECT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Redirects.TryGetValue(id, out var r) ? r : null);

        public Task<REDIRECT?> GetByFromPathAsync(string fromPath, CancellationToken cancellationToken) =>
            Task.FromResult(Redirects.Values.FirstOrDefault(r => r.FROM_PATH == fromPath));

        public Task<bool> FromPathExistsAsync(string fromPath, Guid? excludeRedirectId, CancellationToken cancellationToken) =>
            Task.FromResult(Redirects.Values.Any(r => r.FROM_PATH == fromPath && (!excludeRedirectId.HasValue || r.REDIRECT_ID != excludeRedirectId.Value)));

        public Task<PagedResult<REDIRECT>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(PagedResult<REDIRECT>.Create(Redirects.Values.Skip((page - 1) * pageSize).Take(pageSize).ToList(), Redirects.Count, page, pageSize));

        public void Add(REDIRECT redirect) => Redirects[redirect.REDIRECT_ID] = redirect;

        public void Remove(REDIRECT redirect) => Redirects.Remove(redirect.REDIRECT_ID);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task BannerService_CreateAndReorder()
    {
        var repo = new FakeBannerRepository();
        var service = new BannerService(repo);

        var c1 = new CreateBannerCommand("home-hero", "https://img1.png", null, "https://link1.com", "Banner 1", null, null);
        var c2 = new CreateBannerCommand("home-hero", "https://img2.png", null, "https://link2.com", "Banner 2", null, null);

        var r1 = await service.CreateAsync(c1, CancellationToken.None);
        var r2 = await service.CreateAsync(c2, CancellationToken.None);

        Assert.True(r1.IsSuccess);
        Assert.True(r2.IsSuccess);
        Assert.Equal(0, r1.Value.SortOrder);
        Assert.Equal(1, r2.Value.SortOrder);

        var reorderCmd = new ReorderBannersCommand([
            new ReorderBannerItem(r2.Value.Id, 0),
            new ReorderBannerItem(r1.Value.Id, 1)
        ]);
        var reorderResult = await service.ReorderAsync(reorderCmd, CancellationToken.None);

        Assert.True(reorderResult.IsSuccess);
        Assert.Equal(0, repo.Banners[r2.Value.Id].SORT_ORDER);
        Assert.Equal(1, repo.Banners[r1.Value.Id].SORT_ORDER);
    }

    [Fact]
    public async Task PostService_Create_Publish_GetBySlug()
    {
        var repo = new FakePostRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new PostService(repo, clock);

        var authorId = Guid.NewGuid();
        var cmd = new CreatePostCommand("intro-to-csharp", "Intro to C#", "Excerpt", "<p>Hello</p>", null, "Intro", "Desc");

        var created = await service.CreateAsync(authorId, cmd, CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal(PostStatus.Draft, created.Value.Status);

        // Before publish, GetPublishedBySlug returns NotFound
        var beforePublish = await service.GetPublishedBySlugAsync("intro-to-csharp", CancellationToken.None);
        Assert.False(beforePublish.IsSuccess);

        // Publish
        var published = await service.ChangeStatusAsync(created.Value.Id, new ChangePostStatusCommand(PostStatus.Published), CancellationToken.None);
        Assert.True(published.IsSuccess);

        // After publish, GetPublishedBySlug returns Post
        var afterPublish = await service.GetPublishedBySlugAsync("intro-to-csharp", CancellationToken.None);
        Assert.True(afterPublish.IsSuccess);
        Assert.Equal(created.Value.Id, afterPublish.Value.Id);
    }

    [Fact]
    public async Task MenuItemService_Create_DeleteWithChildren_Rejects()
    {
        var repo = new FakeMenuItemRepository();
        var service = new MenuItemService(repo);

        var parentCmd = new CreateMenuItemCommand(null, "Courses", "/courses");
        var parent = await service.CreateAsync(parentCmd, CancellationToken.None);
        Assert.True(parent.IsSuccess);

        var childCmd = new CreateMenuItemCommand(parent.Value.Id, ".NET", "/courses/dotnet");
        var child = await service.CreateAsync(childCmd, CancellationToken.None);
        Assert.True(child.IsSuccess);

        // Deleting parent when child exists should fail
        var deleteParent = await service.DeleteAsync(parent.Value.Id, CancellationToken.None);
        Assert.False(deleteParent.IsSuccess);
        Assert.Equal("conflict", deleteParent.Error.Code);

        // Deleting child first
        var deleteChild = await service.DeleteAsync(child.Value.Id, CancellationToken.None);
        Assert.True(deleteChild.IsSuccess);

        // Deleting parent now succeeds
        var deleteParentAgain = await service.DeleteAsync(parent.Value.Id, CancellationToken.None);
        Assert.True(deleteParentAgain.IsSuccess);
    }

    [Fact]
    public async Task RedirectService_Create_Duplicate_ReturnsConflict()
    {
        var repo = new FakeRedirectRepository();
        var service = new RedirectService(repo);

        var cmd = new CreateRedirectCommand("/old-path", "/new-path", 301);
        var r1 = await service.CreateAsync(cmd, CancellationToken.None);
        Assert.True(r1.IsSuccess);

        var r2 = await service.CreateAsync(cmd, CancellationToken.None);
        Assert.False(r2.IsSuccess);
        Assert.Equal("conflict", r2.Error.Code);
    }
}
