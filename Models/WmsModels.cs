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

// ─── Liste di prelievo ────────────────────────────────────────────────────────

/// <summary>Testata lista — usata nella schermata di selezione.</summary>
public record PickListHeaderDto(
    Guid         Id,
    string       Code,
    string       Description,
    string       Status,
    DateTime     CreatedAt,
    string       CreatedByOp,
    string?      AssignedOperator,  // operatore assegnato dall'ufficio
    int          TotalRows,
    int          CompletedRows,
    int          MissingRows,
    List<string> OlCods            // bolle di produzione collegate
);

/// <summary>Riga articolo da prelevare, arricchita con giacenze ERP e prelievi staged.</summary>
public class PickListRowDto
{
    public Guid    Id               { get; set; }
    public Guid    ListId           { get; set; }
    public string  ArticleCode      { get; set; } = "";
    public string  ArticleDesc      { get; set; } = "";
    public string  UoM              { get; set; } = "";
    public decimal PlannedQty       { get; set; }
    public string? OlCod            { get; set; }       // bolla di produzione (null = extra)
    public string  Handling         { get; set; } = "";
    public int?    IdSpec           { get; set; }
    public int?    IdGroup          { get; set; }
    public string  Paf02             { get; set; } = "";  // A_PAR.PAF02 — ubicazione old-style
    public string  MainWarehouseCode { get; set; } = "";
    public string  MainLocationCode  { get; set; } = "";  // L_MLPA LCPRC='Y' — locazione preferenziale
    public bool    IsMissing         { get; set; }
    public bool    IsExtraItem       { get; set; }
    public int     SortOrder         { get; set; }

    // Giacenze ERP — popolate da PickListService.GetPickListDetailAsync
    public List<ArticleLocationDto> Locations { get; set; } = [];

    // Prelievi (staged = non ancora eseguiti; executed = già scritti su ERP)
    public List<StagedPickDto> Picks { get; set; } = [];

    // Calcolati
    public decimal StagedQty       => Picks.Where(p => !p.IsExecuted).Sum(p => p.PickedQty);
    public decimal ExecutedQty     => Picks.Where(p => p.IsExecuted).Sum(p => p.PickedQty);
    public decimal TotalCoveredQty => StagedQty + ExecutedQty;
    public decimal RemainingQty    => Math.Max(0, PlannedQty - TotalCoveredQty);
    public bool    HasStock        => Locations.Any(l => l.Quantity > 0);

    public StagingStatus StagingStatus =>
        IsMissing ? StagingStatus.Missing :
        TotalCoveredQty >= PlannedQty && PlannedQty > 0 ? StagingStatus.Done :
        TotalCoveredQty > 0 ? StagingStatus.Partial :
        StagingStatus.Pending;
}

/// <summary>Singolo prelievo — staged (ExecutedAt null) o già eseguito su ERP.</summary>
public class StagedPickDto
{
    public Guid      Id            { get; set; }
    public Guid      RowId         { get; set; }
    public decimal   PickedQty     { get; set; }
    public string    WarehouseCode { get; set; } = "";
    public string    LocationCode  { get; set; } = "";
    public string    OperatorCode  { get; set; } = "";
    public DateTime  StagedAt      { get; set; }
    public DateTime? ExecutedAt    { get; set; }
    public int?      ErpMovId      { get; set; }
    public int?      ErpSesId      { get; set; }  // S_SES.IDSES in cui è stato eseguito
    public bool      IsExecuted    => ExecutedAt.HasValue;
}

public enum PickSortField { ArticleCode, Handling, IdSpec, IdGroup, MainLocation, Paf02 }
public enum StagingStatus  { Pending, Partial, Done, Missing }

// ─── Accettazione merce ───────────────────────────────────────────────────────

public record AcceptanceDocDto(
    int ErpDocId,          // A_DOT primary key (0 = mock)
    string DocType,        // DTDO: RLA / RFL / DCF
    string DocumentRef,    // numero documento
    string SupplierName,   // A_FOR.FORAG
    DateTime DocumentDate,
    List<AcceptanceItemDto> Items
);

public class AcceptanceItemDto
{
    public Guid   Id          { get; init; } = Guid.NewGuid();
    public int    ErpLineId   { get; set; }   // A_DOR primary key
    public int    ErpDocId    { get; set; }   // FK to parent A_DOT
    public string ArticleCode { get; set; } = "";
    public string ArticleDesc { get; set; } = "";
    public string UoM         { get; set; } = "";
    public decimal ExpectedQty  { get; set; }
    public decimal AcceptedQty  { get; set; }
    public decimal RemainingQty => Math.Max(0, ExpectedQty - AcceptedQty);
    public string? DestLocation { get; set; }
    public AcceptanceStatus Status { get; set; } = AcceptanceStatus.Pending;
    public List<AcceptanceVersamentoDto> Versamenti { get; set; } = [];
}

/// <summary>Singolo versamento registrato per una riga DDT (da WMS_AcceptanceLine).</summary>
public record AcceptanceVersamentoDto(
    Guid     Id,
    decimal  Qty,
    string   WarehouseCode,
    string   LocationCode,
    string   OperatorCode,
    DateTime AcceptedAt,
    int?     ErpMovId
);

/// <summary>Riga versamento persistita su WMS_AcceptanceLine (Logic DB).</summary>
public class WmsAcceptanceLine
{
    public Guid    Id            { get; set; } = Guid.NewGuid();
    public int     ErpDocId      { get; set; }
    public int     ErpLineId     { get; set; }
    public string  ArticleCode   { get; set; } = "";
    public string  WarehouseCode { get; set; } = "";
    public string  LocationCode  { get; set; } = "";
    public decimal AcceptedQty   { get; set; }
    public decimal ExpectedQty   { get; set; }
    public string  OperatorCode  { get; set; } = "";
    public int?    ErpMovId      { get; set; }
    public DateTime AcceptedAt   { get; set; } = DateTime.Now;
    public string  DocumentRef   { get; set; } = "";
    public string? Notes         { get; set; }
}

/// <summary>Input per AcceptanceService.AcceptLineAsync.</summary>
public record AcceptLineRequest(
    int     ErpDocId,
    int     ErpLineId,
    string  ArticleCode,
    string  ArticleDesc,
    string  UoM,
    decimal Qty,
    string  WarehouseCode,
    string  LocationCode,
    string  OperatorCode,
    string  DocumentRef,
    decimal ExpectedQty
);

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

// ─── Gestione locazioni ───────────────────────────────────────────────────────

public class ManagedLocationDto
{
    public string  WarehouseCode { get; set; } = "";
    public string  LocationCode  { get; set; } = "";
    public decimal CurrentQty    { get; set; }
    public bool    IsMain        { get; set; }
}

// ─── Pick list service ────────────────────────────────────────────────────────

public record AddExtraRowResult(
    string  ArticleCode,
    string  ArticleDesc,
    string  UoM,
    decimal PlannedQty,
    string? OlCod
);

public record ErpMovRequest(
    string  ArticleCode,
    string  CausalCode,
    decimal Qty,
    string  WarehouseCode,
    string  LocationCode,
    string  OperatorCode,
    string? ReferenceCode = null,
    int     NodeId        = 0
);

// ─── Magazzini ────────────────────────────────────────────────────────────────

public record WarehouseDto(string Code, string Description, string Type);
public record LocationSummaryDto(string Code, string Description, string WarehouseCode);

// ─── Enums ───────────────────────────────────────────────────────────────────

public enum PickStatus { Pending, Partial, Completed }
public enum StockLevel { Ok, Low, Zero, Negative }
public enum AcceptanceStatus { Pending, Partial, Done }
