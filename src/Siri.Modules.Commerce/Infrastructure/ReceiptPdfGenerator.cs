using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Siri.Modules.Commerce.Infrastructure;

public sealed record ReceiptPdfItem(
    string Title,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

/// <summary>
/// The seller printed in the receipt header — always the real, configured legal entity
/// (<see cref="ReceiptSellerOptions"/>). <see cref="BranchLabel"/>, <see cref="ContactEmail"/> and
/// <see cref="Website"/> are optional and omitted from the page when null/blank.
/// </summary>
public sealed record ReceiptSellerInfo(
    string CompanyName,
    string? BranchLabel,
    string TaxId,
    string Address,
    string? ContactEmail,
    string? Website);

public sealed record ReceiptPdfData(
    string DocumentTitle,
    string DocumentNumber,
    string OrderNo,
    string BuyerName,
    string? BuyerTaxId,
    string? BuyerEmail,
    DateTime IssuedAtUtc,
    string PaymentMethod,
    IReadOnlyList<ReceiptPdfItem> Items,
    decimal SubtotalAmount,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    ReceiptSellerInfo Seller);

public static class ReceiptPdfGenerator
{
    static ReceiptPdfGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] GeneratePdf(ReceiptPdfData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Darken3));

                page.Header().Column(col =>
                {
                    col.Spacing(6);

                    col.Item().Row(row =>
                    {
                        // Company Info
                        row.RelativeItem(3).Column(c =>
                        {
                            // Seller identity is the configured legal entity — never a built-in company.
                            var seller = data.Seller;
                            var sellerName = string.IsNullOrWhiteSpace(seller.BranchLabel)
                                ? seller.CompanyName
                                : $"{seller.CompanyName} ({seller.BranchLabel})";

                            c.Item().Text("SIRI UPSKILL ACADEMY").FontSize(16).Bold().FontColor(Colors.Blue.Darken3);
                            c.Item().Text(sellerName).FontSize(10).SemiBold();
                            c.Item().Text($"เลขประจำตัวผู้เสียภาษี: {seller.TaxId}").FontSize(8).FontColor(Colors.Grey.Darken1);
                            c.Item().Text(seller.Address).FontSize(8).FontColor(Colors.Grey.Darken1);

                            var contactParts = new List<string>();
                            if (!string.IsNullOrWhiteSpace(seller.ContactEmail))
                            {
                                contactParts.Add($"อีเมล: {seller.ContactEmail}");
                            }

                            if (!string.IsNullOrWhiteSpace(seller.Website))
                            {
                                contactParts.Add($"เว็บไซต์: {seller.Website}");
                            }

                            if (contactParts.Count > 0)
                            {
                                c.Item().Text(string.Join(" | ", contactParts)).FontSize(8).FontColor(Colors.Grey.Darken1);
                            }
                        });

                        // Document Title & Metadata
                        row.RelativeItem(2).AlignRight().Column(c =>
                        {
                            c.Item().Text(data.DocumentTitle).FontSize(13).Bold().FontColor(Colors.Grey.Darken4);
                            c.Item().Text("ใบเสร็จรับเงิน / ใบกำกับภาษี").FontSize(9).FontColor(Colors.Grey.Medium);
                            c.Item().PaddingTop(4).Text($"เลขที่ / No: {data.DocumentNumber}").FontSize(8).Bold();
                            c.Item().Text($"วันที่ / Date: {data.IssuedAtUtc.AddHours(7):dd/MM/yyyy HH:mm}").FontSize(8);
                            c.Item().Text($"เลขอ้างอิง / Order Ref: {data.OrderNo}").FontSize(8);
                            c.Item().Text($"ช่องทางชำระ / Paid via: {data.PaymentMethod}").FontSize(8);
                        });
                    });

                    col.Item().PaddingVertical(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    // Customer / Buyer Details Box
                    col.Item().Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("ข้อมูลลูกค้า / Customer Information:").FontSize(9).Bold().FontColor(Colors.Grey.Darken3);
                            c.Item().PaddingTop(2).Text($"ชื่อผู้ซื้อ / Name: {data.BuyerName}").FontSize(9);
                            if (!string.IsNullOrWhiteSpace(data.BuyerTaxId))
                            {
                                c.Item().Text($"เลขประจำตัวผู้เสียภาษี / Tax ID: {data.BuyerTaxId}").FontSize(9);
                            }
                            if (!string.IsNullOrWhiteSpace(data.BuyerEmail))
                            {
                                c.Item().Text($"อีเมล / Email: {data.BuyerEmail}").FontSize(9);
                            }
                        });
                    });

                    // Items Table
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(30);
                            columns.RelativeColumn(5);
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        // Table Header
                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("#").FontSize(8).Bold().FontColor(Colors.White);
                            header.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("รายการ / Description").FontSize(8).Bold().FontColor(Colors.White);
                            header.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignCenter().Text("จำนวน / Qty").FontSize(8).Bold().FontColor(Colors.White);
                            header.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight().Text("ราคา / Unit Price").FontSize(8).Bold().FontColor(Colors.White);
                            header.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight().Text("จำนวนเงิน / Amount (THB)").FontSize(8).Bold().FontColor(Colors.White);
                        });

                        // Table Rows
                        for (var i = 0; i < data.Items.Count; i++)
                        {
                            var item = data.Items[i];
                            var bg = i % 2 == 1 ? Colors.Grey.Lighten5 : Colors.White;

                            table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).Text($"{i + 1}").FontSize(8);
                            table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(item.Title).FontSize(8).SemiBold();
                            table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignCenter().Text($"{item.Quantity}").FontSize(8);
                            table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight().Text($"{item.UnitPrice:N2}").FontSize(8);
                            table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6).AlignRight().Text($"{item.LineTotal:N2}").FontSize(8);
                        }
                    });

                    // Summary & Totals
                    col.Item().Row(row =>
                    {
                        row.RelativeItem(3).Column(c =>
                        {
                            c.Item().Text("หมายเหตุ / Notes:").FontSize(8).Bold();
                            c.Item().Text("• เอกสารนี้ออกในรูปแบบอิเล็กทรอนิกส์ (e-Tax Invoice / e-Receipt)").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                            c.Item().Text("• สิทธิ์การเข้าเรียนออนไลน์มีผลทันทีหลังจากชำระเงินสำเร็จ").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                        });

                        row.RelativeItem(2).Column(c =>
                        {
                            c.Item().Row(r =>
                            {
                                r.RelativeItem().Text("รวมเป็นเงิน (Subtotal):").FontSize(8);
                                r.RelativeItem().AlignRight().Text($"{data.SubtotalAmount:N2} ฿").FontSize(8);
                            });

                            if (data.DiscountAmount > 0)
                            {
                                c.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("ส่วนลด (Discount):").FontSize(8).FontColor(Colors.Green.Darken2);
                                    r.RelativeItem().AlignRight().Text($"-{data.DiscountAmount:N2} ฿").FontSize(8).FontColor(Colors.Green.Darken2);
                                });
                            }

                            var netBeforeVat = Math.Max(0, data.TotalAmount - data.TaxAmount);
                            c.Item().Row(r =>
                            {
                                r.RelativeItem().Text("มูลค่าก่อนภาษี (Net Before VAT):").FontSize(8);
                                r.RelativeItem().AlignRight().Text($"{netBeforeVat:N2} ฿").FontSize(8);
                            });

                            c.Item().Row(r =>
                            {
                                r.RelativeItem().Text("ภาษีมูลค่าเพิ่ม 7% (VAT 7%):").FontSize(8);
                                r.RelativeItem().AlignRight().Text($"{data.TaxAmount:N2} ฿").FontSize(8);
                            });

                            c.Item().PaddingTop(2).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                            c.Item().PaddingTop(2).Row(r =>
                            {
                                r.RelativeItem().Text("ยอดรวมสุทธิ (Grand Total):").FontSize(10).Bold().FontColor(Colors.Blue.Darken3);
                                r.RelativeItem().AlignRight().Text($"{data.TotalAmount:N2} ฿").FontSize(10).Bold().FontColor(Colors.Blue.Darken3);
                            });
                        });
                    });
                });

                page.Footer().Column(col =>
                {
                    col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text("SIRI UpSkill — ขอบคุณที่ร่วมเรียนรู้และอัปสกิลไปกับเรา").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                        row.RelativeItem().AlignRight().Text(x =>
                        {
                            x.Span("หน้า / Page ").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                            x.CurrentPageNumber().FontSize(7.5f).FontColor(Colors.Grey.Medium);
                            x.Span(" จาก / of ").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                            x.TotalPages().FontSize(7.5f).FontColor(Colors.Grey.Medium);
                        });
                    });
                });
            });
        });

        return document.GeneratePdf();
    }
}
