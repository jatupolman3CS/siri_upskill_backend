namespace Siri.Modules.Cms.Domain;

/// <summary>
/// Not enumerated by docs/DATABASE.md — its <c>cms.Posts</c> sketch just says "Status" with no value
/// list, the same "not enumerated, kept minimal" situation
/// <see cref="Siri.Modules.Catalog.Domain.CourseEpisodeStatus"/>'s own doc comment describes.
/// <see cref="Draft"/>/<see cref="Published"/> cover the two states docs/REQUIREMENTS.md AD-01 ("เขียน
/// blog/article ที่ทำ SEO ได้ โดยไม่ต้อง deploy code") actually needs; <see cref="Archived"/> is added for
/// the same "hide without deleting" reason <see cref="Siri.Modules.Catalog.Domain.CourseStatus.Archived"/>
/// exists. No InReview/Rejected admin-approval workflow — nothing in AD-01 or docs/TASKS.md's P6-01
/// describes posts needing editorial review the way P1-05 gave courses one; a later task can add states
/// here if that turns out to be wrong. Per docs/DECISIONS.md D-17, only the <see cref="Domain.POST.STATUS"/>
/// property holding this enum is UPPERCASE — the enum type and its members stay ordinary PascalCase.
/// </summary>
public enum PostStatus
{
    Draft,
    Published,
    Archived,
}
