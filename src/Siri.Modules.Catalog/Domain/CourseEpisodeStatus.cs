namespace Siri.Modules.Catalog.Domain;

/// <summary>Not enumerated by DATABASE.md. Kept minimal and entirely derived from media presence for
/// this task: <see cref="CourseEpisode.AttachMedia"/> sets <see cref="Ready"/>,
/// <see cref="CourseEpisode.RemoveMedia"/> sets back to <see cref="Draft"/>. <see cref="Processing"/>
/// sits unused until Phase 2's Bunny Stream webhook flow needs an in-between state.</summary>
public enum CourseEpisodeStatus
{
    Draft,
    Processing,
    Ready,
}
