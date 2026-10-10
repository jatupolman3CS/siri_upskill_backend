namespace Siri.Modules.Live.Application;

/// <summary>
/// Hands one claimed recording import (status <c>Transferring</c>, lease taken) to a background worker that does the long Drive -&gt; video provider copy
/// (P11-13). The five-minute <c>live-recording-import</c> tick must stay short, so it never copies a file itself: it claims the row, calls this once, and moves on.
/// Implemented over Hangfire by <c>HangfireRecordingTransferScheduler</c>; tests substitute a recorder.
/// </summary>
public interface IRecordingTransferScheduler
{
    /// <summary>Queues the transfer of <paramref name="importId"/>. Called after the claim is saved, so a row is queued once per claim; an exception means the
    /// transfer was NOT queued (the caller then gives the row back with a failed attempt instead of leaving it to wait out its lease).</summary>
    void Enqueue(Guid importId);
}
