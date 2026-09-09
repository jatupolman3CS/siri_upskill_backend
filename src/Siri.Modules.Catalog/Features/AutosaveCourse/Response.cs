namespace Siri.Modules.Catalog.Features.AutosaveCourse;

public sealed record AutosaveCourseResponse(
    Guid CourseId,
    byte[] RowVersion,
    DateTime UpdatedAtUtc,
    IReadOnlyList<AutosaveSectionIds>? Sections = null);

public sealed record AutosaveSectionIds(Guid Id, IReadOnlyList<AutosaveEpisodeIds> Episodes);

public sealed record AutosaveEpisodeIds(Guid Id);
