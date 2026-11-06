using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AlplaPortal.Application.DTOs.Operations;
using AlplaPortal.Infrastructure.Services.Integration.Operations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlplaPortal.Infrastructure.Services.Operations;

/// <summary>
/// Catalogue of the items that can be placed on a machine card, with the read-only
/// data-source metadata shown in the design editor.
///
/// The catalogue is the maintained source of truth for the user-facing description
/// (table, field, filters, calculation, caveats). It never stores SQL text: on first
/// use (and on every catalogue request) the entries are VERIFIED against the SQL the
/// query builder really produces with the live options — the alias must be defined in
/// that query and the declared table and field must take part in it — and the expression
/// shown as optional technical detail is extracted from that rendered text at that moment.
/// Figures computed by the service (deviation, unit estimates, status) are verified by the
/// presence of the method on the service type. Payload fields are resolved against the
/// DTO JSON names. Anything that fails is reported as NOT_VERIFIED, never shown as fact.
///
/// No connection detail is involved: the builder renders text only; nothing is executed.
/// </summary>
public static class Viana3CardItemCatalog
{
    public const string Q1 = "Q1";
    public const string Q2 = "Q2";
    public const string Service = "SERVICE";

    private static readonly Regex FreeKeyPattern = new("^free-(text|image)-([1-9]|1[0-9])$", RegexOptions.Compiled);

    public sealed record Source(string PayloadField, string Table, string Field, string Query, string Alias, string[]? Filters = null);

    public sealed record Item(
        string Key, string Label, string Group, string Kind, bool DataDriven, bool Rotatable, string Description,
        Source[] Sources, string? Calculation = null, string[]? Caveats = null, string[]? ConfiguredOptions = null);

    public static bool IsFreeKey(string key) => FreeKeyPattern.IsMatch(key);

    public static bool IsFreeImageKey(string key) => key.StartsWith("free-image-", StringComparison.Ordinal);

    public static readonly IReadOnlyList<Item> Items = new[]
    {
        new Item("machineName", "Nome da máquina / linha", "Cabeçalho", "TEXT", true, false,
            "Nome da área de máquina (linha) tal como registado no AlplaPROD.",
            new[] { new Source("machineName", "T_LagerAbteilungen", "Bezeichnung", Q1, "MachineName", new[] { "IdLagerAbteilung = IdMaschine do planeamento" }) }),

        new Item("status", "Estado da máquina", "Cabeçalho", "CHIP", true, false,
            "Em produção / Manutenção / Sem ordem / Lote reservado / Sem lote planeado agora, derivado do tipo do lote atual.",
            new[] { new Source("statusCode", "T_Artikelvarianten", "Bezeichnung", Q1, "LotKind", new[] { "lote atual = PlanVon ≤ agora < PlanBis", "tipo por prefixo do nome do artigo ($Manutencao…, $No-Order…, $Placeholder…)" }),
                    new Source("statusLabel", "", "", Service, "DeriveMachineStatus") },
            "Production → Em produção; Maintenance → Manutenção; NoOrder → Sem ordem; Placeholder → Lote reservado; sem lote → Sem lote planeado agora."),

        new Item("lotLine", "Lote e garrafa", "Cabeçalho", "TEXT", true, false,
            "Número do lote (IdProdPlanung) e artigo da garrafa do lote atual.",
            new[] { new Source("current.lotId", "T_ProdPlanung", "IdProdPlanung", Q1, "Lot"),
                    new Source("current.bottleArticle", "T_Artikelvarianten", "Bezeichnung", Q1, "Bottle", new[] { "IdArtikelvarianten = T_ProdPlanung.IdArtikelvarianten" }) }),

        new Item("planWindow", "Janela de planeamento", "Cabeçalho", "TEXT", true, false,
            "Início e fim planeados do lote atual (hora de Angola, tal como registada).",
            new[] { new Source("current.planFrom", "T_ProdPlanung", "PlanVon", Q1, "PlanVon"),
                    new Source("current.planTo", "T_ProdPlanung", "PlanBis", Q1, "PlanBis") },
            null, new[] { "Datas transportadas sem conversão de fuso horário." }),

        new Item("illustration", "Ilustração da linha", "Ilustração", "ILLUSTRATION", false, true,
            "Desenho da linha (tremonha, máquina SOPRO, tapete, garrafas). Animação ligada quando a máquina está em produção. Pode ser substituída por uma imagem PNG/JPEG/WebP.",
            Array.Empty<Source>()),

        new Item("palletStack", "Paletes de pré-formas", "Stock", "PALLETS", true, false,
            "Ícones de palete: uma por posição disponível (máx. 12) da pré-forma do lote no Armazém 3.",
            new[] { new Source("preformStock.availablePositions", "T_LagerPositionen", "COUNT(*)", Q2, "AvailablePositions", new[] { "IdWarenLager = armazém 3", "localizações acordadas", "GesperrtAktiv = 0", "fora da retenção" }) },
            "Soma das posições disponíveis de todas as localizações acordadas; sem stock → palete vazia tracejada."),

        new Item("preformLabel", "Pré-forma do lote", "Stock", "TEXT", true, false,
            "Artigo da pré-forma planeada para o lote (material principal).",
            new[] { new Source("current.preformArticle", "T_Artikelvarianten", "Bezeichnung", Q1, "PlannedPreform", new[] { "IdArtikelvarianten = T_ProdPlanung.IdHauptmaterial" }) }),

        new Item("stockAvailable", "Unidades disponíveis", "Stock", "NUMBER", true, false,
            "Quantidade disponível da pré-forma do lote no Armazém 3 (localizações acordadas, não bloqueada, fora da retenção).",
            new[] { new Source("preformStock.availableQty", "T_LagerPositionen", "EinlagerungsMenge", Q2, "AvailableQty", new[] { "IdWarenLager = armazém 3", "localizações acordadas", "GesperrtAktiv = 0", "IdLagerAbteilung ≠ retenção" }) },
            "Soma por localização e depois por pré-forma.", new[] { "A retenção (30012) e as posições bloqueadas nunca entram neste valor." },
            new[] { "WarehouseId", "LocationIds", "RetentionLocationId", "PreformArticleTypeId" }),

        new Item("stockPositions", "Paletes/posições disponíveis", "Stock", "NUMBER", true, false,
            "Número de posições (paletes) disponíveis da pré-forma do lote.",
            new[] { new Source("preformStock.availablePositions", "T_LagerPositionen", "COUNT(*)", Q2, "AvailablePositions", new[] { "mesmos filtros das unidades disponíveis" }) },
            null, new[] { "A fonte conta posições armazenadas; a correspondência com «caixas» não está confirmada." }),

        new Item("stockBlocked", "Retenção / bloqueado", "Stock", "NUMBER", true, false,
            "Unidades e posições na retenção (30012) ou bloqueadas — mostradas à parte do disponível.",
            new[] { new Source("preformStock.blockedQty", "T_LagerPositionen", "EinlagerungsMenge", Q2, "BlockedQty", new[] { "IdLagerAbteilung = retenção OU GesperrtAktiv = 1" }),
                    new Source("preformStock.blockedPositions", "T_LagerPositionen", "COUNT(*)", Q2, "BlockedPositions") },
            null, null, new[] { "RetentionLocationId" }),

        new Item("planned", "Planeado (garrafas)", "Produção", "BAR", true, false,
            "Quantidade planeada do lote atual.",
            new[] { new Source("current.plannedBottles", "T_ProdPlanung", "PlanMenge", Q1, "PlannedBottles") }),

        new Item("reported", "Reportado (garrafas)", "Produção", "BAR", true, false,
            "Garrafas reportadas no AlplaREPORT para o lote atual.",
            new[] { new Source("current.reportedBottles", "T_ProduktionsRueckmeldungDaten", "ProdMenge", Q1, "ReportedBottles", new[] { "T_ProduktionsRueckmeldungen.IdProdPlanung = lote" }),
                    new Source("current.reportLastUpdated", "T_ProduktionsRueckmeldungDaten", "Upd_Date", Q1, "ReportLastUpdated") },
            "SUM(ProdMenge) de todos os registos de reporte do lote."),

        new Item("scanned", "Lidas no scanner (pré-formas)", "Produção", "BAR", true, false,
            "Pré-formas lidas pelo scanner da linha para o lote (lançamentos de produção), nunca consumo físico.",
            new[] { new Source("current.scannedPreforms", "T_ProduktionsBuchungen", "Menge", Q1, "ScannedPreforms", new[] { "ProduktionsLos = lote", "IdBuchungsGrund = ScanReasonId", "IdLagerAbteilung = ScanLocationId", "Urheber ∈ {consumo, anulação}", "artigo = pré-forma do lote" }),
                    new Source("current.scanCount", "T_ProduktionsBuchungen", "Urheber", Q1, "ScanCount", new[] { "contagem de lançamentos de consumo" }),
                    new Source("current.lastScan", "T_ProduktionsBuchungen", "BuchungsDatum", Q1, "LastScan") },
            "SUM(Menge) com sinal por Bewegung (1 = +, outro = −).",
            new[] { "Sem lançamentos → «Sem leitura de scanner»; nunca se mostra 0 nem um desvio de −100 %." },
            new[] { "ScanReasonId", "ScanLocationId", "ScanConsumeOperation", "ScanUnconsumeOperation" }),

        new Item("deviation", "Desvio (provisório)", "Produção", "CHIP", true, false,
            "Diferença relativa entre pré-formas lidas e garrafas reportadas.",
            new[] { new Source("current.deviation.ratio", "", "", Service, "ComputeDeviation") },
            "(lidas − reportadas) ÷ reportadas, aritmética decimal; só com leituras > 0 e reporte > 0.",
            new[] { "PROVISÓRIO: a regra 1 pré-forma = 1 garrafa não está confirmada.", "O Waste não entra no cálculo." }),

        new Item("wasteBottles", "Garrafas rejeitadas (art. 56)", "Waste", "WASTE", true, false,
            "Waste AlplaLABEL do artigo 56 (PET Produto Acabado) lançado para o lote, em kg.",
            new[] { new Source("current.waste.rejectedBottlesKg", "T_LagerBuchungen", "Menge", Q1, "RejectedBottles_kg", new[] { "ProduktionsLos = lote", "IdBuchungsGrund = WasteReasonId", "Urheber = WasteOriginatorId", "artigo = WasteBottlesArticleId" }),
                    new Source("current.waste.rejectedBottlesUnitsEstimate", "", "", Service, "EstimateUnits") },
            "kg registados; unidades ≈ kg × 1000 ÷ peso da garrafa (g) — estimativa.",
            new[] { "Os kg são o valor registado; as unidades são sempre uma estimativa.", "Nunca é somado ao Waste de pré-formas." },
            new[] { "WasteReasonId", "WasteOriginatorId", "WasteBottlesArticleId" }),

        new Item("wastePreforms", "Pré-formas rejeitadas (art. 55)", "Waste", "WASTE", true, false,
            "Waste AlplaLABEL do artigo 55 (PET Produto Semi Acabado) lançado para o lote, em kg.",
            new[] { new Source("current.waste.rejectedPreformsKg", "T_LagerBuchungen", "Menge", Q1, "RejectedPreforms_kg", new[] { "ProduktionsLos = lote", "IdBuchungsGrund = WasteReasonId", "Urheber = WasteOriginatorId", "artigo = WastePreformsArticleId" }),
                    new Source("current.waste.rejectedPreformsUnitsEstimate", "", "", Service, "EstimateUnits") },
            "kg registados; unidades ≈ kg × 1000 ÷ peso da pré-forma planeada (g) — estimativa.",
            new[] { "Os kg são o valor registado; as unidades são sempre uma estimativa.", "Nunca é somado ao Waste de garrafas." },
            new[] { "WasteReasonId", "WasteOriginatorId", "WastePreformsArticleId" }),

        new Item("previousOrders", "Ordens anteriores", "Histórico", "LIST", true, false,
            "As ordens de produção anteriores da máquina (reportado, lidas, Waste, desvio), layout interno fixo.",
            new[] { new Source("previous", "T_ProdPlanung", "IdProdPlanung", Q1, "Lot", new[] { "SlotNo > 0 (linhas das ordens anteriores)", "lotes de produção anteriores ao atual, por PlanVon decrescente" }) },
            null, null, new[] { "PreviousLots" }),
    };

    public static Item? Find(string key) => Items.FirstOrDefault(i => i.Key == key);

    public static bool IsKnownKey(string key) => Find(key) is not null || IsFreeKey(key);

    public static bool IsRotatable(string key) => IsFreeKey(key) || (Find(key)?.Rotatable ?? false);
}

/// <summary>Verifies the catalogue against the real query text and the DTO contracts; cached per process.</summary>
public class Viana3CardCatalogVerifier
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<Viana3CardCatalogVerifier> _logger;
    private readonly object _gate = new();
    private Viana3CardItemCatalogDto? _cached;
    private DateTime _cachedAtUtc;

    public Viana3CardCatalogVerifier(IConfiguration configuration, ILogger<Viana3CardCatalogVerifier> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Viana3CardItemCatalogDto GetVerified(bool force = false)
    {
        lock (_gate)
        {
            if (!force && _cached is not null && (DateTime.UtcNow - _cachedAtUtc) < TimeSpan.FromMinutes(10)) return _cached;
            _cached = Build();
            _cachedAtUtc = DateTime.UtcNow;
            return _cached;
        }
    }

    private Viana3CardItemCatalogDto Build()
    {
        var options = (_configuration.GetSection(Viana3ProductionMonitorOptions.SectionName).Get<Viana3ProductionMonitorOptions>()
                       ?? new Viana3ProductionMonitorOptions()).Normalize();

        // Render the real query text (nothing is executed; the command never gets a connection).
        var sql = new Dictionary<string, string>();
        try
        {
            sql[Viana3CardItemCatalog.Q1] = Viana3ProductionMonitorQueryBuilder.BuildLotsQuery(new SqlCommand(), options, options.MachineIds);
            sql[Viana3CardItemCatalog.Q2] = Viana3ProductionMonitorQueryBuilder.BuildStockQuery(new SqlCommand(), options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Viana3 card catalogue: the query builder could not render the queries; every SQL-backed source is NOT_VERIFIED");
        }

        var serviceType = typeof(Viana3ProductionMonitorService);
        var dto = new Viana3CardItemCatalogDto { VerifiedAtUtc = DateTime.UtcNow, SourceDatabase = "AlplaPROD_aovia3 (só leitura, via o backend do Portal)" };

        foreach (var item in Viana3CardItemCatalog.Items)
        {
            var itemDto = new Viana3CardItemDto
            {
                Key = item.Key, Label = item.Label, Group = item.Group, Kind = item.Kind, DataDriven = item.DataDriven,
                Rotatable = item.Rotatable, Description = item.Description, Calculation = item.Calculation,
                Caveats = (item.Caveats ?? Array.Empty<string>()).ToList(),
                ConfiguredValues = (item.ConfiguredOptions ?? Array.Empty<string>()).ToDictionary(n => n, n => OptionValue(options, n)),
            };

            foreach (var src in item.Sources)
            {
                var s = new Viana3CardItemSourceDto
                {
                    PayloadField = src.PayloadField, Table = src.Table, Field = src.Field, Query = src.Query, Alias = src.Alias,
                    Filters = (src.Filters ?? Array.Empty<string>()).ToList(),
                };
                var problems = new List<string>();

                if (!PayloadFieldExists(src.PayloadField)) problems.Add($"campo «{src.PayloadField}» não existe no contrato do painel");

                if (src.Query == Viana3CardItemCatalog.Service)
                {
                    var method = serviceType.GetMethod(src.Alias, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (method is null) problems.Add($"método «{src.Alias}» não existe no serviço");
                }
                else if (sql.TryGetValue(src.Query, out var text))
                {
                    var match = Regex.Match(text, @"\bAS\s+" + Regex.Escape(src.Alias) + @"\b", RegexOptions.IgnoreCase);
                    var plainColumn = !match.Success && Regex.IsMatch(text, @"\b" + Regex.Escape(src.Alias) + @"\b");
                    if (!match.Success && !plainColumn) problems.Add($"alias «{src.Alias}» não está definido na consulta {src.Query}");
                    if (!string.IsNullOrEmpty(src.Table) && !text.Contains($"[{src.Table}]", StringComparison.OrdinalIgnoreCase))
                        problems.Add($"tabela «{src.Table}» não aparece na consulta {src.Query}");
                    if (!string.IsNullOrEmpty(src.Field) && src.Field != "COUNT(*)" && !Regex.IsMatch(text, @"\." + Regex.Escape(src.Field) + @"\b", RegexOptions.IgnoreCase))
                        problems.Add($"campo «{src.Field}» não aparece na consulta {src.Query}");
                    if (match.Success) s.Expression = ExtractExpression(text, match.Index);
                }
                else problems.Add("consulta não disponível");

                s.Verification = problems.Count == 0 ? "VERIFIED" : "NOT_VERIFIED";
                s.VerificationDetail = problems.Count == 0 ? null : string.Join("; ", problems);
                if (problems.Count > 0)
                {
                    s.Expression = null;
                    _logger.LogError("Viana3 card catalogue: item {Key} source {Alias} NOT_VERIFIED: {Detail}", item.Key, src.Alias, s.VerificationDetail);
                }
                itemDto.Sources.Add(s);
            }
            dto.Items.Add(itemDto);
        }
        return dto;
    }

    /// <summary>Expression that produces the alias: the top-level segment preceding "AS alias" on the rendered text.</summary>
    private static string ExtractExpression(string text, int asIndex)
    {
        var depth = 0;
        var start = asIndex;
        for (var i = asIndex - 1; i >= 0; i--)
        {
            var c = text[i];
            if (c == ')') depth++;
            else if (c == '(')
            {
                if (depth == 0) { start = i + 1; break; }
                depth--;
            }
            else if (depth == 0 && (c == ',' || c == '\n'))
            {
                // a comma at depth 0 ends the previous column; a line break may precede "SELECT col ..."
                var before = text[..i].TrimEnd();
                if (c == ',' || before.EndsWith("SELECT", StringComparison.OrdinalIgnoreCase)) { start = i + 1; break; }
            }
            start = i;
        }
        var expr = text[start..asIndex].Trim();
        if (expr.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) expr = expr[6..].Trim();
        return expr.Length > 220 ? expr[..217] + "…" : expr;
    }

    private static string OptionValue(Viana3ProductionMonitorOptions o, string name)
    {
        var p = typeof(Viana3ProductionMonitorOptions).GetProperty(name);
        var v = p?.GetValue(o);
        return v switch
        {
            null => "—",
            System.Collections.IEnumerable e when v is not string => string.Join(", ", e.Cast<object>()),
            _ => v.ToString() ?? "—",
        };
    }

    /// <summary>Resolves "a.b.c" over the panel DTO using the JSON property names.</summary>
    private static bool PayloadFieldExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        Type type = typeof(Viana3MachinePanelDto);
        foreach (var segment in path.Split('.'))
        {
            var prop = type.GetProperties().FirstOrDefault(p =>
                string.Equals(p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? p.Name, segment, StringComparison.Ordinal));
            if (prop is null) return false;
            var t = prop.PropertyType;
            t = Nullable.GetUnderlyingType(t) ?? t;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) t = t.GetGenericArguments()[0];
            type = t;
        }
        return true;
    }
}
