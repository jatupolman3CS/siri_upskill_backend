namespace Siri.Modules.Catalog.Features.ReorderCourseSections;

public sealed record ReorderCourseSectionsCommand(IReadOnlyList<ReorderCourseSectionItem> Items);

public sealed record ReorderCourseSectionItem(Guid SectionId, int SortOrder);
