using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Infrastructure;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Catalog;

/// <summary>
/// No virus-scanning engine is integrated. The default scanner must therefore never report a file as
/// clean on its own: it refuses the upload (Required, the default) or — only when an operator explicitly
/// opts in — lets it through while logging that it was NOT scanned.
/// </summary>
public sealed class UnconfiguredAttachmentVirusScannerTests
{
    private sealed class RecordingLogger : ILogger<UnconfiguredAttachmentVirusScanner>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static UnconfiguredAttachmentVirusScanner Create(AttachmentVirusScanOptions options, ILogger<UnconfiguredAttachmentVirusScanner>? logger = null) =>
        new(Options.Create(options), logger ?? NullLogger<UnconfiguredAttachmentVirusScanner>.Instance);

    [Fact]
    public void Options_Default_IsRequired()
    {
        Assert.Equal(AttachmentVirusScanMode.Required, new AttachmentVirusScanOptions().Mode);
    }

    [Fact]
    public async Task ScanBytesAsync_RequiredWithNoEngine_FailsWith503Code()
    {
        var scanner = Create(new AttachmentVirusScanOptions { Mode = AttachmentVirusScanMode.Required });

        var result = await scanner.ScanBytesAsync("report.pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UnconfiguredAttachmentVirusScanner.ScannerNotConfiguredCode, result.Error.Code);
        Assert.EndsWith(DomainErrorHttpResults.NotConfiguredCodeSuffix, result.Error.Code);
    }

    [Fact]
    public async Task ScanAsync_RequiredWithNoEngine_FailsWith503Code()
    {
        var scanner = Create(new AttachmentVirusScanOptions { Mode = AttachmentVirusScanMode.Required });
        await using var stream = new MemoryStream([1, 2, 3]);

        var result = await scanner.ScanAsync("report.pdf", stream, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UnconfiguredAttachmentVirusScanner.ScannerNotConfiguredCode, result.Error.Code);
    }

    [Fact]
    public async Task ScanBytesAsync_Disabled_AllowsTheFileButLogsItWasNotScanned()
    {
        var logger = new RecordingLogger();
        var scanner = Create(new AttachmentVirusScanOptions { Mode = AttachmentVirusScanMode.Disabled }, logger);

        var result = await scanner.ScanBytesAsync("report.pdf", new byte[] { 1 }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("WITHOUT a virus scan", entry.Message);
        Assert.DoesNotContain("clean", entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanBytesAsync_RequiredWithNoEngine_LogsAnErrorNotAWarningAboutAnAcceptedFile()
    {
        var logger = new RecordingLogger();
        var scanner = Create(new AttachmentVirusScanOptions { Mode = AttachmentVirusScanMode.Required }, logger);

        await scanner.ScanBytesAsync("report.pdf", new byte[] { 1 }, CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("refused", entry.Message);
    }

    [Theory]
    [InlineData(null, AttachmentVirusScanMode.Required)]
    [InlineData("Required", AttachmentVirusScanMode.Required)]
    [InlineData("Disabled", AttachmentVirusScanMode.Disabled)]
    [InlineData("disabled", AttachmentVirusScanMode.Disabled)]
    public void Options_BindFromTheAttachmentsVirusScanSection(string? configured, AttachmentVirusScanMode expected)
    {
        var values = new Dictionary<string, string?>();
        if (configured is not null)
        {
            values[$"{AttachmentVirusScanOptions.SectionName}:Mode"] = configured;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new AttachmentVirusScanOptions();
        configuration.GetSection(AttachmentVirusScanOptions.SectionName).Bind(options);

        Assert.Equal(expected, options.Mode);
        Assert.Equal("Attachments:VirusScan", AttachmentVirusScanOptions.SectionName);
    }
}
