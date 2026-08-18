using System;
using System.Collections.Generic;
using System.Linq;
using AlplaPortal.Domain.Constants;

namespace AlplaPortal.Domain.Services;

/// <summary>One source document reduced to what submission validation needs to know.</summary>
public sealed record PaymentSourceDocumentState
{
    public Guid Id { get; init; }
    public int SequenceNumber { get; init; }
    public string Label { get; init; } = string.Empty;

    public bool HasAttachment { get; init; }
    public int? SupplierId { get; init; }
    public int? PlantId { get; init; }
    public string? DocumentNumber { get; init; }
    public string? SourceDocumentType { get; init; }
    public DateTime? DocumentDate { get; init; }

    /// <summary>
    /// When the document must be paid. Mandatory: every line item of a PAYMENT request carries a
    /// due date (enforced at <c>POST /requests/{id}/line-items</c>), and the item takes it from the
    /// document it belongs to. A document without one produces items the API will refuse.
    /// </summary>
    public DateTime? DueDate { get; init; }
    public string? Currency { get; init; }
    public decimal? GrossAmount { get; init; }

    public decimal ItemsTotal { get; init; }
    public int ActiveItemCount { get; init; }

    public string? OcrSuggestion { get; init; }
    public decimal? OcrConfidence { get; init; }
    public bool IndicatesFiscalDocument { get; init; }
    public bool ClassificationConflictAcknowledged { get; init; }
    public string? ClassificationJustification { get; init; }
}

/// <summary>A problem with one document, named so the user knows which card to open.</summary>
public sealed record PaymentSourceDocumentProblem(Guid DocumentId, string Label, string Message);

public sealed record PaymentSourceDocumentValidationResult
{
    public IReadOnlyList<PaymentSourceDocumentProblem> Problems { get; init; } =
        Array.Empty<PaymentSourceDocumentProblem>();

    /// <summary>Sum of the active documents' gross amounts — the request total.</summary>
    public decimal RequestTotal { get; init; }

    /// <summary>Set when the same supplier appears with different document types. Informational.</summary>
    public string? MixedTypeNotice { get; init; }

    public bool CanSubmit => Problems.Count == 0;
}

/// <summary>
/// What a PAYMENT request's source documents must satisfy before it may be submitted.
///
/// <para>Pure: no database, no HTTP. The rules are stated once here and re-checked server-side at
/// submission, so a request cannot be pushed through by a frontend that forgot to ask.</para>
///
/// <para><b>One invalid document blocks the whole request</b>, and every problem names the document
/// it belongs to. A payment request holding three invoices must never report "algo está errado" —
/// the user has to know which card to open.</para>
/// </summary>
public static class PaymentSourceDocumentValidator
{
    /// <summary>
    /// Shown before submission when one supplier appears with different document types. It is
    /// <b>informational</b>: a request paying a proforma and an invoice from the same supplier is
    /// legitimate, and because the type is part of the grouping key the two simply become separate
    /// groups with independent obligations. No justification is required.
    /// </summary>
    public const string MixedTypeMessage =
        "Este pedido contém documentos do mesmo fornecedor com tipos diferentes. " +
        "Cada documento terá obrigações e acompanhamento independentes.";

    public static PaymentSourceDocumentValidationResult Validate(
        IEnumerable<PaymentSourceDocumentState> activeDocuments,
        bool requireClassification)
    {
        var documents = activeDocuments as IReadOnlyList<PaymentSourceDocumentState>
                        ?? activeDocuments.ToList();

        var problems = new List<PaymentSourceDocumentProblem>();

        if (documents.Count == 0)
        {
            return new PaymentSourceDocumentValidationResult
            {
                Problems = new[]
                {
                    new PaymentSourceDocumentProblem(
                        Guid.Empty, "Documentos",
                        "O pedido de pagamento deve conter pelo menos um documento de origem.")
                }
            };
        }

        foreach (var d in documents)
            problems.AddRange(ValidateOne(d, requireClassification));

        // A single currency across the request: without an approved multi-currency rule, summing
        // across currencies would produce a total that means nothing.
        var currencies = documents
            .Select(d => d.Currency?.Trim().ToUpperInvariant())
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .ToList();

        if (currencies.Count > 1)
        {
            problems.Add(new PaymentSourceDocumentProblem(
                Guid.Empty, "Documentos",
                $"Todos os documentos do pedido devem usar a mesma moeda. Encontradas: {string.Join(", ", currencies)}."));
        }

        return new PaymentSourceDocumentValidationResult
        {
            Problems = problems,
            RequestTotal = documents.Sum(d => d.GrossAmount ?? 0m),
            MixedTypeNotice = DetectMixedTypes(documents) ? MixedTypeMessage : null
        };
    }

    private static IEnumerable<PaymentSourceDocumentProblem> ValidateOne(
        PaymentSourceDocumentState d, bool requireClassification)
    {
        PaymentSourceDocumentProblem Problem(string message) => new(d.Id, d.Label, message);

        if (!d.HasAttachment)
            yield return Problem("Anexe o documento.");

        if (d.SupplierId == null)
            yield return Problem("Indique o fornecedor.");

        if (string.IsNullOrWhiteSpace(d.DocumentNumber))
            yield return Problem("Indique o número do documento.");

        if (d.DocumentDate == null)
            yield return Problem("Indique a data do documento.");

        // Checked HERE, at the document, rather than only when its items are created. The item rule
        // is real but fires deep inside persistence, long after the user has confirmed the document
        // and pressed "Gerar Pedido" — which is how an incomplete document reached a request that
        // then failed halfway.
        if (d.DueDate == null)
            yield return Problem("Informe a data de vencimento do documento.");

        if (d.PlantId == null)
            yield return Problem("Indique a planta.");

        if (string.IsNullOrWhiteSpace(d.Currency))
            yield return Problem("Indique a moeda.");

        if ((d.GrossAmount ?? 0m) <= 0m)
            yield return Problem("O valor total do documento deve ser maior que zero.");

        if (d.ActiveItemCount == 0)
            yield return Problem("Associe pelo menos um item a este documento.");

        // The document's own value must be attributable to what is being bought. Without this the
        // group totals derived from items would silently disagree with the amount being paid.
        if ((d.GrossAmount ?? 0m) > 0m && d.ActiveItemCount > 0)
        {
            var tolerance = OperationInvoiceTolerance.For(d.GrossAmount!.Value);
            if (Math.Abs(d.ItemsTotal - d.GrossAmount.Value) > tolerance)
            {
                yield return Problem(
                    $"A soma dos itens ({d.ItemsTotal:N2}) não corresponde ao total do documento " +
                    $"({d.GrossAmount.Value:N2}).");
            }
        }

        // ── Classification ──
        var type = RequestConstants.SourceDocumentTypes.Normalize(d.SourceDocumentType);

        if (type == null)
        {
            if (requireClassification)
                yield return Problem("Indique o tipo de documento anexado.");
            yield break;
        }

        if (!RequestConstants.SourceDocumentTypes.IsValid(type))
        {
            yield return Problem("O tipo de documento anexado é inválido.");
            yield break;
        }

        var obligations = DocumentObligationResolver.Resolve(type, DocumentUsageContext.PaymentRequest);
        if (!obligations.CanInitiatePayment)
        {
            yield return Problem(
                $"{RequestConstants.SourceDocumentTypes.DisplayName(type)}: " +
                (obligations.BlockingReason ?? "este documento não pode originar um pedido de pagamento."));
            yield break;
        }

        // A contradiction of the reading must have been confirmed, and justified where it matters.
        var conflict = DocumentClassificationOverrideRecorder.Evaluate(
            new DocumentClassificationOverrideRequest
            {
                Context = RequestConstants.DocumentClassificationContexts.PaymentRequest,
                ScopeId = d.Id == Guid.Empty ? Guid.NewGuid() : d.Id,
                SuggestedType = d.OcrSuggestion,
                SelectedType = type,
                Confidence = d.OcrConfidence,
                Acknowledged = d.ClassificationConflictAcknowledged,
                Justification = d.ClassificationJustification
            });

        if (conflict.RejectionReason != null)
            yield return Problem(conflict.RejectionReason);
    }

    /// <summary>
    /// True when one supplier appears with more than one document type. Deliberately supplier-scoped:
    /// two different suppliers with different types is unremarkable and warrants no notice.
    /// </summary>
    private static bool DetectMixedTypes(IReadOnlyList<PaymentSourceDocumentState> documents) =>
        documents
            .Where(d => d.SupplierId != null && d.SourceDocumentType != null)
            .GroupBy(d => d.SupplierId!.Value)
            .Any(g => g
                .Select(d => RequestConstants.SourceDocumentTypes.Normalize(d.SourceDocumentType))
                .Distinct()
                .Count() > 1);
}
