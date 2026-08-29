using System.Text;
using Siri.Modules.Catalog.Features;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Catalog;

public sealed class AttachmentFileValidatorTests
{
    private const long DefaultMaxSize = 52_428_800; // 50MB

    // Magic headers
    private static readonly byte[] PdfHeader = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37]; // %PDF-1.7
    private static readonly byte[] ZipHeader = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00]; // PK..
    private static readonly byte[] SevenZipHeader = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04];
    private static readonly byte[] GzipHeader = [0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
    private static readonly byte[] ExeMzHeader = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]; // MZ header
    private static readonly byte[] ElfHeader = [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00]; // .ELF header
    private static readonly byte[] JavaClassHeader = [0xCA, 0xFE, 0xBA, 0xBE, 0x00, 0x00, 0x00, 0x41];
    private static readonly byte[] PlainTextHeader = Encoding.UTF8.GetBytes("Title,Description,Score\nTest,Unit,100\n");

    [Theory]
    [InlineData("slides.pdf", "application/pdf")]
    [InlineData("source-code.zip", "application/zip")]
    [InlineData("document.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("spreadsheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("presentation.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [InlineData("archive.7z", "application/x-7z-compressed")]
    [InlineData("dataset.gz", "application/gzip")]
    [InlineData("data.tar.gz", "application/gzip")]
    [InlineData("diagram.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    [InlineData("photo.jpeg", "image/jpeg")]
    [InlineData("notes.txt", "text/plain")]
    [InlineData("data.csv", "text/csv")]
    [InlineData("schema.json", "application/json")]
    [InlineData("readme.md", "text/markdown")]
    public void Validate_AllowedFileTypesWithValidHeaders_ReturnsSuccess(string fileName, string contentType)
    {
        var headerBytes = GetMatchingHeader(fileName);
        var result = AttachmentFileValidator.Validate(fileName, contentType, 1024, headerBytes, DefaultMaxSize);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
    }

    [Fact]
    public void Validate_ForgedHeader_PdfNamedFileWithExecutableMzHeader_ReturnsFailure()
    {
        var result = AttachmentFileValidator.Validate("lecture_notes.pdf", "application/pdf", 1024, ExeMzHeader, DefaultMaxSize);

        Assert.True(result.IsFailure);
        Assert.Contains("ไฟล์ปฏิบัติการ", result.Error.Message);
    }

    [Fact]
    public void Validate_ForgedHeader_PdfNamedFileWithElfHeader_ReturnsFailure()
    {
        var result = AttachmentFileValidator.Validate("lecture_notes.pdf", "application/pdf", 1024, ElfHeader, DefaultMaxSize);

        Assert.True(result.IsFailure);
        Assert.Contains("ไฟล์ปฏิบัติการ", result.Error.Message);
    }

    [Fact]
    public void Validate_ForgedHeader_PdfNamedFileWithJavaClassHeader_ReturnsFailure()
    {
        var result = AttachmentFileValidator.Validate("lecture_notes.pdf", "application/pdf", 1024, JavaClassHeader, DefaultMaxSize);

        Assert.True(result.IsFailure);
        Assert.Contains("ไฟล์ปฏิบัติการ", result.Error.Message);
    }

    [Fact]
    public void Validate_MismatchedHeader_PdfNamedFileWithPngHeader_ReturnsFailure()
    {
        var result = AttachmentFileValidator.Validate("lecture_notes.pdf", "application/pdf", 1024, PngHeader, DefaultMaxSize);

        Assert.True(result.IsFailure);
        Assert.Contains("PDF", result.Error.Message);
    }

    [Fact]
    public void Validate_MismatchedHeader_JpgNamedFileWithZipHeader_ReturnsFailure()
    {
        var result = AttachmentFileValidator.Validate("photo.jpg", "image/jpeg", 1024, ZipHeader, DefaultMaxSize);

        Assert.True(result.IsFailure);
        Assert.Contains("JPEG", result.Error.Message);
    }

    [Fact]
    public void Validate_TextFileWithNullBytes_ReturnsFailure()
    {
        byte[] corruptedText = [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x00, 0x57, 0x6F, 0x72, 0x6C, 0x64];
        var result = AttachmentFileValidator.Validate("readme.txt", "text/plain", 1024, corruptedText, DefaultMaxSize);

        Assert.True(result.IsFailure);
        Assert.Contains("Null byte", result.Error.Message);
    }

    [Theory]
    [InlineData("script.exe")]
    [InlineData("hack.bat")]
    [InlineData("exploit.sh")]
    [InlineData("script.ps1")]
    [InlineData("trojan.dll")]
    [InlineData("installer.msi")]
    [InlineData("app.apk")]
    [InlineData("macro.vbs")]
    public void Validate_BlockedExtension_ReturnsFailure(string fileName)
    {
        var result = AttachmentFileValidator.Validate(fileName, "application/octet-stream", 1024, [], DefaultMaxSize);

        Assert.True(result.IsFailure);
        Assert.Contains("ไม่อนุญาต", result.Error.Message);
    }

    [Fact]
    public void Validate_FileExceedingMaxSize_ReturnsFailure()
    {
        const long maxSize = 10 * 1024 * 1024; // 10MB
        const long overSize = 11 * 1024 * 1024; // 11MB

        var result = AttachmentFileValidator.Validate("notes.pdf", "application/pdf", overSize, PdfHeader, maxSize);

        Assert.True(result.IsFailure);
        Assert.Contains("เกินเพดาน", result.Error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Validate_NonPositiveSizeBytes_ReturnsFailure(long sizeBytes)
    {
        var result = AttachmentFileValidator.Validate("notes.pdf", "application/pdf", sizeBytes, PdfHeader, DefaultMaxSize);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("", "application/pdf")]
    [InlineData("   ", "application/pdf")]
    [InlineData("notes.pdf", "")]
    [InlineData("notes.pdf", "   ")]
    public void Validate_EmptyFileNameOrContentType_ReturnsFailure(string fileName, string contentType)
    {
        var result = AttachmentFileValidator.Validate(fileName, contentType, 1024, PdfHeader, DefaultMaxSize);

        Assert.True(result.IsFailure);
    }

    private static byte[] GetMatchingHeader(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => PdfHeader,
            ".zip" or ".docx" or ".xlsx" or ".pptx" => ZipHeader,
            ".7z" => SevenZipHeader,
            ".gz" => GzipHeader,
            ".png" => PngHeader,
            ".jpg" or ".jpeg" => JpegHeader,
            ".txt" or ".csv" or ".json" or ".md" => PlainTextHeader,
            _ => fileName.EndsWith(".tar.gz") ? GzipHeader : []
        };
    }
}
