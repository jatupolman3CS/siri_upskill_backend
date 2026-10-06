using Siri.Modules.Catalog.Contracts;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>Default no-op จนกว่า Siri.Modules.Live (P11-03) จะ register implementation จริงทับ —
/// ให้ P11-02's handler เรียก ILiveMeetingSink ได้ตั้งแต่วันแรกโดยไม่ error แม้ Live module จะยังไม่มีอยู่จริง.</summary>
public sealed class NullLiveMeetingSink : ILiveMeetingSink
{
    public Task OnSessionScheduledAsync(Guid sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task OnSessionChangedAsync(Guid sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task OnSessionCancelledAsync(Guid sessionId, CancellationToken cancellationToken) => Task.CompletedTask;
}
