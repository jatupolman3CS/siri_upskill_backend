using Microsoft.Extensions.Options;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.AddEpisodeAttachment;
using Xunit;

namespace Siri.UnitTests.Catalog;

public sealed class EpisodeAttachmentTests
{
    [Fact]
    public void EpisodeAttachment_Create_SetsPropertiesCorrectly()
    {
        var episodeId = Guid.NewGuid();
        var attachment = EPISODE_ATTACHMENT.Create(
            episodeId,
            "slides.pdf",
            "attachments/course-1/slides.pdf",
            "application/pdf",
            2048500);

        Assert.NotEqual(Guid.Empty, attachment.Id);
        Assert.Equal(episodeId, attachment.EpisodeId);
        Assert.Equal("slides.pdf", attachment.FileName);
        Assert.Equal("attachments/course-1/slides.pdf", attachment.StorageKey);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(2048500, attachment.SizeBytes);
    }

    [Theory]
    [InlineData("", "storage/key", "application/pdf", 100, false)]
    [InlineData("file.pdf", "", "application/pdf", 100, false)]
    [InlineData("file.pdf", "storage/key", "", 100, false)]
    [InlineData("file.pdf", "storage/key", "application/pdf", 0, false)]
    [InlineData("file.pdf", "storage/key", "application/pdf", -5, false)]
    [InlineData("file.exe", "storage/key", "application/octet-stream", 1024, false)]
    [InlineData("file.pdf", "storage/key", "image/png", 1024, false)]
    [InlineData("file.pdf", "storage/key", "application/pdf", 1024, true)]
    [InlineData("data.zip", "storage/key", "application/zip", 2048, true)]
    [InlineData("notes.docx", "storage/key", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 4096, true)]
    public void AddEpisodeAttachmentValidator_ValidatesInputs(
        string fileName,
        string storageKey,
        string contentType,
        long sizeBytes,
        bool expectedValid)
    {
        var validator = new AddEpisodeAttachmentValidator();
        var command = new AddEpisodeAttachmentCommand(fileName, storageKey, contentType, sizeBytes);
        var result = validator.Validate(command);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void AddEpisodeAttachmentValidator_WithForgedExecutableHeader_ReturnsInvalid()
    {
        var validator = new AddEpisodeAttachmentValidator();
        byte[] exeHeader = [0x4D, 0x5A, 0x90, 0x00];
        var command = new AddEpisodeAttachmentCommand("innocent.pdf", "storage/key", "application/pdf", 1024, exeHeader);
        var result = validator.Validate(command);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AddEpisodeAttachmentValidator_ExceedingCustomSizeLimit_ReturnsInvalid()
    {
        var options = Options.Create(new EpisodeAttachmentOptions
        {
            MaxFileSizeBytes = 1024 * 1024 // 1MB
        });

        var validator = new AddEpisodeAttachmentValidator(options);
        var command = new AddEpisodeAttachmentCommand("big_book.pdf", "storage/key", "application/pdf", 2 * 1024 * 1024);
        var result = validator.Validate(command);

        Assert.False(result.IsValid);
    }
}
