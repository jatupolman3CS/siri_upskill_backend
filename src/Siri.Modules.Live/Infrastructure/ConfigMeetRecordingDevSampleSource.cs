using Microsoft.Extensions.Options;
using Siri.Integrations.Google.Logging;
using Siri.Modules.Live.Application;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// <b>DEVELOPMENT ONLY</b> (registered only with <c>Live:Provider=Logging</c>): the file the fake recording provider serves as "the Meet recording" comes from
/// <c>Live:Recording:AutoImport:DevSampleFilePath</c> — point it at a short real MP4 to rehearse a successful import end to end. With the setting empty the
/// integration's own placeholder bytes are served instead (the whole pipeline still runs, but the video provider will refuse to transcode them).
/// <c>ProductionConfigurationGuard</c> refuses a non-empty path in Production, and this class is not registered outside Logging mode at all.
/// </summary>
public sealed class ConfigMeetRecordingDevSampleSource(IOptions<LiveOptions> options) : IMeetRecordingDevSampleSource
{
    private readonly DefaultMeetRecordingDevSampleSource _placeholder = new();

    public Task<MeetRecordingDevSample> OpenAsync(CancellationToken ct)
    {
        var path = options.Value.Recording.AutoImport.DevSampleFilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return _placeholder.OpenAsync(ct);
        }

        // Throws FileNotFoundException/IOException for a bad path: a development misconfiguration that should be loud.
        var stream = new FileStream(path.Trim(), FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
        return Task.FromResult(new MeetRecordingDevSample(stream, stream.Length, Path.GetFileName(path.Trim())));
    }
}
