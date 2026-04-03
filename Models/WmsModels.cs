namespace WMS.Models;

// ─── Articoli ────────────────────────────────────────────────────────────────

public record ArticleDto(
    string Code,
    string Description,
    string UoM,
    decimal TotalStock,
    decimal MinStock,
    List<ArticleLocationDto> Locations,
    List<MovementDto> RecentMovements
);

public record ArticleLocationDto(
    string WarehouseCode,
    string WarehouseDesc,
    string LocationCode,
    string LocationDesc,
    decimal Quantity,
    decimal MinQty,
    decimal MaxQty,
    string Priority,       // "P" = preferenziale, "S" = secondaria
    bool IsMainWithdrawal
);

// ─── Locazioni ────────────────────────────────────────────────────────────────

public record LocationDto(
    string WarehouseCode,
    string WarehouseDesc,
    string LocationCode,
    string LocationDesc,
    string LocationType,
    int? Lane,
    int? HPos,
    int? VPos,
    bool IsAcceptance,
    bool IsFiscal,
    List<LocationContentDto> Contents,
    List<MovementDto> RecentMovements
);

public record LocationContentDto(
    string ArticleCode,
    string ArticleDesc,
    string UoM,
    decimal Quantity,
    string Priority
);

// ─── Movimenti ────────────────────────────────────────────────────────────────

public record MovementDto(
    DateTime Timestamp,
    string CausalCode,
    string CausalDesc,
    decimal Quantity,
    string OperatorCode,
    string OperatorName,
    string DocumentRef,
    string WarehouseCode,
    string LocationCode
);

// ─── Carrello (transito provvisorio) ─────────────────────────────────────────
// Fase 1 — accumulo: art + loc origine + qty (senza destinazione)
// Fase 2 — evasione: si assegna la destinazione per una o più righe

public class CartRow
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string ArticleCode { get; set; } = "";
    public string ArticleDesc { get; set; } = "";
    public string UoM { get; set; } = "";
    public string SourceWarehouse { get; set; } = "";
    public string SourceLocation { get; set; } = "";
    public decimal Quantity { get; set; }
    // Destinazione — null finché non si evade
    public string? DestWarehouse { get; set; }
    public string? DestLocation { get; set; }
    public bool IsEvaded => DestLocation is not null;
    // UI: selezione multipla durante evasione
    public bool IsSelected { get; set; }
}

// ─── Ordini di produzione ────────────────────────────────────────────────────

public record ProductionOrderDto(
    string Code,
    string Description,
    DateTime DueDate,
    string Status,
    int TotalItems,
    int PickedItems,
    List<ProductionItemDto> Items
);

public record ProductionItemDto(
    Guid Id,
    string ArticleCode,
    string ArticleDesc,
    string UoM,
    decimal PlannedQty,
    decimal PickedQty,
    string SuggestedWarehouse,
    string SuggestedLocation,
    decimal AvailableAtLocation,
    PickStatus Status
);

// ─── Prelievo produzione da XV_SITUAZIONE_PRELIEVI ───────────────────────────

/// <summary>
/// Testata della lista prelievo per bolla di lavoro (OLCOD).
/// </summary>
public record ProductionPickListDto(
    string OlCod,
    string CoCod,
    string ComDesc,
    int Conum,
    int LoCode,
    string Stato,
    bool Terminata,
    bool LottoTerminato,
    string? BollaVersamento,
    List<ProductionPickRowDto> Rows
)
{
    public int TotaleRighe    => Rows.Count;
    public int RigheEvase     => Rows.Count(r => r.QtaResidua <= 0);
    public bool TutteEvase    => TotaleRighe > 0 && RigheEvase == TotaleRighe;
    /// <summary>Righe raggruppate per Handling, ordinate per descrizione articolo.</summary>
    public IEnumerable<IGrouping<string, ProductionPickRowDto>> PerHandling =>
        Rows.OrderBy(r => r.Handling).ThenBy(r => r.PaDsc)
            .GroupBy(r => r.Handling);
}

/// <summary>
/// Riga articolo da prelevare: un articolo con il suo tipo handling e ubicazione.
/// </summary>
public record ProductionPickRowDto(
    string  PaCod,
    string  PaDsc,
    string  PaUdm,
    string  Handling,
    string  Ubicazione,
    decimal QtaDaPrelevare,
    decimal QtaPrelevata,
    decimal Giacenza,
    int     Conum,
    int     LoCode,
    int?    IdPap        // null = S_PAP non ancora creata per questa coppia OLCOD+PACOD
)
{
    public decimal QtaResidua => QtaDaPrelevare - QtaPrelevata;
    public PickStatus Status  => QtaResidua <= 0  ? PickStatus.Completed
                               : QtaPrelevata > 0 ? PickStatus.Partial
                               :                    PickStatus.Pending;
};

// ─── Liste di prelievo v2 ─────────────────────────────────────────────────────
// Struttura riprogettata: multi-prelievo per articolo, loc principale + loc
// con più scorta, attributo riepilogo customizzabile.

public record PickListV2Dto(
    string Code,
    string Description,
    string Reference,               // es. bolla di consegna, OC cliente
    DateTime CreatedAt,
    string Status,
    string CustomAttributeLabel,    // etichetta colonna custom (es. "Tipo Handling")
    List<PickListV2ItemDto> Items
);

public class PickListV2ItemDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string ArticleCode { get; set; } = "";
    public string ArticleDesc { get; set; } = "";
    public string UoM { get; set; } = "";
    public decimal PlannedQty { get; set; }
    public decimal PickedQty { get; set; }
    public decimal RemainingQty => PlannedQty - PickedQty;

    // Locazione principale da anagrafica
    public string MainLocationCode { get; set; } = "";
    public string MainLocationDesc { get; set; } = "";

    // Locazione con la scorta maggiore (calcolata)
    public string BestStockLocationCode { get; set; } = "";
    public string BestStockLocationDesc { get; set; } = "";
    public decimal BestStockQty { get; set; }

    // Attributo riepilogo (valore specifico dell'articolo nella lista)
    public string CustomAttribute { get; set; } = "";

    public bool HasStock => BestStockQty > 0;
    public PickStatus Status { get; set; } = PickStatus.Pending;
}

// ─── Accettazione merce ───────────────────────────────────────────────────────

public record AcceptanceDocDto(
    string DocumentRef,
    string SupplierName,
    DateTime DocumentDate,
    List<AcceptanceItemDto> Items
);

public class AcceptanceItemDto
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string ArticleCode { get; set; } = "";
    public string ArticleDesc { get; set; } = "";
    public string UoM { get; set; } = "";
    public decimal ExpectedQty { get; set; }
    public decimal AcceptedQty { get; set; }
    public decimal RemainingQty => ExpectedQty - AcceptedQty;
    public string? DestLocation { get; set; }
    public AcceptanceStatus Status { get; set; } = AcceptanceStatus.Pending;
}

// ─── Inventario ───────────────────────────────────────────────────────────────

public class InventorySession
{
    public Guid     Id            { get; set; } = Guid.NewGuid();
    public string   OperatorCode  { get; set; } = "";
    public string   WarehouseCode { get; set; } = "";
    public string   WarehouseDesc { get; set; } = "";
    public string   Zone          { get; set; } = "";
    public DateTime StartedAt     { get; set; } = DateTime.Now;
    public List<InventoryCount> Counts { get; set; } = [];
}

public class InventoryCount
{
    public Guid     Id            { get; set; } = Guid.NewGuid();
    public string   WarehouseCode { get; set; } = "";
    public string   ArticleCode   { get; set; } = "";
    public string   ArticleDesc   { get; set; } = "";
    public string   UoM           { get; set; } = "";
    public string   LocationCode  { get; set; } = "";
    public string   LocationDesc  { get; set; } = "";
    public decimal  ExpectedQty   { get; set; }
    public decimal? CountedQty    { get; set; }
    public bool     Applied       { get; set; }
    public decimal Delta      => CountedQty.HasValue ? CountedQty.Value - ExpectedQty : 0;
    public bool    IsCounted  => CountedQty.HasValue;
}

// ─── Stampa ───────────────────────────────────────────────────────────────────

public class PrintTemplate
{
    public int    Id          { get; set; }
    public string Context     { get; set; } = "";   // "ARTICLE","LOCATION","MOVE","CART","INVENTORY","ACCEPTANCE"
    public string Name        { get; set; } = "";
    public string ReportName  { get; set; } = "";   // printModelCode oppure path .rpt
    public string? PrinterName { get; set; }        // override stampante (null = default config)
    public bool   IsActive    { get; set; } = true;
    public List<PrintTemplateParam> Params { get; set; } = [];
}

public class PrintTemplateParam
{
    public int    Id          { get; set; }
    public int    TemplateId  { get; set; }
    public string ParamName   { get; set; } = "";   // nome parametro Crystal / JSON key
    public string? AutoFillKey { get; set; }        // chiave auto-fill dal contesto (es. "PACOD","LCCOD")
    public string Label       { get; set; } = "";   // etichetta mostrata in UI
    public bool   IsRequired  { get; set; }
    public int    SortOrder   { get; set; }
}

// ─── Magazzini ────────────────────────────────────────────────────────────────

public record WarehouseDto(string Code, string Description, string Type);
public record LocationSummaryDto(string Code, string Description, string WarehouseCode);

// ─── Enums ───────────────────────────────────────────────────────────────────

public enum PickStatus { Pending, Partial, Completed }
public enum StockLevel { Ok, Low, Zero, Negative }
public enum AcceptanceStatus { Pending, Partial, Done }
