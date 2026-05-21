using System;
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

    // Fonts
    private static readonly XFont TitleFont = new("Arial", 14, XFontStyle.Bold);
    private static readonly XFont SubTitleFont = new("Arial", 11, XFontStyle.Bold);
    private static readonly XFont NormalFont = new("Arial", 9, XFontStyle.Regular);
    private static readonly XFont NormalBoldFont = new("Arial", 9, XFontStyle.Bold);
    private static readonly XFont SmallFont = new("Arial", 7.5, XFontStyle.Regular);
    private static readonly XFont SmallBoldFont = new("Arial", 7.5, XFontStyle.Bold);
    private static readonly XFont HeaderFont = new("Arial", 8, XFontStyle.Regular);

    // Colors
    private static readonly XColor PrimaryColor = XColor.FromArgb(0, 45, 114); // #002D72
    private static readonly XColor LabelBgColor = XColor.FromArgb(242, 242, 242); // #F2F2F2
    private static readonly XColor BorderColor = XColor.FromArgb(200, 200, 200);
    private static readonly XColor LightBorderColor = XColor.FromArgb(230, 230, 230);

    // Layout constants
    private const double PageMargin = 40;
    private const double TableLabelWidth = 160;

    public ITEquipmentPdfService(ILogger<ITEquipmentPdfService> logger, IWebHostEnvironment env)
    {
        _logger = logger;

        // Resolve project root (same pattern as ITEquipmentAgreementService)
        string rootDir = env.ContentRootPath;
        var sep = Path.DirectorySeparatorChar.ToString();
        var srcToken = $"{sep}src{sep}";
        var srcIdx = rootDir.IndexOf(srcToken, StringComparison.OrdinalIgnoreCase);
        if (srcIdx > 0)
        {
            rootDir = rootDir.Substring(0, srcIdx);
        }
        else
        {
            rootDir = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", ".."));
        }

        _templateDir = Path.GetFullPath(Path.Combine(rootDir, "data", "templates", "it-equipment"));
        _brandingDir = Path.GetFullPath(Path.Combine(rootDir, "data", "templates", "branding"));
        _storageDir = Path.GetFullPath(Path.Combine(rootDir, "data", "attachments", "it-equipment"));

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

            // ── Date line ──
            y += 6;
            gfx.DrawString($"Data: {data.AssignedDate:dd/MM/yyyy HH:mm} UTC",
                NormalFont, XBrushes.Black, new XPoint(PageMargin, y));
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
                ("Asset Tag", data.AssetTag),
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
            y = DrawPolicyText(document, gfx, ref page, y, contentWidth, policyLines);

            // ── Signature lines ──
            y = EnsureSpace(document, ref page, ref gfx, y, 120, contentWidth);
            y += 30;

            DrawSignatureLine(gfx, page, y, contentWidth,
                data.AssigneeName, "Utilizador");
            y += 60;

            DrawSignatureLine(gfx, page, y, contentWidth,
                data.AssignedByName, "Responsável — Departamento de T.I");

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
                ("Asset Tag", data.AssetTag),
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
            var tf = new XTextFormatter(gfx) { Alignment = XParagraphAlignment.Justify };

            var decl1 = "Declara-se que o equipamento acima identificado foi devolvido ao departamento de T.I na data e hora indicadas neste documento.";
            var rect1 = new XRect(PageMargin, y, contentWidth, 40);
            tf.DrawString(decl1, NormalFont, XBrushes.Black, rect1);
            y += 30;

            var decl2 = "A condição do equipamento foi registada conforme informado no momento da devolução. Caso sejam identificados danos, inconsistências ou pendências após análise técnica, o departamento de T.I poderá atualizar o histórico do equipamento e tomar as medidas aplicáveis conforme as políticas internas da empresa.";
            var rect2 = new XRect(PageMargin, y, contentWidth, 60);
            tf.DrawString(decl2, NormalFont, XBrushes.Black, rect2);
            y += 50;

            // ── Signature lines ──
            y = EnsureSpace(document, ref page, ref gfx, y, 120, contentWidth);
            y += 20;

            DrawSignatureLine(gfx, page, y, contentWidth,
                data.UserName, "Utilizador que devolveu o equipamento");
            y += 60;

            DrawSignatureLine(gfx, page, y, contentWidth,
                data.ReceivedByName, "Recebido por (Departamento de T.I)");

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
    //  SHARED DRAWING HELPERS
    // ═══════════════════════════════════════════════════════════════

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
    private double DrawPolicyText(PdfDocument document, XGraphics gfx, ref PdfPage page,
        double y, double contentWidth, string[] lines)
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                y += 6; // blank line spacing
                continue;
            }

            // Estimate text height (approx 12pt per line of text, ~80 chars per line at font size 7.5)
            int estimatedLines = Math.Max(1, (int)Math.Ceiling(line.Length / 95.0));
            double blockHeight = estimatedLines * 11;

            y = EnsureSpace(document, ref page, ref gfx, y, blockHeight, contentWidth);

            // Determine if this is a section header (starts with a number followed by a period)
            bool isHeader = line.Length > 2 && char.IsDigit(line[0]) && line.Contains('.');
            var font = isHeader ? SmallBoldFont : SmallFont;

            var tf = new XTextFormatter(gfx) { Alignment = XParagraphAlignment.Justify };
            var rect = new XRect(PageMargin, y, contentWidth, blockHeight + 4);
            tf.DrawString(line, font, XBrushes.Black, rect);
            y += blockHeight + 2;
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
    /// Draws a signature line with name and role label, centered.
    /// </summary>
    private void DrawSignatureLine(XGraphics gfx, PdfPage page, double y, double contentWidth,
        string name, string roleLabel)
    {
        double lineWidth = 250;
        double centerX = PageMargin + (contentWidth - lineWidth) / 2;

        gfx.DrawLine(new XPen(XColors.Black, 0.5), centerX, y, centerX + lineWidth, y);
        y += 4;

        gfx.DrawString(name, SmallBoldFont, XBrushes.Black,
            new XRect(PageMargin, y, contentWidth, 12), XStringFormats.TopCenter);
        y += 12;

        gfx.DrawString(roleLabel, SmallFont, XBrushes.Gray,
            new XRect(PageMargin, y, contentWidth, 12), XStringFormats.TopCenter);
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
