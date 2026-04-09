namespace AlplaPortal.Application.DTOs.Extraction;

public class ExtractionHeaderDto
{
    public string? SupplierName { get; set; }
    public string? SupplierTaxId { get; set; }
    public string? BilledCompanyName { get; set; }
    public string? DocumentNumber { get; set; }
    public string? DocumentDate { get; set; }
    public string? Currency { get; set; }
    public decimal? TotalAmount { get; set; }
    public decimal? GrandTotal { get; set; }
    public decimal? DiscountAmount { get; set; }
}

public class ExtractionLineItemDto
{
    public int LineNumber { get; set; }
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? TotalPrice { get; set; }
    public decimal? TaxRate { get; set; }
}

public class ExtractionMetadataDto
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public int PagesProcessed { get; set; }
    public int ProcessingDpi { get; set; }
    public string ProcessingFormat { get; set; } = string.Empty;
    public string RoutingStrategy { get; set; } = string.Empty;
    public string DetailMode { get; set; } = "auto";
    public bool NativeTextDetected { get; set; }
    
    // Phase 3 Extensions
    public int ChunkCount { get; set; }
    public bool IsPartial { get; set; }
    public bool ConflictsDetected { get; set; }
}

public class ExtractionContractDto
{
    public string? DocumentType { get; set; }
    public string? Parties { get; set; }
    public string? EffectiveDate { get; set; }
    public string? EndDate { get; set; }
    public string? GoverningLaw { get; set; }
    public string? PaymentTerms { get; set; }
    public string? TerminationClauses { get; set; }
}

public class ExtractionResultDto
{
    public bool Success { get; set; }
    public ExtractionHeaderDto Header { get; set; } = new();
    public List<ExtractionLineItemDto> Items { get; set; } = new();
    public ExtractionContractDto? Contract { get; set; }
    public string? ProviderName { get; set; }
    public decimal QualityScore { get; set; }
    public ExtractionMetadataDto Metadata { get; set; } = new();
}

