using Siri.Modules.Catalog.Domain;

namespace Siri.UnitTests.Catalog;

public class CategoryTests
{
    [Fact]
    public void Create_ValidInput_ReturnsActiveCategoryWithGivenFields()
    {
        var parentId = Guid.NewGuid();

        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", "icon-web", parentId, 3);

        Assert.NotEqual(Guid.Empty, category.Id);
        Assert.Equal("web-development", category.Slug);
        Assert.Equal("พัฒนาเว็บ", category.NameTh);
        Assert.Equal("Web Development", category.NameEn);
        Assert.Equal("icon-web", category.IconKey);
        Assert.Equal(parentId, category.ParentId);
        Assert.Equal(3, category.SortOrder);
        Assert.True(category.IsActive);
    }

    [Fact]
    public void Create_NoParent_RootCategoryHasNullParentId()
    {
        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", null, null, 0);

        Assert.Null(category.ParentId);
    }

    [Fact]
    public void Create_NoIcon_IconKeyIsNull()
    {
        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", null, null, 0);

        Assert.Null(category.IconKey);
    }

    [Theory]
    [InlineData("", "พัฒนาเว็บ", "Web Development")]
    [InlineData("web-development", "", "Web Development")]
    [InlineData("web-development", "พัฒนาเว็บ", "")]
    public void Create_MissingRequiredField_ThrowsArgumentException(string slug, string nameTh, string nameEn)
    {
        Assert.Throws<ArgumentException>(() => CATEGORY.Create(slug, nameTh, nameEn, null, null, 0));
    }

    [Fact]
    public void Rename_ValidNames_UpdatesBothNames()
    {
        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", null, null, 0);

        category.Rename("การพัฒนาเว็บ", "Web Dev");

        Assert.Equal("การพัฒนาเว็บ", category.NameTh);
        Assert.Equal("Web Dev", category.NameEn);
    }

    [Theory]
    [InlineData("", "Web Dev")]
    [InlineData("การพัฒนาเว็บ", "")]
    public void Rename_MissingRequiredField_ThrowsArgumentException(string nameTh, string nameEn)
    {
        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", null, null, 0);

        Assert.Throws<ArgumentException>(() => category.Rename(nameTh, nameEn));
    }

    [Fact]
    public void ChangeSlug_ValidSlug_UpdatesSlug()
    {
        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", null, null, 0);

        category.ChangeSlug("web-dev");

        Assert.Equal("web-dev", category.Slug);
    }

    [Fact]
    public void ChangeSlug_Empty_ThrowsArgumentException()
    {
        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", null, null, 0);

        Assert.Throws<ArgumentException>(() => category.ChangeSlug(""));
    }

    [Fact]
    public void SetIcon_NewValue_UpdatesIconKey()
    {
        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", null, null, 0);

        category.SetIcon("icon-web");

        Assert.Equal("icon-web", category.IconKey);
    }

    [Fact]
    public void SetIcon_Null_ClearsIconKey()
    {
        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", "icon-web", null, 0);

        category.SetIcon(null);

        Assert.Null(category.IconKey);
    }

    [Fact]
    public void Reorder_NewValue_UpdatesSortOrder()
    {
        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", null, null, 0);

        category.Reorder(7);

        Assert.Equal(7, category.SortOrder);
    }

    [Fact]
    public void Deactivate_ActiveCategory_SetsIsActiveFalse()
    {
        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", null, null, 0);

        category.Deactivate();

        Assert.False(category.IsActive);
    }

    [Fact]
    public void Activate_InactiveCategory_SetsIsActiveTrue()
    {
        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", null, null, 0);
        category.Deactivate();

        category.Activate();

        Assert.True(category.IsActive);
    }

    [Fact]
    public void MoveTo_DifferentParent_UpdatesParentId()
    {
        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", null, null, 0);
        var newParentId = Guid.NewGuid();

        category.MoveTo(newParentId);

        Assert.Equal(newParentId, category.ParentId);
    }

    [Fact]
    public void MoveTo_Null_MakesItARootCategory()
    {
        var parentId = Guid.NewGuid();
        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", null, parentId, 0);

        category.MoveTo(null);

        Assert.Null(category.ParentId);
    }

    [Fact]
    public void MoveTo_ItsOwnId_ThrowsInvalidOperationException()
    {
        // The entity can only guard the trivial self-parent case — it has no way to see the rest of
        // the tree (backend.md: domain stays EF-ignorant), so the general cycle case (moving under a
        // descendant) is the Update handler's job, not tested here. See CATEGORY's own doc comment.
        var category = CATEGORY.Create("web-development", "พัฒนาเว็บ", "Web Development", null, null, 0);

        Assert.Throws<InvalidOperationException>(() => category.MoveTo(category.Id));
        Assert.Null(category.ParentId); // rejected attempt must not mutate state
    }
}
