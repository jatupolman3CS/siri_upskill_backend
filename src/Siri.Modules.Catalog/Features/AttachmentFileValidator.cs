using System.Text;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features;

/// <summary>
/// Validates episode attachment files against an allowed list of MIME types/extensions,
/// enforces file size limits, and inspects magic byte file signatures to reject forged headers
/// and malicious/executable payloads (P4-03).
/// </summary>
public static class AttachmentFileValidator
{
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".sh", ".ps1", ".vbs", ".msi", ".jar",
        ".com", ".scr", ".pif", ".app", ".apk", ".bin", ".elf", ".iso", ".dmg",
        ".hta", ".cpl", ".reg", ".wsf", ".gadget"
    };

    private static readonly Dictionary<string, string[]> AllowedExtensionsToMimeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ["application/pdf"],
        [".zip"] = ["application/zip", "application/x-zip-compressed", "application/octet-stream"],
        [".7z"] = ["application/x-7z-compressed", "application/x-compressed", "application/octet-stream"],
        [".gz"] = ["application/gzip", "application/x-gzip", "application/octet-stream"],
        [".tar.gz"] = ["application/gzip", "application/x-gzip", "application/x-compressed-tar", "application/octet-stream"],
        [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/zip", "application/octet-stream"],
        [".xlsx"] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/zip", "application/octet-stream"],
        [".pptx"] = ["application/vnd.openxmlformats-officedocument.presentationml.presentation", "application/zip", "application/octet-stream"],
        [".txt"] = ["text/plain", "application/octet-stream"],
        [".csv"] = ["text/csv", "text/plain", "application/vnd.ms-excel", "application/octet-stream"],
        [".json"] = ["application/json", "text/plain", "application/octet-stream"],
        [".md"] = ["text/markdown", "text/x-markdown", "text/plain", "application/octet-stream"],
        [".png"] = ["image/png"],
        [".jpg"] = ["image/jpeg", "image/pjpeg"],
        [".jpeg"] = ["image/jpeg", "image/pjpeg"]
    };

    // Magic byte signatures
    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46]; // %PDF
    private static readonly byte[] ZipMagic1 = [0x50, 0x4B, 0x03, 0x04]; // PK..
    private static readonly byte[] ZipMagic2 = [0x50, 0x4B, 0x05, 0x06]; // PK.. (empty zip)
    private static readonly byte[] ZipMagic3 = [0x50, 0x4B, 0x07, 0x08]; // PK.. (spanned zip)
    private static readonly byte[] SevenZipMagic = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]; // 7z
    private static readonly byte[] GzipMagic = [0x1F, 0x8B];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

    // Executable / malicious signatures to reject unconditionally
    private static readonly byte[] DosMzMagic = [0x4D, 0x5A]; // MZ (Windows EXE / DLL)
    private static readonly byte[] ElfMagic = [0x7F, 0x45, 0x4C, 0x46]; // .ELF (Linux executable)
    private static readonly byte[] JavaClassMagic = [0xCA, 0xFE, 0xBA, 0xBE]; // Java class / Mach-O Fat Binary
    private static readonly byte[] MachOMagic32 = [0xFE, 0xED, 0xFA, 0xCE];
    private static readonly byte[] MachOMagic64 = [0xFE, 0xED, 0xFA, 0xCF];
    private static readonly byte[] MachOMagic32Rev = [0xCE, 0xFA, 0xED, 0xFE];
    private static readonly byte[] MachOMagic64Rev = [0xCF, 0xFA, 0xED, 0xFE];

    public static Result Validate(
        string fileName,
        string contentType,
        long sizeBytes,
        ReadOnlySpan<byte> headerBytes,
        long maxFileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Result.Failure(DomainError.Validation("ชื่อไฟล์ไม่สามารถเป็นค่าว่างได้"));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            return Result.Failure(DomainError.Validation("ContentType ไม่สามารถเป็นค่าว่างได้"));
        }

        if (sizeBytes <= 0)
        {
            return Result.Failure(DomainError.Validation("ขนาดไฟล์ต้องมากกว่า 0 ไบต์"));
        }

        if (sizeBytes > maxFileSizeBytes)
        {
            return Result.Failure(DomainError.Validation(
                $"ขนาดไฟล์เกินเพดานที่กำหนด (สูงสุด {maxFileSizeBytes / (1024 * 1024)} MB)"));
        }

        var extension = GetFileExtension(fileName);
        if (string.IsNullOrEmpty(extension))
        {
            return Result.Failure(DomainError.Validation("ไฟล์ต้องมีนามสกุลที่ถูกต้อง"));
        }

        if (BlockedExtensions.Contains(extension))
        {
            return Result.Failure(DomainError.Validation($"ไม่อนุญาตให้อัปโหลดไฟล์ชนิด {extension} เนื่องจากความปลอดภัย"));
        }

        if (!AllowedExtensionsToMimeMap.TryGetValue(extension, out var allowedMimes))
        {
            return Result.Failure(DomainError.Validation($"ไม่อนุญาตให้อัปโหลดไฟล์นามสกุล {extension}"));
        }

        var normalizedContentType = contentType.Trim().ToLowerInvariant();
        if (!allowedMimes.Any(m => m.Equals(normalizedContentType, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure(DomainError.Validation($"ContentType '{contentType}' ไม่ตรงกับนามสกุลไฟล์ '{extension}'"));
        }

        if (!headerBytes.IsEmpty)
        {
            var signatureResult = ValidateMagicBytes(extension, headerBytes);
            if (signatureResult.IsFailure)
            {
                return signatureResult;
            }
        }

        return Result.Success();
    }

    public static Result ValidateMagicBytes(string extension, ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.IsEmpty)
        {
            return Result.Success();
        }

        // 1. First, check if the header matches any known dangerous/executable signatures
        if (headerBytes.StartsWith(DosMzMagic) ||
            headerBytes.StartsWith(ElfMagic) ||
            headerBytes.StartsWith(JavaClassMagic) ||
            headerBytes.StartsWith(MachOMagic32) ||
            headerBytes.StartsWith(MachOMagic64) ||
            headerBytes.StartsWith(MachOMagic32Rev) ||
            headerBytes.StartsWith(MachOMagic64Rev))
        {
            return Result.Failure(DomainError.Validation("เนื้อหาไฟล์เป็นไฟล์ปฏิบัติการ (Executable) ซึ่งไม่ได้รับอนุญาต"));
        }

        var normalizedExt = extension.Trim().ToLowerInvariant();
        if (normalizedExt.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            normalizedExt = ".gz";
        }

        switch (normalizedExt)
        {
            case ".pdf":
                if (!headerBytes.StartsWith(PdfMagic))
                {
                    return Result.Failure(DomainError.Validation("เนื้อหาไฟล์ไม่ตรงกับรูปแบบเอกสาร PDF (Magic bytes ไม่ถูกต้อง)"));
                }
                break;

            case ".zip":
            case ".docx":
            case ".xlsx":
            case ".pptx":
                if (!headerBytes.StartsWith(ZipMagic1) && !headerBytes.StartsWith(ZipMagic2) && !headerBytes.StartsWith(ZipMagic3))
                {
                    return Result.Failure(DomainError.Validation($"เนื้อหาไฟล์ไม่ตรงกับรูปแบบ {normalizedExt} (Zip magic bytes ไม่ถูกต้อง)"));
                }
                break;

            case ".7z":
                if (!headerBytes.StartsWith(SevenZipMagic))
                {
                    return Result.Failure(DomainError.Validation("เนื้อหาไฟล์ไม่ตรงกับรูปแบบ 7-Zip (Magic bytes ไม่ถูกต้อง)"));
                }
                break;

            case ".gz":
                if (!headerBytes.StartsWith(GzipMagic))
                {
                    return Result.Failure(DomainError.Validation("เนื้อหาไฟล์ไม่ตรงกับรูปแบบ GZip (Magic bytes ไม่ถูกต้อง)"));
                }
                break;

            case ".png":
                if (!headerBytes.StartsWith(PngMagic))
                {
                    return Result.Failure(DomainError.Validation("เนื้อหาไฟล์ไม่ตรงกับรูปแบบรูปภาพ PNG (Magic bytes ไม่ถูกต้อง)"));
                }
                break;

            case ".jpg":
            case ".jpeg":
                if (!headerBytes.StartsWith(JpegMagic))
                {
                    return Result.Failure(DomainError.Validation("เนื้อหาไฟล์ไม่ตรงกับรูปแบบรูปภาพ JPEG (Magic bytes ไม่ถูกต้อง)"));
                }
                break;

            case ".txt":
            case ".csv":
            case ".json":
            case ".md":
                // Text files should not contain binary null bytes in the opening chunk
                if (headerBytes.Contains((byte)0x00))
                {
                    return Result.Failure(DomainError.Validation($"ไฟล์ข้อความ {normalizedExt} มีไบนารีที่ผิดรูปแบบ (ตรวจพบ Null byte)"));
                }
                break;
        }

        return Result.Success();
    }

    /// <summary>
    /// The MIME type the server stores and serves for a validated file, derived from its extension — never the
    /// client-declared one (which was only checked for consistency). <c>null</c> for an extension that is not allowed.
    /// </summary>
    public static string? GetCanonicalContentType(string extension) =>
        !string.IsNullOrEmpty(extension) && AllowedExtensionsToMimeMap.TryGetValue(extension, out var mimes) ? mimes[0] : null;

    public static bool IsAllowedExtension(string fileName)
    {
        var ext = GetFileExtension(fileName);
        return !string.IsNullOrEmpty(ext) &&
               !BlockedExtensions.Contains(ext) &&
               AllowedExtensionsToMimeMap.ContainsKey(ext);
    }

    public static string GetFileExtension(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;

        var name = fileName.Trim();
        if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            return ".tar.gz";
        }

        return Path.GetExtension(name);
    }
}
