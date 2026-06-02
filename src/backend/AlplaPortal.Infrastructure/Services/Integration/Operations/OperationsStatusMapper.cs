namespace AlplaPortal.Infrastructure.Services.Integration.Operations;

/// <summary>
/// Maps raw AlplaPROD status integer values to Portuguese labels and severity levels.
///
/// Source of truth: Script 14 (OQ1–OQ5) discovery findings.
/// Reference: docs/OPERATIONS_MODULE_ALPLAPROD_DISCOVERY.md Appendix E
///
/// Severity levels:
///   • success  — completed/synced events
///   • info     — normal progress events
///   • warning  — cancelled/discrepancy events
///   • error    — error states
/// </summary>
public static class OperationsStatusMapper
{
    private const string Unknown = "Desconhecido";

    // ─── T_Bestellungen.Status ───

    public static (string Meaning, string Severity) MapBestellungStatus(int? status) => status switch
    {
        1 => ("Rascunho", "info"),
        2 => ("Submetido", "info"),
        3 => ("Cancelado", "warning"),
        5 => ("Parcialmente entregue", "warning"),
        6 => ("Ativo", "info"),
        7 => ("Concluído", "success"),
        8 => ("Concluído", "success"),
        null => (Unknown, "info"),
        _ => ($"{Unknown} ({status})", "info"),
    };

    // ─── T_EAIJournal.IdJournalStatus ───

    public static (string Meaning, string Severity) MapJournalStatus(int? status) => status switch
    {
        11 => ("Pedido EDI criado", "info"),
        62 => ("Entrega / Carregamento", "info"),
        64 => ("Divergência na entrega", "warning"),
        91 => ("Revisão ativa", "info"),
        92 => ("Revisão concluída", "success"),
        93 => ("Informativa", "info"),
        94 => ("Informativa", "info"),
        null => (Unknown, "info"),
        _ => ($"{Unknown} ({status})", "info"),
    };

    // ─── T_EAIJournalSynch.Status ───

    public static (string Meaning, string Severity) MapSynchStatus(int? status) => status switch
    {
        0 => ("Pendente", "info"),
        1 => ("Sincronizado", "success"),
        2 => ("Erro", "error"),
        null => (Unknown, "info"),
        _ => ($"{Unknown} ({status})", "info"),
    };

    // ─── T_Abrufe.AbrufStatus ───

    public static (string Meaning, string Severity) MapAbrufStatus(int? status) => status switch
    {
        1 => ("Aberto", "info"),
        2 => ("Parcial", "info"),
        3 => ("Carregado", "success"),
        null => (Unknown, "info"),
        _ => ($"{Unknown} ({status})", "info"),
    };

    // ─── T_LadeAuftraege.Status ───

    public static (string Meaning, string Severity) MapLadeAuftragStatus(int? status) => status switch
    {
        0 => ("Rascunho", "info"),
        1 => ("Pendente", "info"),
        6 => ("Cancelado", "warning"),
        11 => ("Em Progresso", "info"),
        21 => ("Concluído", "success"),
        null => (Unknown, "info"),
        _ => ($"{Unknown} ({status})", "info"),
    };

    // ─── T_Wareneingaenge.Status ───

    public static (string Meaning, string Severity) MapWareneingangStatus(int? status) => status switch
    {
        0 => ("Pendente", "info"),
        1 => ("Novo", "info"),
        6 => ("Cancelado", "warning"),
        11 => ("Em Progresso", "info"),
        21 => ("Concluído", "success"),
        null => (Unknown, "info"),
        _ => ($"{Unknown} ({status})", "info"),
    };

    // ─── T_LadePlanungen.Status (shared with LadeAuftraege) ───

    public static (string Meaning, string Severity) MapLadePlanungStatus(int? status)
        => MapLadeAuftragStatus(status);

    /// <summary>
    /// Routes status mapping based on the event code (which implies the source table).
    /// Returns (statusMeaning, severity, isCompleted).
    /// </summary>
    public static (string Meaning, string Severity, bool IsCompleted) MapEvent(string eventCode, int? mainStatus)
    {
        var (meaning, severity) = eventCode switch
        {
            "PO_CREATED" => MapBestellungStatus(mainStatus),
            "PO_REVISION" => (mainStatus != null ? $"Revisão {mainStatus}" : Unknown, "info"),
            "EDI_CREATED" => MapJournalStatus(mainStatus),
            "EDI_EXPORTED" => MapJournalStatus(mainStatus),
            "EDI_SYNCED" => MapSynchStatus(mainStatus),
            "CALLOFF_CREATED" => MapAbrufStatus(mainStatus),
            "LOADING_PLANNED" => MapLadePlanungStatus(mainStatus),
            "LOADING_ORDER" => MapLadeAuftragStatus(mainStatus),
            "GR_CREATED" => MapWareneingangStatus(mainStatus),
            "GR_COMPLETED" => MapWareneingangStatus(mainStatus),
            "INHOUSE_DELIVERY" => ("Entrega interna", "info"),
            _ => (Unknown, "info"),
        };

        var isCompleted = eventCode switch
        {
            "GR_COMPLETED" => mainStatus == 21,
            "EDI_SYNCED" => mainStatus == 1,
            "LOADING_ORDER" => mainStatus == 21,
            "INHOUSE_DELIVERY" => true,
            _ => severity == "success",
        };

        return (meaning, severity, isCompleted);
    }

    /// <summary>
    /// Determines whether an event is technical (not typically shown to end users).
    /// </summary>
    public static bool IsTechnicalEvent(string eventCode) => eventCode switch
    {
        "EDI_SYNCED" => true,
        "PO_REVISION" => true,
        _ => false,
    };
}
