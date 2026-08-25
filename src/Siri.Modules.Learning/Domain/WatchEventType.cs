namespace Siri.Modules.Learning.Domain;

/// <summary>docs/DATABASE.md's "learning" section: "EventType — play|pause|seek|ended|heartbeat" on
/// WatchEvents — raw playback telemetry from the player, appended by the (not-yet-built) playback-
/// heartbeat flow. <see cref="Heartbeat"/> is the periodic "still watching" ping used to accumulate watch
/// time even when the learner never pauses/seeks.</summary>
public enum WatchEventType
{
    Play,
    Pause,
    Seek,
    Ended,
    Heartbeat,
}
