using Siri.Modules.Catalog.Features.ReorderCategories;

namespace Siri.UnitTests.Catalog;

public class ReorderCategoriesValidatorTests
{
    private readonly ReorderCategoriesValidator _validator = new();

    [Fact]
    public void Validate_DistinctCategoriesAndSortOrders_Succeeds()
    {
        var command = new ReorderCategoriesCommand([
            new ReorderCategoryItem(Guid.NewGuid(), 0),
            new ReorderCategoryItem(Guid.NewGuid(), 1),
            new ReorderCategoryItem(Guid.NewGuid(), 2),
        ]);

        var result = _validator.Validate(command);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyItems_Fails()
    {
        var command = new ReorderCategoriesCommand([]);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_DuplicateCategoryId_Fails()
    {
        var duplicateId = Guid.NewGuid();
        var command = new ReorderCategoriesCommand([
            new ReorderCategoryItem(duplicateId, 0),
            new ReorderCategoryItem(duplicateId, 1),
        ]);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_DuplicateSortOrder_Fails()
    {
        var command = new ReorderCategoriesCommand([
            new ReorderCategoryItem(Guid.NewGuid(), 0),
            new ReorderCategoryItem(Guid.NewGuid(), 0),
        ]);

        var result = _validator.Validate(command);

        Assert.False(result.IsValid);
    }
}
