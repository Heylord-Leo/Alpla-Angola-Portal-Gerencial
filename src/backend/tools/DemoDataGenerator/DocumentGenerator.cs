using System;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DemoDataGenerator;

public static class DocumentGenerator
{
    static DocumentGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] GenerateProforma(string supplierName, string title, decimal totalAmount)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(12));

                page.Header().Text("PROFORMA INVOICE").SemiBold().FontSize(20).FontColor(Colors.Blue.Darken2);
                
                page.Content().PaddingVertical(1, Unit.Centimetre).Column(x =>
                {
                    x.Spacing(20);
                    x.Item().Text($"Supplier: {supplierName}");
                    x.Item().Text($"Reference: {title}");
                    x.Item().Text($"Date: {DateTime.Now:yyyy-MM-dd}");
                    x.Item().Text($"Total Amount: {totalAmount:C2}");
                    x.Item().Text("This is a mock proforma generated for demonstration purposes.");
                });
            });
        }).GeneratePdf();
    }

    public static byte[] GeneratePurchaseOrder(string supplierName, string requestNumber, decimal totalAmount)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(12));

                page.Header().Text("PURCHASE ORDER").SemiBold().FontSize(20).FontColor(Colors.Green.Darken2);
                
                page.Content().PaddingVertical(1, Unit.Centimetre).Column(x =>
                {
                    x.Spacing(20);
                    x.Item().Text($"P.O. Number: {requestNumber}");
                    x.Item().Text($"Supplier: {supplierName}");
                    x.Item().Text($"Date: {DateTime.Now:yyyy-MM-dd}");
                    x.Item().Text($"Total Approved: {totalAmount:C2}");
                    x.Item().Text("Mock Purchase Order document for demo flow.");
                });
            });
        }).GeneratePdf();
    }

    public static byte[] GeneratePaymentProof(string requestNumber, string type, decimal amount)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(12));

                string headerText = type == "SCHEDULE" ? "PAYMENT SCHEDULING PROOF" : "FINAL PAYMENT PROOF";

                page.Header().Text(headerText).SemiBold().FontSize(20).FontColor(Colors.Orange.Darken2);
                
                page.Content().PaddingVertical(1, Unit.Centimetre).Column(x =>
                {
                    x.Spacing(20);
                    x.Item().Text($"Related P.O.: {requestNumber}");
                    x.Item().Text($"Date: {DateTime.Now:yyyy-MM-dd}");
                    x.Item().Text($"Processed Amount: {amount:C2}");
                    
                    x.Item().PaddingTop(20).Text("Banking Details:").SemiBold();
                    x.Item().Text("IBAN: MOCK AO06 0000 0000 0000 0000 0000 0");
                    x.Item().Text("SWIFT: MOCKAOXX");
                    x.Item().Text("REF: MOCK-BANK-REF-2026-0001");
                });
            });
        }).GeneratePdf();
    }
}
