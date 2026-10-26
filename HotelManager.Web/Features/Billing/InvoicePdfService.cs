using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HotelManager.Web.Features.Billing;

public sealed class InvoicePdfService : IInvoicePdfService
{
    public byte[] Generate(BillingInvoiceDocumentDto invoice)
    {
        var austrianCulture = CultureInfo.GetCultureInfo("de-AT");

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(style => style.FontSize(10));

                page.Header().Column(column =>
                {
                    column.Item().Text("Rechnung / Invoice").FontSize(20).SemiBold();
                    column.Item().Text($"Nr.: {invoice.InvoiceNumber}");
                    column.Item().Text($"Ausgestellt: {invoice.IssuedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}");
                });

                page.Content().Column(column =>
                {
                    column.Spacing(12);

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text("Leistungserbringer").SemiBold();
                            left.Item().Text(invoice.SellerName);
                            left.Item().Text(invoice.SellerAddress);
                            left.Item().Text($"USt-IdNr.: {invoice.SellerVatId}");
                        });

                        row.RelativeItem().Column(right =>
                        {
                            right.Item().Text("Leistungsempfänger").SemiBold();
                            right.Item().Text(invoice.BuyerName);
                            right.Item().Text(invoice.BuyerAddress);
                            right.Item().Text($"USt-IdNr.: {(string.IsNullOrWhiteSpace(invoice.BuyerVatId) ? "-" : invoice.BuyerVatId)}");
                        });
                    });

                    column.Item().Text(
                        $"Leistungszeitraum: {invoice.ServiceFrom:dd.MM.yyyy} - {invoice.ServiceTo:dd.MM.yyyy} · Fällig am: {invoice.DueDate:dd.MM.yyyy}");

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(1.3f);
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(1.7f);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(CellStyle).Text("Beschreibung");
                            header.Cell().Element(CellStyle).AlignRight().Text("Menge");
                            header.Cell().Element(CellStyle).AlignRight().Text("Netto/Stk");
                            header.Cell().Element(CellStyle).AlignRight().Text("MwSt");
                            header.Cell().Element(CellStyle).AlignRight().Text("Brutto");
                        });

                        foreach (var line in invoice.Lines)
                        {
                            table.Cell().Element(CellStyle).Text(line.Description);
                            table.Cell().Element(CellStyle).AlignRight().Text(line.Quantity.ToString("0.###", austrianCulture));
                            table.Cell().Element(CellStyle).AlignRight().Text(line.UnitNetAmount.ToString("C", austrianCulture));
                            table.Cell().Element(CellStyle).AlignRight().Text($"{line.VatRatePercent:0.##}%");
                            table.Cell().Element(CellStyle).AlignRight().Text(line.GrossAmount.ToString("C", austrianCulture));
                        }

                        static IContainer CellStyle(IContainer container)
                        {
                            return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(2);
                        }
                    });

                    column.Item().Row(row =>
                    {
                        row.RelativeItem();
                        row.ConstantItem(220).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            table.Cell().PaddingVertical(2).Text("Netto");
                            table.Cell().PaddingVertical(2).AlignRight().Text(invoice.NetAmount.ToString("C", austrianCulture));
                            table.Cell().PaddingVertical(2).Text("MwSt");
                            table.Cell().PaddingVertical(2).AlignRight().Text(invoice.VatAmount.ToString("C", austrianCulture));
                            table.Cell().BorderTop(1).PaddingTop(4).Text("Brutto").SemiBold();
                            table.Cell().BorderTop(1).PaddingTop(4).AlignRight().Text(invoice.GrossAmount.ToString("C", austrianCulture)).SemiBold();
                        });
                    });

                    var vatGroups = invoice.Lines
                        .GroupBy(line => line.VatRatePercent)
                        .OrderBy(group => group.Key)
                        .Select(group => new
                        {
                            VatRate = group.Key,
                            Net = group.Sum(line => line.NetAmount),
                            Vat = group.Sum(line => line.VatAmount)
                        })
                        .ToList();

                    column.Item().Text("Steueraufschlüsselung").SemiBold();
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(CellStyle).Text("MwSt-Satz");
                            header.Cell().Element(CellStyle).AlignRight().Text("Netto");
                            header.Cell().Element(CellStyle).AlignRight().Text("MwSt");
                        });

                        foreach (var group in vatGroups)
                        {
                            table.Cell().Element(CellStyle).Text($"{group.VatRate:0.##}%");
                            table.Cell().Element(CellStyle).AlignRight().Text(group.Net.ToString("C", austrianCulture));
                            table.Cell().Element(CellStyle).AlignRight().Text(group.Vat.ToString("C", austrianCulture));
                        }

                        static IContainer CellStyle(IContainer container)
                        {
                            return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).PaddingHorizontal(2);
                        }
                    });
                });

                page.Footer().AlignCenter().Text("Elektronisch erzeugte Rechnung");
            });
        }).GeneratePdf();
    }
}
