using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Siri.Modules.Learning.Infrastructure;

public sealed record CertificatePdfData(
    string StudentName,
    string CourseTitle,
    string SerialNo,
    string VerifyCode,
    DateTime IssuedAtUtc,
    string VerifyUrl);

public static class CertificatePdfGenerator
{
    static CertificatePdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] GeneratePdf(CertificatePdfData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // 1. Generate QR Code bytes using QRCoder
        byte[] qrCodeBytes;
        using (var qrGenerator = new QRCodeGenerator())
        {
            var qrCodeData = qrGenerator.CreateQrCode(data.VerifyUrl, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            qrCodeBytes = qrCode.GetGraphic(5);
        }

        // 2. Build QuestPDF document
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.PageColor(Colors.White);

                page.Content().Border(3).BorderColor(Colors.Amber.Darken2).Padding(24).Column(col =>
                {
                    col.Spacing(10);

                    // Header
                    col.Item().AlignCenter().Text("SIRI UPSKILL ACADEMY")
                        .FontSize(18).Bold().FontColor(Colors.Amber.Darken3);

                    col.Item().AlignCenter().Text("CERTIFICATE OF COMPLETION")
                        .FontSize(26).Bold().FontColor(Colors.Grey.Darken4);

                    col.Item().AlignCenter().Text("ใบประกาศนียบัตรสำเร็จหลักสูตร")
                        .FontSize(13).FontColor(Colors.Grey.Medium);

                    col.Item().PaddingVertical(8).AlignCenter().Text("This is proudly presented to / ขอมอบประกาศนียบัตรฉบับนี้ให้แก่")
                        .FontSize(10).Italic().FontColor(Colors.Grey.Darken1);

                    // Student Name
                    col.Item().AlignCenter().Text(data.StudentName)
                        .FontSize(24).Bold().FontColor(Colors.Blue.Darken3).Underline();

                    col.Item().AlignCenter().Text("for successfully completing the course / สำหรับการเรียนจบหลักสูตรอย่างสมบูรณ์")
                        .FontSize(10).FontColor(Colors.Grey.Darken1);

                    // Course Title
                    col.Item().AlignCenter().Text(data.CourseTitle)
                        .FontSize(18).Bold().FontColor(Colors.Grey.Darken3);

                    col.Item().PaddingTop(12).Row(row =>
                    {
                        // Left: Date & Serial
                        row.RelativeItem().Column(info =>
                        {
                            info.Spacing(4);
                            info.Item().Text($"Issue Date / วันที่ออกใบรับรอง: {data.IssuedAtUtc:dd/MM/yyyy}").FontSize(9).FontColor(Colors.Grey.Darken2);
                            info.Item().Text($"Serial No. / เลขที่: {data.SerialNo}").FontSize(9).FontColor(Colors.Grey.Darken2);
                            info.Item().Text($"Verify Code / รหัสยืนยัน: {data.VerifyCode}").FontSize(9).FontColor(Colors.Grey.Darken2);
                        });

                        // Right: QR Code & Verification info
                        row.AutoItem().Column(qr =>
                        {
                            qr.Item().Width(65).Height(65).Image(qrCodeBytes);
                            qr.Item().AlignCenter().Text("Scan to verify").FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });
                });
            });
        });

        return document.GeneratePdf();
    }
}
