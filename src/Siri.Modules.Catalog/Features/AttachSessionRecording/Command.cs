namespace Siri.Modules.Catalog.Features.AttachSessionRecording;

/// <summary>
/// Attaches a teaching recording to a finished live session (docs/contracts/P11-06-live-recording-catchup.md §3).
/// <para>
/// Exactly one of <paramref name="MediaAssetId"/> / <paramref name="EpisodeId"/>:
/// <list type="bullet">
/// <item><b>Mode A</b> — <paramref name="MediaAssetId"/>: an uploaded media asset (<c>Ready</c>, owned by the caller) becomes a new lesson (or replaces the
/// media of the lesson already recording this session). <paramref name="SectionId"/> picks the section it goes into (default: the "บันทึกการสอนสด" section,
/// created on first use) and <paramref name="EpisodeTitle"/> names the lesson (default: "บันทึก: {session title}").</item>
/// <item><b>Mode B</b> — <paramref name="EpisodeId"/>: link a lesson of this course that already has media. <paramref name="SectionId"/> is not allowed
/// here and <paramref name="EpisodeTitle"/> is ignored (the lesson keeps its own title).</item>
/// </list>
/// </para>
/// </summary>
public sealed record AttachSessionRecordingCommand(
    Guid? MediaAssetId,
    Guid? EpisodeId,
    Guid? SectionId,
    string? EpisodeTitle);
