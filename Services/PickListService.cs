using Dapper;
using WMS.Models;

namespace WMS.Services;

/// <summary>
/// Orchestrates pick list operations across Logic DB (state) and ERP (stock + movements).
/// Singleton — all methods are stateless; circuit-level state stays in the Razor component.
/// </summary>
public class PickListService
{
    private readonly LogicService _logic;
    private readonly ErpService   _erp;
    private readonly ILogger<PickListService> _log;

    public PickListService(LogicService logic, ErpService erp, ILogger<PickListService> log)
    {
        _logic = logic;
        _erp   = erp;
        _log   = log;
    }

    // ─── Liste ───────────────────────────────────────────────────────────────

    public Task<List<PickListHeaderDto>> GetPickListsAsync()
        => _logic.GetPickListsAsync();

    /// <summary>
    /// Crea una nuova lista caricando le righe dalla vista WMS_V_PickList per la bolla indicata.
    /// Ritorna l'ID della lista creata.
    /// </summary>
    public async Task<Guid> LoadListFromOlCodAsync(string olCod, string opCode)
    {
        var rows = await _erp.GetPickListRowsForOlCodAsync(olCod);
        if (rows.Count == 0)
            throw new InvalidOperationException($"Bolla {olCod} non trovata o senza righe da prelevare.");

        var listId = await _logic.CreatePickListAsync(
            code:        olCod,
            description: $"Lista {olCod}",
            opCode:      opCode);

        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].Id       = Guid.NewGuid();
            rows[i].ListId   = listId;
            rows[i].SortOrder = i;
            await _logic.AddPickListRowAsync(listId, rows[i]);
        }
        return listId;
    }

    // ─── Dettaglio lista ──────────────────────────────────────────────────────

    /// <summary>
    /// Carica le righe della lista dal Logic DB e le arricchisce con le giacenze ERP in batch.
    /// </summary>
    public async Task<List<PickListRowDto>> GetPickListDetailAsync(Guid listId)
    {
        var rows = await _logic.GetPickListRowsAsync(listId);
        if (rows.Count == 0) return rows;

        // Giacenze batch: una sola query per tutti gli articoli della lista
        var codes   = rows.Select(r => r.ArticleCode).Distinct().ToList();
        var locMap  = await _erp.GetStockBatchAsync(codes);

        foreach (var row in rows)
        {
            if (locMap.TryGetValue(row.ArticleCode, out var locs))
                row.Locations = locs;

            // Aggiorna snapshot locazione principale se la riga non ce l'ha
            if (string.IsNullOrEmpty(row.MainLocationCode))
            {
                var main = locs?.FirstOrDefault(l => l.IsMainWithdrawal);
                if (main is not null)
                {
                    row.MainWarehouseCode = main.WarehouseCode;
                    row.MainLocationCode  = main.LocationCode;
                }
            }
        }
        return rows;
    }

    // ─── Staging picks ────────────────────────────────────────────────────────

    /// <summary>Aggiunge un prelievo staged (scritto subito su DB per resilienza WiFi).</summary>
    public async Task StagePickAsync(Guid rowId, decimal qty,
                                     string warehouseCode, string locationCode, string opCode)
    {
        var pick = new StagedPickDto
        {
            Id            = Guid.NewGuid(),
            RowId         = rowId,
            PickedQty     = qty,
            WarehouseCode = warehouseCode,
            LocationCode  = locationCode,
            OperatorCode  = opCode,
            StagedAt      = DateTime.Now
        };
        await _logic.StagePickAsync(pick);
    }

    public Task RemoveStagedPickAsync(Guid pickId)
        => _logic.RemoveStagedPickAsync(pickId);

    public Task CancelStagedPicksAsync(Guid listId)
        => _logic.CancelStagedPicksForListAsync(listId);

    public Task ClosePickListAsync(Guid listId)
        => _logic.ClosePickListAsync(listId);

    public Task DeclareMissingAsync(Guid rowId)
        => _logic.SetRowMissingAsync(rowId, true);

    public Task UndoMissingAsync(Guid rowId)
        => _logic.SetRowMissingAsync(rowId, false);

    /// <summary>
    /// "Preleva tutto": aggiunge un pick staged dalla locazione principale
    /// per ogni riga non mancante e con residuo > 0.
    /// </summary>
    public async Task StageAllFromMainAsync(Guid listId, string opCode)
    {
        var rows = await _logic.GetPickListRowsAsync(listId);
        foreach (var row in rows)
        {
            if (row.IsMissing || row.RemainingQty <= 0) continue;
            if (string.IsNullOrEmpty(row.MainLocationCode)) continue;
            await StagePickAsync(row.Id, row.RemainingQty,
                                 row.MainWarehouseCode, row.MainLocationCode, opCode);
        }
    }

    /// <summary>Aggiunge una riga extra (fuori lista) con bolla opzionale.</summary>
    public async Task AddExtraRowAsync(Guid listId, string articleCode, decimal plannedQty,
                                       string? olCod, string opCode)
    {
        // Recupera descrizione e UdM dall'ERP
        var art = await _erp.GetArticleAsync(articleCode);

        var row = new PickListRowDto
        {
            Id          = Guid.NewGuid(),
            ListId      = listId,
            ArticleCode = articleCode,
            ArticleDesc = art?.Description ?? articleCode,
            UoM         = art?.UoM ?? "",
            PlannedQty  = plannedQty,
            OlCod       = olCod,
            IsExtraItem = true
        };
        await _logic.AddPickListRowAsync(listId, row);
    }

    // ─── Esecuzione batch ─────────────────────────────────────────────────────

    /// <summary>
    /// Esegue tutti i pick staged per la lista: per riga con OlCod usa TRD_InsertMov SCAR
    /// con referenceCode=OlCod; per righe senza OlCod usa TRD_InsertMov SCAR puro.
    /// Aggiorna ExecutedAt + ErpMovId su ogni pick completato.
    /// Ritorna (successi, errori) per il feedback UI.
    /// </summary>
    public async Task<(int Ok, List<string> Errors)> ExecuteStagedPicksAsync(
        Guid listId, string opCode, int nodeId)
    {
        var pending = await _logic.GetPendingPicksForListAsync(listId);
        if (pending.Count == 0) return (0, []);

        int ok = 0;
        var errors = new List<string>();

        foreach (var (pick, articleCode, articleDesc, uom, olCod) in pending)
        {
            try
            {
                var movId = await _erp.InsertMovAsync(new ErpMovRequest(
                    ArticleCode:   articleCode,
                    CausalCode:    "SCAR",
                    Qty:           pick.PickedQty,
                    WarehouseCode: pick.WarehouseCode,
                    LocationCode:  pick.LocationCode,
                    OperatorCode:  opCode,
                    ReferenceCode: olCod,
                    NodeId:        nodeId
                ));

                if (movId <= 0)
                {
                    errors.Add($"{articleCode}: TRD_InsertMov ha restituito {movId}");
                    continue;
                }

                await _logic.MarkPickExecutedAsync(pick.Id, movId);
                ok++;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "ExecuteStagedPicksAsync: errore su {Art}", articleCode);
                errors.Add($"{articleCode}: {ex.Message}");
            }
        }

        await _logic.UpdatePickListStatusAsync(listId);
        return (ok, errors);
    }
}
