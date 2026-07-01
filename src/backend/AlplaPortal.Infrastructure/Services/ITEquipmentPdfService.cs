using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using PdfSharpCore.Drawing;
using PdfSharpCore.Drawing.Layout;
using PdfSharpCore.Pdf;

using Microsoft.Extensions.Configuration;

namespace AlplaPortal.Infrastructure.Services;

/// <summary>
/// Generates branded PDF documents for the I.T Equipment module using PdfSharpCore.
/// Replaces the legacy DOCX generation (ITEquipmentAgreementService) for all new documents.
/// Uses the same AgreementResult / AgreementData / ReturnData DTOs for compatibility.
/// </summary>
public class ITEquipmentPdfService
{
    private readonly ILogger<ITEquipmentPdfService> _logger;
    private readonly string _storageDir;
    private readonly string _brandingDir;
    private readonly string _templateDir;
    private readonly IWebHostEnvironment _env;

    // Fonts
    private static readonly XFont TitleFont = new("Arial", 14, XFontStyle.Bold);
    private static readonly XFont SubTitleFont = new("Arial", 11, XFontStyle.Bold);
    private static readonly XFont NormalFont = new("Arial", 9, XFontStyle.Regular);
    private static readonly XFont NormalBoldFont = new("Arial", 9, XFontStyle.Bold);
    private static readonly XFont SmallFont = new("Arial", 7.5, XFontStyle.Regular);
    private static readonly XFont SmallBoldFont = new("Arial", 7.5, XFontStyle.Bold);
    private static readonly XFont HeaderFont = new("Arial", 8, XFontStyle.Regular);
    private static readonly XFont CompactFont = new("Arial", 6.5, XFontStyle.Regular);
    private static readonly XFont CompactBoldFont = new("Arial", 6.5, XFontStyle.Bold);
    private static readonly XFont AcceptanceFont = new("Arial", 6.5, XFontStyle.Italic);

    // Colors
    private static readonly XColor PrimaryColor = XColor.FromArgb(0, 45, 114); // #002D72
    private static readonly XColor LabelBgColor = XColor.FromArgb(242, 242, 242); // #F2F2F2
    private static readonly XColor BorderColor = XColor.FromArgb(200, 200, 200);
    private static readonly XColor LightBorderColor = XColor.FromArgb(230, 230, 230);

    // Layout constants
    private const double PageMargin = 40;
    private const double TableLabelWidth = 160;

    public ITEquipmentPdfService(ILogger<ITEquipmentPdfService> logger, IWebHostEnvironment env, IConfiguration config)
    {
        _logger = logger;
        _env = env;

        var templatesRes = AlplaPortal.Infrastructure.Helpers.PathResolutionHelper.ResolvePath(
            env, config, "ITEquipment:TemplatesPath", Path.Combine("data", "templates", "it-equipment"));
        
        var brandingRes = AlplaPortal.Infrastructure.Helpers.PathResolutionHelper.ResolvePath(
            env, config, "ITEquipment:BrandingPath", Path.Combine("data", "templates", "branding"));
        
        var storageRes = AlplaPortal.Infrastructure.Helpers.PathResolutionHelper.ResolvePath(
            env, config, "ITEquipment:StoragePath", Path.Combine("data", "attachments", "it-equipment"));

        _templateDir = templatesRes.ResolvedPath;
        _brandingDir = brandingRes.ResolvedPath;
        _storageDir = storageRes.ResolvedPath;

        if (!Directory.Exists(_storageDir))
            Directory.CreateDirectory(_storageDir);
    }

    // ═══════════════════════════════════════════════════════════════
    //  ASSIGNMENT AGREEMENT PDF
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Generate a branded PDF Assignment Agreement (Termo de Responsabilidade).
    /// Requires policy-text.txt — returns error if missing.
    /// </summary>
    public async Task<ITEquipmentAgreementService.AgreementResult> GenerateAssignmentPdfAsync(
        ITEquipmentAgreementService.AgreementData data)
    {
        // ── Require policy text ──
        var policyPath = Path.Combine(_templateDir, "policy-text.txt");
        if (!File.Exists(policyPath))
        {
            _logger.LogError("Policy text file not found at {Path}", policyPath);
            throw new FileNotFoundException(
                "Texto da política de uso de equipamento não encontrado. Contacte o administrador do sistema.",
                policyPath);
        }
        var policyLines = await File.ReadAllLinesAsync(policyPath);

        var fileId = Guid.NewGuid();
        var storageFileName = $"{fileId}.pdf";
        var outputPath = Path.Combine(_storageDir, storageFileName);

        using (var document = new PdfDocument())
        {
            document.Info.Title = "Termo de Entrega e Responsabilidade de Equipamento de T.I";
            document.Info.Author = "Portal Gerencial — Alpla Angola";

            var page = document.AddPage();
            page.Size = PdfSharpCore.PageSize.A4;
            var gfx = XGraphics.FromPdfPage(page);
            double y = PageMargin;
            double contentWidth = page.Width - 2 * PageMargin;

            // ── Branded header ──
            y = DrawBrandedHeader(gfx, page, y, contentWidth,
                "TERMO DE ENTREGA E RESPONSABILIDADE DE EQUIPAMENTO DE T.I");

            // ── Date lines ──
            var documentGeneratedAt = DateTime.UtcNow;
            y += 6;
            gfx.DrawString($"Data de disponibilização ao utilizador: {data.AssignedDate:dd/MM/yyyy}",
                NormalFont, XBrushes.Black, new XPoint(PageMargin, y));
            y += 14;
            gfx.DrawString($"Data do documento: {documentGeneratedAt:dd/MM/yyyy HH:mm} UTC",
                NormalFont, XBrushes.Gray, new XPoint(PageMargin, y));
            y += 18;

            // ── Equipment description ──
            var typeLabel = ITEquipmentConstants.EquipmentType.All.Contains(data.EquipmentType)
                ? data.EquipmentType : "Equipamento";

            // ── Data table ──
            var tableRows = new (string label, string value)[]
            {
                ("Utilizador", data.AssigneeName),
                ("E-mail do Utilizador", data.AssigneeEmail),
                ("Departamento", data.AssigneeDepartment),
                ("Planta", data.AssigneePlant),
                ("Data de disponibilização", $"{data.AssignedDate:dd/MM/yyyy}"),
                ("Código do Ativo", data.AssetTag),
                ("Hostname", data.Hostname ?? "—"),
                ("Tipo de Equipamento", typeLabel),
                ("Fabricante", data.Manufacturer ?? "—"),
                ("Modelo", data.Model ?? "—"),
                ("Número de Série", data.SerialNumber ?? "—"),
                ("Endereço MAC", data.MacAddress ?? "—"),
                ("Valor de Referência", data.PurchaseAmount.HasValue
                    ? $"{data.PurchaseAmount.Value:N2} {data.Currency ?? ""}"
                    : "Não especificado"),
                ("Entregue por", data.AssignedByName),
                ("E-mail de quem entrega", data.AssignedByEmail),
                ("Observações", data.Notes ?? "—"),
            };

            y = DrawInfoTable(gfx, page, y, contentWidth, tableRows);
            y += 10;

            // ── Separator ──
            gfx.DrawLine(new XPen(BorderColor, 0.5), PageMargin, y, PageMargin + contentWidth, y);
            y += 12;

            // ── Policy title ──
            gfx.DrawString("POLÍTICA DE USO DE EQUIPAMENTO DE T.I",
                SubTitleFont, new XSolidBrush(PrimaryColor),
                new XRect(PageMargin, y, contentWidth, 16), XStringFormats.TopCenter);
            y += 22;

            // ── Policy text ──
            y = DrawPolicyText(document, ref gfx, ref page, y, contentWidth, policyLines);

            // ── Signature blocks ──
            y = EnsureSpace(document, ref page, ref gfx, y, 200, contentWidth);
            y += 20;

            // User: empty signature area for manual signing
            y = DrawEmptySignatureBlock(gfx, page, y, contentWidth,
                data.AssigneeName, "Utilizador");
            y += 20;

            // I.T Responsible: generated visual signature
            y = DrawEnhancedSignatureBlock(gfx, page, y, contentWidth,
                data.AssignedByName, "Responsável — Departamento de T.I");
            y += 16;

            // ── Electronic generation statement ──
            y = EnsureSpace(document, ref page, ref gfx, y, 70, contentWidth);
            gfx.DrawLine(new XPen(LightBorderColor, 0.5), PageMargin, y, PageMargin + contentWidth, y);
            y += 8;
            DrawElectronicStatement(gfx, y, contentWidth,
                data.AssigneeName, data.AssigneeEmail, data.AssetTag,
                data.AssignedByName, data.AssignedByEmail,
                documentGeneratedAt, data.AssignedDate);

            // ── Footer ──
            DrawFooter(gfx, page);

            document.Save(outputPath);
        }

        var fileHash = await ComputeFileHashAsync(outputPath);
        var displayFileName = $"Termo_Responsabilidade_{data.AssetTag}_{data.AssignedDate:yyyyMMddHHmm}.pdf";

        _logger.LogInformation(
            "Assignment PDF generated: {FileName} for equipment {AssetTag} assigned to {Assignee}",
            displayFileName, data.AssetTag, data.AssigneeName);

        return new ITEquipmentAgreementService.AgreementResult
        {
            FilePath = outputPath,
            StorageFileName = storageFileName,
            DisplayFileName = displayFileName,
            FileHash = fileHash
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  RETURN DOCUMENT PDF
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Generate a branded PDF Return Document (Termo de Devolução).
    /// Does not require policy-text.txt — uses internal declaration text.
    /// </summary>
    public async Task<ITEquipmentAgreementService.AgreementResult> GenerateReturnPdfAsync(
        ITEquipmentAgreementService.ReturnData data)
    {
        var fileId = Guid.NewGuid();
        var storageFileName = $"{fileId}.pdf";
        var outputPath = Path.Combine(_storageDir, storageFileName);

        var conditionLabel = data.Condition?.ToUpper() switch
        {
            "GOOD" => "Em bom estado",
            "DAMAGED" => "Danificado",
            "NEEDS_REPAIR" => "Necessita conserto",
            _ => data.Condition ?? "—"
        };

        var typeLabel = ITEquipmentConstants.EquipmentType.All.Contains(data.EquipmentType)
            ? data.EquipmentType : "Equipamento";
        var descParts = new[] { typeLabel, data.Manufacturer, data.Model }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var description = string.Join(" ", descParts);
        if (!string.IsNullOrWhiteSpace(data.Hostname)) description += $" — Hostname: {data.Hostname}";

        using (var document = new PdfDocument())
        {
            document.Info.Title = "Termo de Devolução de Equipamento de T.I";
            document.Info.Author = "Portal Gerencial — Alpla Angola";

            var page = document.AddPage();
            page.Size = PdfSharpCore.PageSize.A4;
            var gfx = XGraphics.FromPdfPage(page);
            double y = PageMargin;
            double contentWidth = page.Width - 2 * PageMargin;

            // ── Branded header ──
            y = DrawBrandedHeader(gfx, page, y, contentWidth,
                "TERMO DE DEVOLUÇÃO DE EQUIPAMENTO DE T.I");

            // ── Date line ──
            y += 6;
            gfx.DrawString($"Data e Hora da Devolução: {data.ReturnDateTime:dd/MM/yyyy HH:mm} UTC",
                NormalFont, XBrushes.Black, new XPoint(PageMargin, y));
            y += 18;

            // ── Data table ──
            var tableRows = new (string label, string value)[]
            {
                ("Utilizador", data.UserName),
                ("E-mail do Utilizador", data.UserEmail),
                ("Departamento", data.Department),
                ("Planta", data.Plant),
                ("Código do Ativo", data.AssetTag),
                ("Hostname", data.Hostname ?? "—"),
                ("Tipo de Equipamento", typeLabel),
                ("Fabricante", data.Manufacturer ?? "—"),
                ("Modelo", data.Model ?? "—"),
                ("Número de Série", data.SerialNumber ?? "—"),
                ("Endereço MAC", data.MacAddress ?? "—"),
                ("Descrição do Equipamento", description),
                ("Valor de Referência", data.PurchaseAmount.HasValue
                    ? $"{data.PurchaseAmount.Value:N2} {data.Currency ?? ""}"
                    : "Não especificado"),
                ("Data e Hora da Devolução", $"{data.ReturnDateTime:dd/MM/yyyy HH:mm} UTC"),
                ("Recebido por", data.ReceivedByName),
                ("E-mail de quem recebeu", data.ReceivedByEmail),
                ("Condição na Devolução", conditionLabel),
                ("Observações da Devolução", string.IsNullOrWhiteSpace(data.Notes)
                    ? "Sem observações." : data.Notes),
            };

            y = DrawInfoTable(gfx, page, y, contentWidth, tableRows);
            y += 16;

            // ── Separator ──
            gfx.DrawLine(new XPen(BorderColor, 0.5), PageMargin, y, PageMargin + contentWidth, y);
            y += 14;

            // ── Formal declaration ──
            var decl1 = "Declara-se que o equipamento acima identificado foi devolvido ao departamento de T.I na data e hora indicadas neste documento.";
            y = DrawWrappedText(document, ref page, ref gfx, decl1, NormalFont, y, PageMargin, contentWidth);
            y += 15;

            var decl2 = "A condição do equipamento foi registada conforme informado no momento da devolução. Caso sejam identificados danos, inconsistências ou pendências após análise técnica, o departamento de T.I poderá atualizar o histórico do equipamento e tomar as medidas aplicáveis conforme as políticas internas da empresa.";
            y = DrawWrappedText(document, ref page, ref gfx, decl2, NormalFont, y, PageMargin, contentWidth);
            y += 30;

            // ── Signature blocks ──
            var returnDocGeneratedAt = DateTime.UtcNow;
            y = EnsureSpace(document, ref page, ref gfx, y, 100, contentWidth);
            y += 15;

            double halfWidth = contentWidth / 2;
            double sigY = y;

            // User: empty signature area for manual signing
            double endY1 = DrawEmptySignatureBlockAt(gfx, page, PageMargin, sigY, halfWidth,
                data.UserName, "Utilizador que devolveu");

            // I.T Responsible: generated visual signature
            double endY2 = DrawEnhancedSignatureBlockAt(gfx, page, PageMargin + halfWidth, sigY, halfWidth,
                data.ReceivedByName, "Recebido por (T.I)");

            y = Math.Max(endY1, endY2) + 10;

            // ── Electronic generation statement ──
            y = EnsureSpace(document, ref page, ref gfx, y, 50, contentWidth);
            gfx.DrawLine(new XPen(LightBorderColor, 0.5), PageMargin, y, PageMargin + contentWidth, y);
            y += 5;
            DrawElectronicStatement(gfx, y, contentWidth,
                data.UserName, data.UserEmail, data.AssetTag,
                data.ReceivedByName, data.ReceivedByEmail,
                returnDocGeneratedAt, null);

            // ── Footer ──
            DrawFooter(gfx, page);

            document.Save(outputPath);
        }

        var fileHash = await ComputeFileHashAsync(outputPath);
        var displayFileName = $"Termo_Devolucao_{data.AssetTag}_{data.ReturnDateTime:yyyyMMddHHmm}.pdf";

        _logger.LogInformation(
            "Return PDF generated: {FileName} for equipment {AssetTag} returned by {User}",
            displayFileName, data.AssetTag, data.UserName);

        return new ITEquipmentAgreementService.AgreementResult
        {
            FilePath = outputPath,
            StorageFileName = storageFileName,
            DisplayFileName = displayFileName,
            FileHash = fileHash
        };
    }
    // ═══════════════════════════════════════════════════════════════
    //  GROUPED DELIVERY TERM PDF
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Generate a branded PDF for a grouped delivery term with multiple equipment items.
    /// Reuses branded header, policy text, signature blocks, and footer.
    /// </summary>
    public async Task<ITEquipmentAgreementService.AgreementResult> GenerateDeliveryTermPdfAsync(DeliveryTermData data)
    {
        var policyPath = Path.Combine(_templateDir, "policy-text.txt");
        if (!File.Exists(policyPath))
        {
            _logger.LogError("Policy text file not found at {Path}", policyPath);
            throw new FileNotFoundException(
                "Texto da política de uso de equipamento não encontrado. Contacte o administrador do sistema.",
                policyPath);
        }
        var policyLines = await File.ReadAllLinesAsync(policyPath);

        var fileId = Guid.NewGuid();
        var storageFileName = $"{fileId}.pdf";
        var outputPath = Path.Combine(_storageDir, storageFileName);

        using (var document = new PdfDocument())
        {
            document.Info.Title = $"Termo de Entrega e Responsabilidade — {data.TermNumber}";
            document.Info.Author = "Portal Gerencial — Alpla Angola";

            var page = document.AddPage();
            page.Size = PdfSharpCore.PageSize.A4;
            var gfx = XGraphics.FromPdfPage(page);
            double y = PageMargin;
            double contentWidth = page.Width - 2 * PageMargin;

            // ── Branded header ──
            y = DrawBrandedHeader(gfx, page, y, contentWidth,
                "TERMO DE ENTREGA E RESPONSABILIDADE");

            // ── Term & Date info ──
            var documentGeneratedAt = DateTime.UtcNow;
            y += 6;
            gfx.DrawString($"Nº do Termo: {data.TermNumber}",
                NormalBoldFont, new XSolidBrush(PrimaryColor), new XPoint(PageMargin, y));
            y += 14;
            gfx.DrawString($"Data de disponibilização ao utilizador: {data.DeliveryDate:dd/MM/yyyy}",
                NormalFont, XBrushes.Black, new XPoint(PageMargin, y));
            y += 14;
            gfx.DrawString($"Data do documento: {documentGeneratedAt:dd/MM/yyyy HH:mm} UTC",
                NormalFont, XBrushes.Gray, new XPoint(PageMargin, y));
            y += 18;

            // ── Employee info table ──
            var employeeRows = new (string label, string value)[]
            {
                ("Utilizador", data.EmployeeName),
                ("E-mail do Utilizador", data.EmployeeEmail),
                ("Departamento", data.Department),
                ("Cargo", data.Position),
                ("Planta", data.Plant),
                ("Entregue por", data.DeliveredByName),
                ("E-mail de quem entrega", data.DeliveredByEmail),
                ("Observações", data.Notes ?? "—"),
            };

            y = DrawInfoTable(gfx, page, y, contentWidth, employeeRows);
            y += 16;

            // ── Equipment list section ──
            y = EnsureSpace(document, ref page, ref gfx, y, 60, contentWidth);
            gfx.DrawString("EQUIPAMENTOS ENTREGUES",
                SubTitleFont, new XSolidBrush(PrimaryColor),
                new XRect(PageMargin, y, contentWidth, 16), XStringFormats.TopCenter);
            y += 22;

            // Equipment table header
            y = DrawEquipmentTableHeader(gfx, page, y, contentWidth);

            // Equipment rows
            for (int idx = 0; idx < data.Equipment.Count; idx++)
            {
                y = EnsureSpace(document, ref page, ref gfx, y, 20, contentWidth);
                // Re-draw header on new page
                if (y < PageMargin + 20)
                    y = DrawEquipmentTableHeader(gfx, page, y, contentWidth);

                var item = data.Equipment[idx];
                y = DrawEquipmentTableRow(gfx, page, y, contentWidth, idx + 1, item);
            }

            y += 10;

            // ── Separator ──
            gfx.DrawLine(new XPen(BorderColor, 0.5), PageMargin, y, PageMargin + contentWidth, y);
            y += 12;

            // ── Handwritten Observations Section ──
            y = EnsureSpace(document, ref page, ref gfx, y, 120, contentWidth);
            gfx.DrawString("OBSERVAÇÕES DO UTILIZADOR SOBRE O ESTADO DOS EQUIPAMENTOS",
                SmallBoldFont, new XSolidBrush(PrimaryColor),
                new XRect(PageMargin, y, contentWidth, 12), XStringFormats.TopLeft);
            y += 14;

            string obsHelper = "Utilize o espaço abaixo para registrar, no momento da entrega, qualquer avaria, dano, acessório em falta ou observação relevante sobre o estado dos equipamentos recebidos.";
            y = DrawWrappedText(document, ref page, ref gfx, obsHelper, SmallFont, y, PageMargin, contentWidth);
            y += 10;

            // Draw a bordered box with some lines for writing
            double boxHeight = 80;
            y = EnsureSpace(document, ref page, ref gfx, y, boxHeight, contentWidth);
            var boxRect = new XRect(PageMargin, y, contentWidth, boxHeight);
            gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(252, 252, 252)), boxRect);
            gfx.DrawRectangle(new XPen(BorderColor, 0.5), boxRect);

            // Draw faint lines inside the box
            double lineY = y + 20;
            while (lineY < y + boxHeight - 10)
            {
                gfx.DrawLine(new XPen(LightBorderColor, 0.5), PageMargin + 10, lineY, PageMargin + contentWidth - 10, lineY);
                lineY += 20;
            }

            y += boxHeight + 15;

            // ── Policy title (Always on Page 2) ──
            DrawFooter(gfx, page);
            gfx.Dispose();
            page = document.AddPage();
            page.Size = PdfSharpCore.PageSize.A4;
            gfx = XGraphics.FromPdfPage(page);
            y = PageMargin;

            gfx.DrawString("POLÍTICA DE USO DE EQUIPAMENTO DE T.I",
                SubTitleFont, new XSolidBrush(PrimaryColor),
                new XRect(PageMargin, y, contentWidth, 16), XStringFormats.TopCenter);
            y += 22;

            // ── Policy text ──
            y = DrawPolicyText(document, ref gfx, ref page, y, contentWidth, policyLines);

            // ── Signature blocks ──
            y = EnsureSpace(document, ref page, ref gfx, y, 200, contentWidth);
            y += 20;

            y = DrawEmptySignatureBlock(gfx, page, y, contentWidth,
                data.EmployeeName, "Utilizador");
            y += 20;

            y = DrawEnhancedSignatureBlock(gfx, page, y, contentWidth,
                data.DeliveredByName, "Responsável — Departamento de T.I");
            y += 16;

            // ── Electronic generation statement ──
            y = EnsureSpace(document, ref page, ref gfx, y, 70, contentWidth);
            gfx.DrawLine(new XPen(LightBorderColor, 0.5), PageMargin, y, PageMargin + contentWidth, y);
            y += 8;

            var equipmentSummary = string.Join(", ", data.Equipment.Select(e => e.AssetTag));
            DrawElectronicStatement(gfx, y, contentWidth,
                data.EmployeeName, data.EmployeeEmail,
                $"Termo {data.TermNumber} ({data.Equipment.Count} itens: {equipmentSummary})",
                data.DeliveredByName, data.DeliveredByEmail,
                documentGeneratedAt, data.DeliveryDate);

            // ── Footer ──
            DrawFooter(gfx, page);

            document.Save(outputPath);
        }

        var fileHash = await ComputeFileHashAsync(outputPath);
        var displayFileName = $"Termo_Entrega_{data.TermNumber}_{data.DeliveryDate:yyyyMMdd}.pdf";

        _logger.LogInformation(
            "Delivery term PDF generated: {FileName} for term {TermNumber} with {ItemCount} items assigned to {Employee}",
            displayFileName, data.TermNumber, data.Equipment.Count, data.EmployeeName);

        return new ITEquipmentAgreementService.AgreementResult
        {
            FilePath = outputPath,
            StorageFileName = storageFileName,
            DisplayFileName = displayFileName,
            FileHash = fileHash
        };
    }

    /// <summary>Draw the equipment table header row (10 columns including purchase traceability).</summary>
    private double DrawEquipmentTableHeader(XGraphics gfx, PdfPage page, double y, double contentWidth)
    {
        var colWidths = GetEquipmentColumnWidths(contentWidth);
        var headers = new[] { "#", "Código Imobilizado", "Tipo", "Fabricante", "Modelo", "S/N", "Hostname", "Valor Ref.", "Data Compra", "Documento" };
        double x = PageMargin;

        // Header background
        gfx.DrawRectangle(new XSolidBrush(PrimaryColor),
            PageMargin, y - 2, contentWidth, 16);

        for (int c = 0; c < headers.Length; c++)
        {
            gfx.DrawString(headers[c], CompactBoldFont, XBrushes.White,
                new XRect(x + 2, y, colWidths[c] - 4, 12), XStringFormats.TopLeft);
            x += colWidths[c];
        }

        return y + 16;
    }

    /// <summary>Draw one equipment item row (10 columns including purchase traceability).</summary>
    private double DrawEquipmentTableRow(XGraphics gfx, PdfPage page, double y, double contentWidth,
        int rowNum, DeliveryTermEquipmentItem item)
    {
        var colWidths = GetEquipmentColumnWidths(contentWidth);

        // Format purchase display values
        string purchaseValueDisplay = item.PurchaseInfoUnavailable || !item.PurchaseAmount.HasValue
            ? "Indisponível"
            : $"{item.PurchaseAmount:N2} {item.Currency ?? "AOA"}";
        string purchaseDateDisplay = item.PurchaseInfoUnavailable || !item.AcquisitionDate.HasValue
            ? "Indisponível"
            : item.AcquisitionDate.Value.ToString("dd/MM/yyyy");
        string purchaseDocDisplay = item.PurchaseInfoUnavailable || string.IsNullOrWhiteSpace(item.InvoiceNumber)
            ? "Indisponível"
            : item.InvoiceNumber;

        var values = new[]
        {
            rowNum.ToString(),
            item.AssetTag,
            item.EquipmentType ?? "—",
            item.Manufacturer ?? "—",
            item.Model ?? "—",
            item.SerialNumber ?? "—",
            item.Hostname ?? "—",
            purchaseValueDisplay,
            purchaseDateDisplay,
            purchaseDocDisplay
        };

        double rowHeight = 22;
        double x = PageMargin;

        // Alternating row background
        if (rowNum % 2 == 0)
            gfx.DrawRectangle(new XSolidBrush(LabelBgColor), PageMargin, y - 2, contentWidth, rowHeight);

        // Bottom border
        gfx.DrawLine(new XPen(LightBorderColor, 0.3), PageMargin, y + rowHeight - 2, PageMargin + contentWidth, y + rowHeight - 2);

        var tf = new XTextFormatter(gfx);

        for (int c = 0; c < values.Length; c++)
        {
            var val = values[c] ?? "—";
            tf.DrawString(val, CompactFont, XBrushes.Black,
                new XRect(x + 2, y, colWidths[c] - 4, rowHeight - 2));
            x += colWidths[c];
        }

        return y + rowHeight;
    }

    /// <summary>Column widths for the 10-column equipment table (including purchase traceability).</summary>
    private static double[] GetEquipmentColumnWidths(double contentWidth)
    {
        // #(18), Código Imobilizado(100), Tipo(40), Fabricante(45), Modelo(50), S/N(45), Hostname(45), Valor Ref.(50), Data Compra(45), Documento(remaining)
        double fixedTotal = 18 + 100 + 40 + 45 + 50 + 45 + 45 + 50 + 45;
        double remaining = contentWidth - fixedTotal;
        return new double[] { 18, 100, 40, 45, 50, 45, 45, 50, 45, remaining };
    }

    // ── DTOs for Delivery Term PDF ──

    public class DeliveryTermData
    {
        public string TermNumber { get; set; } = string.Empty;
        public DateTime DeliveryDate { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeEmail { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string Plant { get; set; } = string.Empty;
        public string DeliveredByName { get; set; } = string.Empty;
        public string DeliveredByEmail { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public List<DeliveryTermEquipmentItem> Equipment { get; set; } = new();
    }

    public class DeliveryTermEquipmentItem
    {
        public string EquipmentType { get; set; } = string.Empty;
        public string AssetTag { get; set; } = string.Empty;
        public string? Hostname { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public string? Notes { get; set; }
        // Purchase traceability fields
        public decimal? PurchaseAmount { get; set; }
        public string? Currency { get; set; }
        public DateTime? AcquisitionDate { get; set; }
        /// <summary>General purchase/delivery document reference (invoice, delivery note, etc.).</summary>
        public string? InvoiceNumber { get; set; }
        /// <summary>When true, purchase info is unavailable — display "Indisponível" in PDF.</summary>
        public bool PurchaseInfoUnavailable { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════
    //  GROUPED RETURN TERM PDF
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Generate a branded PDF for a grouped return term when all items from a delivery term are returned.
    /// Links to the original delivery term and lists each returned item with its condition.
    /// </summary>
    public async Task<ITEquipmentAgreementService.AgreementResult> GenerateReturnTermPdfAsync(ReturnTermData data)
    {
        var fileId = Guid.NewGuid();
        var storageFileName = $"{fileId}.pdf";
        var outputPath = Path.Combine(_storageDir, storageFileName);

        using (var document = new PdfDocument())
        {
            document.Info.Title = $"Termo de Devolução — {data.OriginalTermNumber}";
            document.Info.Author = "Portal Gerencial — Alpla Angola";

            var page = document.AddPage();
            page.Size = PdfSharpCore.PageSize.A4;
            var gfx = XGraphics.FromPdfPage(page);
            double y = PageMargin;
            double contentWidth = page.Width - 2 * PageMargin;

            // ── Branded header ──
            y = DrawBrandedHeader(gfx, page, y, contentWidth,
                "TERMO DE DEVOLUÇÃO DE EQUIPAMENTO DE T.I");

            // ── Term & Date info ──
            var documentGeneratedAt = DateTime.UtcNow;
            y += 6;
            gfx.DrawString($"Referência do Termo de Entrega Original: {data.OriginalTermNumber}",
                NormalBoldFont, new XSolidBrush(PrimaryColor), new XPoint(PageMargin, y));
            y += 14;
            gfx.DrawString($"Data de Devolução: {data.ReturnDate:dd/MM/yyyy}",
                NormalFont, XBrushes.Black, new XPoint(PageMargin, y));
            y += 14;
            gfx.DrawString($"Data do documento: {documentGeneratedAt:dd/MM/yyyy HH:mm} UTC",
                NormalFont, XBrushes.Gray, new XPoint(PageMargin, y));
            y += 18;

            // ── Employee info table ──
            var employeeRows = new (string label, string value)[]
            {
                ("Utilizador (Devolvente)", data.EmployeeName),
                ("E-mail do Utilizador", data.EmployeeEmail),
                ("Departamento", data.Department),
                ("Planta", data.Plant),
                ("Nº do Termo de Entrega Original", data.OriginalTermNumber),
                ("Data de Devolução", $"{data.ReturnDate:dd/MM/yyyy}"),
                ("Recebido por (T.I)", data.ReceivedByName),
                ("E-mail de quem recebeu", data.ReceivedByEmail),
                ("Observações", data.Notes ?? "—"),
            };

            y = DrawInfoTable(gfx, page, y, contentWidth, employeeRows);
            y += 16;

            // ── Equipment list section ──
            y = EnsureSpace(document, ref page, ref gfx, y, 50, contentWidth);
            gfx.DrawString("EQUIPAMENTOS DEVOLVIDOS",
                SubTitleFont, new XSolidBrush(PrimaryColor),
                new XRect(PageMargin, y, contentWidth, 14), XStringFormats.TopCenter);
            y += 16;

            // Return equipment table header
            y = DrawReturnEquipmentTableHeader(gfx, page, y, contentWidth);

            // Equipment rows
            for (int idx = 0; idx < data.Equipment.Count; idx++)
            {
                y = EnsureSpace(document, ref page, ref gfx, y, 20, contentWidth);
                if (y < PageMargin + 20)
                    y = DrawReturnEquipmentTableHeader(gfx, page, y, contentWidth);

                var item = data.Equipment[idx];
                y = DrawReturnEquipmentTableRow(gfx, page, y, contentWidth, idx + 1, item);
            }

            y += 6;

            // ── Separator ──
            gfx.DrawLine(new XPen(BorderColor, 0.5), PageMargin, y, PageMargin + contentWidth, y);
            y += 10;

            // ── Formal declaration ──
            var decl1 = "Declara-se que os equipamentos acima identificados foram devolvidos ao departamento de T.I nas condições indicadas neste documento.";
            y = DrawWrappedText(document, ref page, ref gfx, decl1, NormalFont, y, PageMargin, contentWidth);
            y += 10;

            var decl2 = "A condição de cada equipamento foi registada conforme informado no momento da devolução. Caso sejam identificados danos, inconsistências ou pendências após análise técnica, o departamento de T.I poderá atualizar o histórico do equipamento e tomar as medidas aplicáveis conforme as políticas internas da empresa.";
            y = DrawWrappedText(document, ref page, ref gfx, decl2, NormalFont, y, PageMargin, contentWidth);
            y += 15;

            // ── Signature blocks ──
            y = EnsureSpace(document, ref page, ref gfx, y, 100, contentWidth);
            y += 15;

            double halfWidth = contentWidth / 2;
            double sigY = y;

            // User: empty signature area for manual signing
            double endY1 = DrawEmptySignatureBlockAt(gfx, page, PageMargin, sigY, halfWidth,
                data.EmployeeName, "Utilizador que devolveu");

            // I.T Responsible: generated visual signature
            double endY2 = DrawEnhancedSignatureBlockAt(gfx, page, PageMargin + halfWidth, sigY, halfWidth,
                data.ReceivedByName, "Recebido por (T.I)");

            y = Math.Max(endY1, endY2) + 10;

            // ── Electronic generation statement ──
            y = EnsureSpace(document, ref page, ref gfx, y, 50, contentWidth);
            gfx.DrawLine(new XPen(LightBorderColor, 0.5), PageMargin, y, PageMargin + contentWidth, y);
            y += 5;

            var equipmentSummary = string.Join(", ", data.Equipment.Select(e => e.AssetTag));
            DrawElectronicStatement(gfx, y, contentWidth,
                data.EmployeeName, data.EmployeeEmail,
                $"Devolução {data.OriginalTermNumber} ({data.Equipment.Count} itens: {equipmentSummary})",
                data.ReceivedByName, data.ReceivedByEmail,
                documentGeneratedAt, null);

            // ── Footer ──
            DrawFooter(gfx, page);

            document.Save(outputPath);
        }

        var fileHash = await ComputeFileHashAsync(outputPath);
        var displayFileName = $"Termo_Devolucao_{data.OriginalTermNumber}_{data.ReturnDate:yyyyMMdd}.pdf";

        _logger.LogInformation(
            "Return term PDF generated: {FileName} for term {TermNumber} with {ItemCount} items returned by {Employee}",
            displayFileName, data.OriginalTermNumber, data.Equipment.Count, data.EmployeeName);

        return new ITEquipmentAgreementService.AgreementResult
        {
            FilePath = outputPath,
            StorageFileName = storageFileName,
            DisplayFileName = displayFileName,
            FileHash = fileHash
        };
    }

    /// <summary>Draw the return equipment table header row.</summary>
    private double DrawReturnEquipmentTableHeader(XGraphics gfx, PdfPage page, double y, double contentWidth)
    {
        var colWidths = GetReturnEquipmentColumnWidths(contentWidth);
        var headers = new[] { "#", "Código Imobilizado", "Tipo", "Hostname", "Fabricante", "Modelo", "S/N", "Condição" };
        double x = PageMargin;

        gfx.DrawRectangle(new XSolidBrush(PrimaryColor),
            PageMargin, y - 2, contentWidth, 14);

        for (int c = 0; c < headers.Length; c++)
        {
            gfx.DrawString(headers[c], CompactBoldFont, XBrushes.White,
                new XRect(x + 2, y, colWidths[c] - 4, 12), XStringFormats.TopLeft);
            x += colWidths[c];
        }

        return y + 14;
    }

    /// <summary>Draw one return equipment item row.</summary>
    private double DrawReturnEquipmentTableRow(XGraphics gfx, PdfPage page, double y, double contentWidth,
        int rowNum, ReturnTermEquipmentItem item)
    {
        var colWidths = GetReturnEquipmentColumnWidths(contentWidth);

        var conditionLabel = item.ReturnCondition?.ToUpper() switch
        {
            "GOOD" => "Bom estado",
            "DAMAGED" => "Danificado",
            "NEEDS_REPAIR" => "Necessita reparo",
            _ => item.ReturnCondition ?? "—"
        };

        var values = new[]
        {
            rowNum.ToString(),
            item.AssetTag,
            item.EquipmentType ?? "—",
            item.Hostname ?? "—",
            item.Manufacturer ?? "—",
            item.Model ?? "—",
            item.SerialNumber ?? "—",
            conditionLabel
        };

        double rowHeight = 16;
        double x = PageMargin;

        if (rowNum % 2 == 0)
            gfx.DrawRectangle(new XSolidBrush(LabelBgColor), PageMargin, y - 2, contentWidth, rowHeight);

        gfx.DrawLine(new XPen(LightBorderColor, 0.3), PageMargin, y + rowHeight - 2, PageMargin + contentWidth, y + rowHeight - 2);

        var tf = new XTextFormatter(gfx);

        for (int c = 0; c < values.Length; c++)
        {
            var text = values[c] ?? "—";
            tf.DrawString(text, CompactFont, XBrushes.Black,
                new XRect(x + 2, y, colWidths[c] - 4, rowHeight - 2));
            x += colWidths[c];
        }

        return y + rowHeight;
    }

    /// <summary>Column widths for the return equipment table.</summary>
    private static double[] GetReturnEquipmentColumnWidths(double contentWidth)
    {
        // Increase Codigo Imobilizado to 120 and reduce others to avoid overlap
        // #(18), Código Imobilizado(120), Tipo(50), Hostname(60), Fabricante(60), Modelo(60), S/N(60), Condição(remaining)
        double fixedTotal = 18 + 120 + 50 + 60 + 60 + 60 + 60;
        double remaining = contentWidth - fixedTotal;
        return new double[] { 18, 120, 50, 60, 60, 60, 60, remaining };
    }

    // ── DTOs for Return Term PDF ──

    public class ReturnTermData
    {
        public string OriginalTermNumber { get; set; } = string.Empty;
        public DateTime ReturnDate { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeEmail { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Plant { get; set; } = string.Empty;
        public string ReceivedByName { get; set; } = string.Empty;
        public string ReceivedByEmail { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public List<ReturnTermEquipmentItem> Equipment { get; set; } = new();
    }

    public class ReturnTermEquipmentItem
    {
        public string EquipmentType { get; set; } = string.Empty;
        public string AssetTag { get; set; } = string.Empty;
        public string? Hostname { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public string? ReturnCondition { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Draws the branded header: logo (if available) + company name + document title.
    /// Returns the Y position after the header.
    /// </summary>
    private double DrawBrandedHeader(XGraphics gfx, PdfPage page, double y, double contentWidth, string title)
    {
        var logoPath = Path.Combine(_brandingDir, "portal-logo.png");
        double logoWidth = 0;

        if (File.Exists(logoPath))
        {
            try
            {
                using var image = XImage.FromFile(logoPath);
                // Scale logo to max 80px height, maintain aspect ratio
                double maxLogoHeight = 50;
                double scale = maxLogoHeight / image.PixelHeight;
                logoWidth = image.PixelWidth * scale;
                double logoHeight = maxLogoHeight;

                gfx.DrawImage(image, PageMargin, y, logoWidth, logoHeight);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load logo from {Path}. Using text-only header.", logoPath);
                logoWidth = 0;
            }
        }
        else
        {
            _logger.LogWarning(
                "Logo file not found at {Path}. Generating PDF with text-only header. " +
                "Place the Portal Gerencial logo at this path to enable branding.",
                logoPath);
        }

        // Company name — next to logo or at top
        double textX = logoWidth > 0 ? PageMargin + logoWidth + 12 : PageMargin;
        double textWidth = contentWidth - (logoWidth > 0 ? logoWidth + 12 : 0);

        gfx.DrawString("Portal Gerencial — Alpla Angola",
            new XFont("Arial", 11, XFontStyle.Bold), new XSolidBrush(PrimaryColor),
            new XRect(textX, y + 8, textWidth, 16), XStringFormats.TopLeft);

        gfx.DrawString("Módulo de T.I — Gestão de Equipamentos",
            HeaderFont, XBrushes.Gray,
            new XRect(textX, y + 24, textWidth, 14), XStringFormats.TopLeft);

        y += 56;

        // Separator under header
        gfx.DrawLine(new XPen(PrimaryColor, 1.5), PageMargin, y, PageMargin + contentWidth, y);
        y += 14;

        // Document title
        gfx.DrawString(title, TitleFont, new XSolidBrush(PrimaryColor),
            new XRect(PageMargin, y, contentWidth, 20), XStringFormats.TopCenter);
        y += 26;

        return y;
    }

    /// <summary>
    /// Draws a two-column info table (label | value) and returns the Y position after.
    /// </summary>
    private double DrawInfoTable(XGraphics gfx, PdfPage page, double y, double contentWidth,
        (string label, string value)[] rows)
    {
        double valueWidth = contentWidth - TableLabelWidth;
        double rowHeight = 16;

        foreach (var (label, value) in rows)
        {
            // Label cell (gray background)
            var labelRect = new XRect(PageMargin, y, TableLabelWidth, rowHeight);
            gfx.DrawRectangle(new XSolidBrush(LabelBgColor), labelRect);
            gfx.DrawRectangle(new XPen(LightBorderColor, 0.5), labelRect);
            gfx.DrawString(label, SmallBoldFont, XBrushes.Black,
                new XRect(PageMargin + 4, y + 2, TableLabelWidth - 8, rowHeight - 4), XStringFormats.TopLeft);

            // Value cell
            var valueRect = new XRect(PageMargin + TableLabelWidth, y, valueWidth, rowHeight);
            gfx.DrawRectangle(new XPen(LightBorderColor, 0.5), valueRect);
            gfx.DrawString(value, SmallFont, XBrushes.Black,
                new XRect(PageMargin + TableLabelWidth + 4, y + 2, valueWidth - 8, rowHeight - 4), XStringFormats.TopLeft);

            y += rowHeight;
        }

        return y;
    }

    /// <summary>
    /// Draws the policy text, handling page breaks as needed.
    /// </summary>
    private double DrawPolicyText(PdfDocument document, ref XGraphics gfx, ref PdfPage page,
        double y, double contentWidth, string[] lines)
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                y += 8; // blank line spacing
                continue;
            }

            if (line.Contains('•'))
            {
                var parts = line.Split('•');
                if (!string.IsNullOrWhiteSpace(parts[0]))
                {
                    y = DrawWrappedText(document, ref page, ref gfx, parts[0].TrimEnd(), SmallFont, y, PageMargin, contentWidth);
                }
                for (int i = 1; i < parts.Length; i++)
                {
                    y = DrawWrappedText(document, ref page, ref gfx, "• " + parts[i].TrimEnd(), SmallFont, y, PageMargin + 15, contentWidth - 15);
                }
                y += 4; // slight padding after bullet list
            }
            else
            {
                string trimmedLine = line.Trim();
                bool isHeader = trimmedLine.Length > 2 && char.IsDigit(trimmedLine[0]) && trimmedLine.Split(' ')[0].Contains('.');
                var font = isHeader ? SmallBoldFont : SmallFont;
                
                y = DrawWrappedText(document, ref page, ref gfx, trimmedLine, font, y, PageMargin, contentWidth);
                y += 4; // slight padding after paragraphs
            }
        }
        return y;
    }

    /// <summary>
    /// Manually wraps and draws text to avoid PdfSharpCore XTextFormatter justification bugs.
    /// </summary>
    private double DrawWrappedText(PdfDocument document, ref PdfPage page, ref XGraphics gfx,
        string text, XFont font, double y, double x, double width)
    {
        var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        string currentLine = "";
        double lineHeight = font.GetHeight() * 1.2; // 20% line spacing
        
        foreach (var word in words)
        {
            string testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
            double lineWidth = gfx.MeasureString(testLine, font).Width;
            
            if (lineWidth > width && !string.IsNullOrEmpty(currentLine))
            {
                y = EnsureSpace(document, ref page, ref gfx, y, lineHeight, page.Width - 2 * PageMargin);
                gfx.DrawString(currentLine, font, XBrushes.Black, new XRect(x, y, width, lineHeight), XStringFormats.TopLeft);
                y += lineHeight;
                currentLine = word;
            }
            else
            {
                currentLine = testLine;
            }
        }
        
        if (!string.IsNullOrEmpty(currentLine))
        {
            y = EnsureSpace(document, ref page, ref gfx, y, lineHeight, page.Width - 2 * PageMargin);
            gfx.DrawString(currentLine, font, XBrushes.Black, new XRect(x, y, width, lineHeight), XStringFormats.TopLeft);
            y += lineHeight;
        }
        
        return y;
    }

    /// <summary>
    /// Ensures enough vertical space on the current page. If not, adds a new page.
    /// </summary>
    private double EnsureSpace(PdfDocument document, ref PdfPage page, ref XGraphics gfx,
        double y, double requiredHeight, double contentWidth)
    {
        if (y + requiredHeight > page.Height - PageMargin - 20)
        {
            DrawFooter(gfx, page);
            gfx.Dispose();
            page = document.AddPage();
            page.Size = PdfSharpCore.PageSize.A4;
            gfx = XGraphics.FromPdfPage(page);
            y = PageMargin;
        }
        return y;
    }

    /// <summary>
    /// Draws an enhanced signature block: cursive PNG signature + line + printed name + role label.
    /// Returns the Y position after the block.
    /// </summary>
    private double DrawEnhancedSignatureBlock(XGraphics gfx, PdfPage page, double y, double contentWidth,
        string fullName, string roleLabel)
    {
        return DrawEnhancedSignatureBlockAt(gfx, page, PageMargin, y, contentWidth, fullName, roleLabel);
    }

    private double DrawEnhancedSignatureBlockAt(XGraphics gfx, PdfPage page, double x, double y, double width,
        string fullName, string roleLabel)
    {
        double blockCenterX = x + width / 2;
        double lineWidth = Math.Min(260, width - 20);
        double lineStartX = blockCenterX - lineWidth / 2;

        // ── Cursive signature image ──
        try
        {
            using var signatureStream = GenerateSignatureImage(fullName);
            using var signatureImage = XImage.FromStream(() => signatureStream);

            // Scale: max 220px wide, max 36px tall, maintain aspect ratio
            double maxW = Math.Min(220, width - 10), maxH = 36;
            double scale = Math.Min(maxW / signatureImage.PixelWidth, maxH / signatureImage.PixelHeight);
            double imgW = signatureImage.PixelWidth * scale;
            double imgH = signatureImage.PixelHeight * scale;
            double imgX = blockCenterX - imgW / 2;

            gfx.DrawImage(signatureImage, imgX, y, imgW, imgH);
            y += imgH + 2;
        }
        catch (Exception)
        {
            // Fallback: draw name in italic if image generation fails
            var fallbackFont = new XFont("Arial", 14, XFontStyle.Italic);
            gfx.DrawString(fullName, fallbackFont, new XSolidBrush(PrimaryColor),
                new XRect(x, y, width, 20), XStringFormats.TopCenter);
            y += 22;
        }

        // ── Signature line ──
        gfx.DrawLine(new XPen(XColors.Black, 0.5), lineStartX, y, lineStartX + lineWidth, y);
        y += 5;

        // ── Printed name ──
        gfx.DrawString(fullName, SmallBoldFont, XBrushes.Black,
            new XRect(x, y, width, 12), XStringFormats.TopCenter);
        y += 12;

        // ── Role label ──
        gfx.DrawString(roleLabel, SmallFont, XBrushes.Gray,
            new XRect(x, y, width, 12), XStringFormats.TopCenter);
        y += 14;

        return y;
    }

    /// <summary>
    /// Generates a transparent PNG image with the full name rendered in a cursive/script font.
    /// Uses System.Drawing.Common (GDI+) for reliable font rendering on Windows.
    /// </summary>
    private static MemoryStream GenerateSignatureImage(string fullName)
    {
        // Font fallback chain: Segoe Script → Lucida Handwriting → Freestyle Script → Arial (italic)
        var fontFamilies = new[] { "Segoe Script", "Lucida Handwriting", "Freestyle Script" };
        System.Drawing.Font? selectedFont = null;

        foreach (var familyName in fontFamilies)
        {
            try
            {
                var testFont = new System.Drawing.Font(familyName, 24f, System.Drawing.FontStyle.Regular);
                // Verify the font was actually found (GDI+ substitutes silently)
                if (testFont.Name.Equals(familyName, StringComparison.OrdinalIgnoreCase))
                {
                    selectedFont = testFont;
                    break;
                }
                testFont.Dispose();
            }
            catch
            {
                // Font not available, try next
            }
        }

        // Last resort: Arial Italic
        selectedFont ??= new System.Drawing.Font("Arial", 24f, System.Drawing.FontStyle.Italic);

        // Measure the text to create a tight-fitting image
        SizeF textSize;
        using (var measureBmp = new Bitmap(1, 1))
        using (var measureGfx = Graphics.FromImage(measureBmp))
        {
            textSize = measureGfx.MeasureString(fullName, selectedFont);
        }

        int imgWidth = (int)Math.Ceiling(textSize.Width) + 10;
        int imgHeight = (int)Math.Ceiling(textSize.Height) + 6;

        // Create the signature bitmap with transparent background
        using var bitmap = new Bitmap(imgWidth, imgHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // Draw in dark navy blue matching the PDF primary color (#002D72)
            using var brush = new SolidBrush(System.Drawing.Color.FromArgb(0, 45, 114));
            g.DrawString(fullName, selectedFont, brush, 4f, 2f);
        }

        selectedFont.Dispose();

        var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        return ms;
    }

    /// <summary>
    /// Draws the electronic generation statement with audit metadata.
    /// </summary>
    private void DrawElectronicStatement(XGraphics gfx, double y, double contentWidth,
        string userName, string userEmail, string assetTag,
        string responsibleName, string responsibleEmail, DateTime generatedAt,
        DateTime? availabilityDate)
    {
        var lines = new List<string>
        {
            "Documento gerado eletronicamente no Portal Gerencial pelo responsável de T.I.",
            $"Utilizador: {userName} ({userEmail})",
            $"Equipamento: {assetTag}",
            $"Responsável de T.I: {responsibleName} ({responsibleEmail})",
            $"Data do documento: {generatedAt:dd/MM/yyyy HH:mm} UTC"
        };

        if (availabilityDate.HasValue)
        {
            lines.Add($"Data de disponibilização: {availabilityDate.Value:dd/MM/yyyy}");
        }

        foreach (var line in lines)
        {
            gfx.DrawString(line, AcceptanceFont, XBrushes.Gray,
                new XRect(PageMargin, y, contentWidth, 10), XStringFormats.TopCenter);
            y += 10;
        }
    }

    /// <summary>
    /// Draws an empty signature block for manual signing: empty space + signature line + printed name + role label.
    /// No generated cursive signature image.
    /// </summary>
    private double DrawEmptySignatureBlock(XGraphics gfx, PdfPage page, double y, double contentWidth,
        string fullName, string roleLabel)
    {
        return DrawEmptySignatureBlockAt(gfx, page, PageMargin, y, contentWidth, fullName, roleLabel);
    }

    private double DrawEmptySignatureBlockAt(XGraphics gfx, PdfPage page, double x, double y, double width,
        string fullName, string roleLabel)
    {
        double blockCenterX = x + width / 2;
        double lineWidth = Math.Min(260, width - 20);
        double lineStartX = blockCenterX - lineWidth / 2;

        // ── Empty space for manual signature ──
        y += 30;

        // ── Signature line ──
        gfx.DrawLine(new XPen(XColors.Black, 0.5), lineStartX, y, lineStartX + lineWidth, y);
        y += 5;

        // ── Printed name ──
        gfx.DrawString(fullName, SmallBoldFont, XBrushes.Black,
            new XRect(x, y, width, 12), XStringFormats.TopCenter);
        y += 12;

        // ── Role label ──
        gfx.DrawString(roleLabel, SmallFont, XBrushes.Gray,
            new XRect(x, y, width, 12), XStringFormats.TopCenter);
        y += 14;

        return y;
    }

    /// <summary>
    /// Draws a subtle footer at the bottom of the page.
    /// </summary>
    private static void DrawFooter(XGraphics gfx, PdfPage page)
    {
        double footerY = page.Height - 25;
        double contentWidth = page.Width - 2 * PageMargin;

        gfx.DrawLine(new XPen(LightBorderColor, 0.5), PageMargin, footerY - 4,
            PageMargin + contentWidth, footerY - 4);

        gfx.DrawString(
            $"Documento gerado automaticamente — Portal Gerencial — {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC",
            new XFont("Arial", 6.5, XFontStyle.Regular), XBrushes.Gray,
            new XRect(PageMargin, footerY, contentWidth, 10), XStringFormats.TopCenter);
    }

    /// <summary>
    /// Computes SHA256 hash of a file.
    /// </summary>
    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }
}
