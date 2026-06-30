using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AlplaPortal.Domain.Constants;
using AlplaPortal.Domain.Entities;
using AlplaPortal.Infrastructure.Data;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services;

/// <summary>
/// Generates DOCX-based responsibility agreements from the Word template,
/// with equipment assignment data inserted as a header table.
/// Designed so PDF conversion can be added later without rewriting the assignment flow.
/// </summary>
public class ITEquipmentAgreementService
{
    private readonly ILogger<ITEquipmentAgreementService> _logger;
    private readonly string _templateDir;
    private readonly string _storageDir;

    private const string TemplateName = "Novas Politicas de uso de equipamento.docx";

    public ITEquipmentAgreementService(ILogger<ITEquipmentAgreementService> logger, IWebHostEnvironment env, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _logger = logger;

        var templatesRes = AlplaPortal.Infrastructure.Helpers.PathResolutionHelper.ResolvePath(
            env, config, "ITEquipment:TemplatesPath", Path.Combine("data", "templates", "it-equipment"));
        
        var storageRes = AlplaPortal.Infrastructure.Helpers.PathResolutionHelper.ResolvePath(
            env, config, "ITEquipment:StoragePath", Path.Combine("data", "attachments", "it-equipment"));

        _templateDir = templatesRes.ResolvedPath;
        _storageDir = storageRes.ResolvedPath;

        if (!Directory.Exists(_storageDir))
            Directory.CreateDirectory(_storageDir);
    }

    /// <summary>
    /// Result of agreement generation — contains file path and metadata for DB record creation.
    /// </summary>
    public class AgreementResult
    {
        public string FilePath { get; set; } = string.Empty;
        public string StorageFileName { get; set; } = string.Empty;
        public string DisplayFileName { get; set; } = string.Empty;
        public string FileHash { get; set; } = string.Empty;
    }

    /// <summary>
    /// Data required to fill the agreement template.
    /// </summary>
    public class AgreementData
    {
        // Assignee
        public string AssigneeName { get; set; } = string.Empty;
        public string AssigneeEmail { get; set; } = string.Empty;
        public string AssigneeDepartment { get; set; } = string.Empty;
        public string AssigneePlant { get; set; } = string.Empty;

        // Equipment
        public string AssetTag { get; set; } = string.Empty;
        public string? Hostname { get; set; }
        public string EquipmentType { get; set; } = string.Empty;
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public string? MacAddress { get; set; }
        public decimal? PurchaseAmount { get; set; }
        public string? Currency { get; set; }

        // Assignment
        public DateTime AssignedDate { get; set; }
        public string AssignedByName { get; set; } = string.Empty;
        public string AssignedByEmail { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Generate the responsibility agreement DOCX from the base template.
    /// Inserts a data table at the beginning of the document, preserving all original policy content.
    /// </summary>
    [Obsolete("Use ITEquipmentPdfService.GenerateAssignmentPdfAsync instead. This DOCX method is kept for legacy reference only.")]
    public async Task<AgreementResult> GenerateAsync(AgreementData data)
    {
        var templatePath = Path.Combine(_templateDir, TemplateName);

        if (!File.Exists(templatePath))
        {
            _logger.LogError("Agreement template not found at {Path}", templatePath);
            throw new FileNotFoundException(
                "Template do Termo de Responsabilidade não encontrado. Contacte o administrador do sistema.",
                templatePath);
        }

        var fileId = Guid.NewGuid();
        var storageFileName = $"{fileId}.docx";
        var outputPath = Path.Combine(_storageDir, storageFileName);

        // Copy template to output location
        File.Copy(templatePath, outputPath, overwrite: true);

        // Open and modify the copy
        using (var wordDoc = WordprocessingDocument.Open(outputPath, true))
        {
            var body = wordDoc.MainDocumentPart?.Document?.Body;
            if (body == null)
                throw new InvalidOperationException("O template DOCX é inválido — sem corpo de documento.");

            // Build the assignment data block (paragraphs + table) to prepend
            var headerElements = BuildAssignmentHeader(data);

            // Insert all elements at the beginning of the body (before existing content)
            var firstChild = body.FirstChild;
            foreach (var element in headerElements)
            {
                if (firstChild != null)
                    body.InsertBefore(element, firstChild);
                else
                    body.Append(element);
            }

            wordDoc.MainDocumentPart!.Document.Save();
        }

        // Compute file hash
        string fileHash;
        using (var stream = new FileStream(outputPath, FileMode.Open, FileAccess.Read))
        using (var sha256 = SHA256.Create())
        {
            var hashBytes = await sha256.ComputeHashAsync(stream);
            fileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        var displayFileName = $"Termo_Responsabilidade_{data.AssetTag}_{data.AssignedDate:yyyyMMdd}.docx";

        _logger.LogInformation(
            "Agreement generated: {FileName} for equipment {AssetTag} assigned to {Assignee}",
            displayFileName, data.AssetTag, data.AssigneeName);

        return new AgreementResult
        {
            FilePath = outputPath,
            StorageFileName = storageFileName,
            DisplayFileName = displayFileName,
            FileHash = fileHash
        };
    }

    /// <summary>
    /// Builds the OpenXml elements for the assignment header: title, info table, separator.
    /// </summary>
    private static OpenXmlElement[] BuildAssignmentHeader(AgreementData data)
    {
        var elements = new List<OpenXmlElement>();

        // ── Title ──
        elements.Add(CreateParagraph(
            "TERMO DE ENTREGA E RESPONSABILIDADE DE EQUIPAMENTO DE T.I",
            bold: true, fontSize: 24, alignment: JustificationValues.Center, spaceAfter: 200));

        // ── Date line ──
        elements.Add(CreateParagraph(
            $"Data: {data.AssignedDate:dd/MM/yyyy HH:mm} UTC",
            bold: false, fontSize: 18, alignment: JustificationValues.Left, spaceAfter: 200));

        // ── Equipment description ──
        var typeLabel = ITEquipmentConstants.EquipmentType.All.Contains(data.EquipmentType)
            ? data.EquipmentType : "Equipamento";
        var descParts = new[] { typeLabel, data.Manufacturer, data.Model }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var description = string.Join(" ", descParts);
        if (!string.IsNullOrWhiteSpace(data.Hostname)) description += $" — Hostname: {data.Hostname}";
        if (!string.IsNullOrWhiteSpace(data.AssetTag)) description += $" — Asset Tag: {data.AssetTag}";
        if (!string.IsNullOrWhiteSpace(data.SerialNumber)) description += $" — S/N: {data.SerialNumber}";

        // ── Data table ──
        var table = CreateInfoTable(new[]
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
            ("Descrição", description),
            ("Valor de Referência", data.PurchaseAmount.HasValue
                ? $"{data.PurchaseAmount.Value:N2} {data.Currency ?? ""}"
                : "Não especificado"),
            ("Entregue por", data.AssignedByName),
            ("E-mail de quem entrega", data.AssignedByEmail),
            ("Observações", data.Notes ?? "—"),
        });
        elements.Add(table);

        // ── Spacer ──
        elements.Add(CreateParagraph("", bold: false, fontSize: 18, spaceAfter: 300));

        // ── Separator line ──
        elements.Add(CreateParagraph(
            "─────────────────────────────────────────────────",
            bold: false, fontSize: 16, alignment: JustificationValues.Center, spaceAfter: 300));

        // ── "Policy text follows" label ──
        elements.Add(CreateParagraph(
            "POLÍTICA DE USO DE EQUIPAMENTO DE T.I (abaixo)",
            bold: true, fontSize: 20, alignment: JustificationValues.Center, spaceAfter: 400));

        return elements.ToArray();
    }

    /// <summary>Creates a simple two-column table with label-value pairs.</summary>
    private static Table CreateInfoTable((string label, string value)[] rows)
    {
        var table = new Table();

        // Table properties: full width, bordered
        var tblProps = new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "CCCCCC" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "EEEEEE" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "EEEEEE" }
            )
        );
        table.Append(tblProps);

        foreach (var (label, value) in rows)
        {
            var row = new TableRow();

            // Label cell (bold, gray background)
            var labelCell = new TableCell();
            labelCell.Append(new TableCellProperties(
                new TableCellWidth { Width = "2000", Type = TableWidthUnitValues.Pct },
                new Shading { Val = ShadingPatternValues.Clear, Fill = "F2F2F2" }
            ));
            labelCell.Append(CreateParagraph(label, bold: true, fontSize: 18));
            row.Append(labelCell);

            // Value cell
            var valueCell = new TableCell();
            valueCell.Append(new TableCellProperties(
                new TableCellWidth { Width = "3000", Type = TableWidthUnitValues.Pct }
            ));
            valueCell.Append(CreateParagraph(value, bold: false, fontSize: 18));
            row.Append(valueCell);

            table.Append(row);
        }

        return table;
    }

    /// <summary>Creates a styled paragraph.</summary>
    private static Paragraph CreateParagraph(string text, bool bold, int fontSize,
        JustificationValues? alignment = null, int? spaceAfter = null)
    {
        var run = new Run();
        var runProps = new RunProperties();
        runProps.Append(new RunFonts { Ascii = "Arial", HighAnsi = "Arial" });
        runProps.Append(new FontSize { Val = fontSize.ToString() }); // half-points
        if (bold) runProps.Append(new Bold());
        run.Append(runProps);
        run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

        var para = new Paragraph();
        var paraProps = new ParagraphProperties();
        if (alignment.HasValue) paraProps.Append(new Justification { Val = alignment.Value });
        if (spaceAfter.HasValue) paraProps.Append(new SpacingBetweenLines { After = spaceAfter.Value.ToString() });
        para.Append(paraProps);
        para.Append(run);

        return para;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  RETURN DOCUMENT GENERATION
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Data required to generate the return document.</summary>
    public class ReturnData
    {
        // User who had the equipment
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Plant { get; set; } = string.Empty;

        // Equipment
        public string AssetTag { get; set; } = string.Empty;
        public string? Hostname { get; set; }
        public string EquipmentType { get; set; } = string.Empty;
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? SerialNumber { get; set; }
        public string? MacAddress { get; set; }
        public decimal? PurchaseAmount { get; set; }
        public string? Currency { get; set; }

        // Return details
        public DateTime ReturnDateTime { get; set; }
        public string ReceivedByName { get; set; } = string.Empty;
        public string ReceivedByEmail { get; set; } = string.Empty;
        public string Condition { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Generate a return document DOCX programmatically (no template).
    /// </summary>
    [Obsolete("Use ITEquipmentPdfService.GenerateReturnPdfAsync instead. This DOCX method is kept for legacy reference only.")]
    public async Task<AgreementResult> GenerateReturnDocumentAsync(ReturnData data)
    {
        var fileId = Guid.NewGuid();
        var storageFileName = $"{fileId}.docx";
        var outputPath = Path.Combine(_storageDir, storageFileName);

        using (var wordDoc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = new Body();

            // ── Title ──
            body.Append(CreateParagraph(
                "TERMO DE DEVOLUÇÃO DE EQUIPAMENTO DE T.I",
                bold: true, fontSize: 26, alignment: JustificationValues.Center, spaceAfter: 300));

            // ── Date line ──
            body.Append(CreateParagraph(
                $"Data e Hora da Devolução: {data.ReturnDateTime:dd/MM/yyyy HH:mm} UTC",
                bold: false, fontSize: 18, alignment: JustificationValues.Left, spaceAfter: 200));

            // ── Equipment description ──
            var typeLabel = ITEquipmentConstants.EquipmentType.All.Contains(data.EquipmentType)
                ? data.EquipmentType : "Equipamento";
            var descParts = new[] { typeLabel, data.Manufacturer, data.Model }
                .Where(s => !string.IsNullOrWhiteSpace(s));
            var description = string.Join(" ", descParts);
            if (!string.IsNullOrWhiteSpace(data.Hostname)) description += $" — Hostname: {data.Hostname}";

            // ── Condition label in Portuguese ──
            var conditionLabel = data.Condition?.ToUpper() switch
            {
                "GOOD" => "Em bom estado",
                "DAMAGED" => "Danificado",
                "NEEDS_REPAIR" => "Necessita conserto",
                _ => data.Condition ?? "—"
            };

            // ── Data table ──
            var table = CreateInfoTable(new[]
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
                ("Moeda", data.Currency ?? "—"),
                ("Data e Hora da Devolução", $"{data.ReturnDateTime:dd/MM/yyyy HH:mm} UTC"),
                ("Recebido por", data.ReceivedByName),
                ("E-mail de quem recebeu", data.ReceivedByEmail),
                ("Condição na Devolução", conditionLabel),
                ("Observações da Devolução", string.IsNullOrWhiteSpace(data.Notes) ? "Sem observações." : data.Notes),
            });
            body.Append(table);

            // ── Spacer ──
            body.Append(CreateParagraph("", bold: false, fontSize: 18, spaceAfter: 300));

            // ── Formal declaration ──
            body.Append(CreateParagraph(
                "Declara-se que o equipamento acima identificado foi devolvido ao departamento de T.I na data e hora indicadas neste documento.",
                bold: false, fontSize: 18, alignment: JustificationValues.Both, spaceAfter: 200));

            body.Append(CreateParagraph(
                "A condição do equipamento foi registada conforme informado no momento da devolução. Caso sejam identificados danos, inconsistências ou pendências após análise técnica, o departamento de T.I poderá atualizar o histórico do equipamento e tomar as medidas aplicáveis conforme as políticas internas da empresa.",
                bold: false, fontSize: 18, alignment: JustificationValues.Both, spaceAfter: 400));

            // ── Signature lines ──
            body.Append(CreateParagraph("", bold: false, fontSize: 18, spaceAfter: 600));

            body.Append(CreateParagraph(
                "___________________________________________",
                bold: false, fontSize: 18, alignment: JustificationValues.Center, spaceAfter: 40));
            body.Append(CreateParagraph(
                data.UserName,
                bold: true, fontSize: 18, alignment: JustificationValues.Center, spaceAfter: 40));
            body.Append(CreateParagraph(
                "Utilizador que devolveu o equipamento",
                bold: false, fontSize: 16, alignment: JustificationValues.Center, spaceAfter: 400));

            body.Append(CreateParagraph(
                "___________________________________________",
                bold: false, fontSize: 18, alignment: JustificationValues.Center, spaceAfter: 40));
            body.Append(CreateParagraph(
                data.ReceivedByName,
                bold: true, fontSize: 18, alignment: JustificationValues.Center, spaceAfter: 40));
            body.Append(CreateParagraph(
                "Recebido por (Departamento de T.I)",
                bold: false, fontSize: 16, alignment: JustificationValues.Center));

            mainPart.Document.Append(body);
            mainPart.Document.Save();
        }

        // Compute file hash
        string fileHash;
        using (var stream = new FileStream(outputPath, FileMode.Open, FileAccess.Read))
        using (var sha256 = SHA256.Create())
        {
            var hashBytes = await sha256.ComputeHashAsync(stream);
            fileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        var displayFileName = $"Termo_Devolucao_{data.AssetTag}_{data.ReturnDateTime:yyyyMMdd}.docx";

        _logger.LogInformation(
            "Return document generated: {FileName} for equipment {AssetTag} returned by {User}",
            displayFileName, data.AssetTag, data.UserName);

        return new AgreementResult
        {
            FilePath = outputPath,
            StorageFileName = storageFileName,
            DisplayFileName = displayFileName,
            FileHash = fileHash
        };
    }
}

