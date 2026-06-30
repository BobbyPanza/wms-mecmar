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

    public Task<List<PickListHeaderDto>> GetPickListsAsync(string opCode, bool onlyAssigned = false)
        => _logic.GetPickListsAsync(opCode, onlyAssigned);

    /// <summary>Preview articoli di una bolla senza creare la lista.</summary>
    public Task<List<PickListRowDto>> GetPreviewRowsAsync(string olCod)
        => _erp.GetPickListRowsForOlCodAsync(olCod);

    /// <summary>Crea lista da righe già selezionate (anziché dall'intera bolla).</summary>
    public async Task<Guid> CreateListFromRowsAsync(
        string code, string description, string opCode, List<PickListRowDto> rows)
    {
        if (rows.Count == 0)
            throw new InvalidOperationException("Nessuna riga selezionata.");
        var listId = await _logic.CreatePickListAsync(code, description, opCode);
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].Id        = Guid.NewGuid();
            rows[i].ListId    = listId;
            rows[i].SortOrder = i;
            await _logic.AddPickListRowAsync(listId, rows[i]);
        }
        return listId;
    }

    /// <summary>Aggiunge le righe di una bolla a una lista già esistente.</summary>
    public async Task AddBollaToListAsync(Guid listId, string olCod)
    {
        var rows = await _erp.GetPickListRowsForOlCodAsync(olCod);
        if (rows.Count == 0)
            throw new InvalidOperationException($"Bolla {olCod} non trovata o senza righe da prelevare.");

        var existing  = await _logic.GetPickListRowsAsync(listId);
        var sortBase  = existing.Count > 0 ? existing.Max(r => r.SortOrder) + 1 : 0;
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].Id        = Guid.NewGuid();
            rows[i].ListId    = listId;
            rows[i].SortOrder = sortBase + i;
            await _logic.AddPickListRowAsync(listId, rows[i]);
        }
        await _logic.UpdatePickListStatusAsync(listId);
    }

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

            // Locazione preferenziale sempre da L_MLPA LCPRC='Y' — indipendente da PAF02
            var main = locs?.FirstOrDefault(l => l.IsMainWithdrawal);
            if (main is not null)
            {
                row.MainWarehouseCode = main.WarehouseCode;
                row.MainLocationCode  = main.LocationCode;
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

    /// <summary>
    /// Ritorna le info di versamento per tutte le bolle presenti nella lista.
    /// Solo le bolle con A_LAV.FAABV='Y' e LAUFC='Y' compaiono nel risultato.
    /// </summary>
    public Task<List<PickListVersamentoInfoDto>> GetVersamentoInfoAsync(IEnumerable<string> olCods)
        => _erp.GetVersamentoInfoForOlCodsAsync(olCods);

    /// <summary>
    /// Esegue il versamento per tutte le bolle della lista che hanno una fase abilitata.
    /// Per ogni bolla: apre S_SES, chiama WMS_InsertVersamentoLine (S_PAV + S_SPV + TRD_InsertMov CAR),
    /// chiude S_SES. Se versamento completo la SP chiude anche A_LAV e S_ODL.
    /// </summary>
    public async Task<(int Ok, List<string> Errors)> ExecuteVersamentoAsync(
        IEnumerable<PickListVersamentoInfoDto> items, string opCode, int nodeId)
    {
        int ok = 0;
        var errors = new List<string>();

        foreach (var item in items)
        {
            var (idSes, sesErr) = await _erp.OpenPickSessionAsync(item.BollaVersamento, opCode, nodeId);
            if (idSes <= 0)
            {
                errors.Add($"{item.ArticleCode} ({item.BollaVersamento}): apertura sessione fallita — {sesErr}");
                continue;
            }
            try
            {
                var (idMov, movErr) = await _erp.InsertVersamentoLineAsync(
                    item.BollaVersamento, item.ArticleCode, item.ArticleDesc,
                    item.UoM, item.Qty, opCode, nodeId, idSes);

                if (idMov <= 0)
                    errors.Add($"{item.ArticleCode}: {movErr}");
                else
                    ok++;
            }
            finally
            {
                await _erp.ClosePickSessionAsync(idSes);
            }
        }
        return (ok, errors);
    }

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
        // GetPickListDetailAsync arricchisce le righe con MainLocationCode/MainWarehouseCode da ERP
        var rows = await GetPickListDetailAsync(listId);
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
    /// Esegue tutti i pick staged per la lista.
    /// Raggruppa per OlCod: per ogni bolla apre S_SES (WMS_OpenPickSession),
    /// registra ogni pick via WMS_InsertPickLine (→ S_PAP + S_SPP + TRD_InsertMov SCAR),
    /// poi chiude la sessione (WMS_ClosePickSession).
    /// Pick senza OlCod: fallback diretto a TRD_InsertMov SCAR.
    /// </summary>
    public async Task<(int Ok, List<string> Errors)> ExecuteStagedPicksAsync(
        Guid listId, string opCode, int nodeId)
    {
        var pending = await _logic.GetPendingPicksForListAsync(listId);
        if (pending.Count == 0) return (0, []);

        int ok = 0;
        var errors = new List<string>();

        // Raggruppa per OlCod (null → stringa vuota per i pick senza bolla)
        var groups = pending
            .GroupBy(p => p.OlCod ?? "")
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            var olCod = group.Key;
            int idSes = -1;

            try
            {
                // Apre la sessione ERP solo se c'è una bolla
                if (!string.IsNullOrEmpty(olCod))
                {
                    var (ses, sesErr) = await _erp.OpenPickSessionAsync(olCod, opCode, nodeId);
                    if (ses <= 0)
                    {
                        var msg = $"[{olCod}] Apertura sessione fallita: {sesErr}";
                        _log.LogError(msg);
                        foreach (var p in group)
                            errors.Add($"{p.ArticleCode}: {msg}");
                        continue;
                    }
                    idSes = ses;
                }

                foreach (var item in group)
                {
                    var pick        = item.Pick;
                    var articleCode = item.ArticleCode;
                    var articleDesc = item.ArticleDesc;
                    var uom         = item.UoM;

                    try
                    {
                        int movId;

                        if (idSes > 0)
                        {
                            var loc = pick.LocationCode ?? "";
                            var mg  = !string.IsNullOrEmpty(pick.WarehouseCode)
                                          ? pick.WarehouseCode
                                          : WmsWarehouse.FromLocation(loc);
                            var (lineOk, mov, lineErr) = await _erp.InsertPickLineAsync(
                                olCod, articleCode, articleDesc, uom,
                                pick.PickedQty, opCode, nodeId, idSes, mg, loc);

                            if (!lineOk)
                            {
                                errors.Add($"{articleCode}: {lineErr}");
                                continue;
                            }
                            movId = mov;
                        }
                        else
                        {
                            var loc2 = pick.LocationCode ?? "";
                            var mg2  = !string.IsNullOrEmpty(pick.WarehouseCode)
                                           ? pick.WarehouseCode
                                           : WmsWarehouse.FromLocation(loc2);
                            movId = await _erp.InsertMovAsync(new ErpMovRequest(
                                ArticleCode:   articleCode,
                                CausalCode:    "SCAR",
                                Qty:           pick.PickedQty,
                                WarehouseCode: mg2,
                                LocationCode:  loc2,
                                OperatorCode:  opCode,
                                NodeId:        nodeId
                            ));

                            if (movId <= 0)
                            {
                                errors.Add($"{articleCode}: TRD_InsertMov ha restituito {movId}");
                                continue;
                            }
                        }

                        await _logic.MarkPickExecutedAsync(pick.Id, movId,
                            idSes > 0 ? idSes : null);
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "ExecuteStagedPicksAsync riga {Art}/{OlCod}", articleCode, olCod);
                        errors.Add($"{articleCode}: {ex.Message}");
                    }
                }
            }
            finally
            {
                // Chiude la sessione anche in caso di errori parziali
                if (idSes > 0)
                {
                    try { await _erp.ClosePickSessionAsync(idSes); }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "ExecuteStagedPicksAsync: ClosePickSession fallita IDSES={IdSes}", idSes);
                    }
                }
            }
        }

        await _logic.UpdatePickListStatusAsync(listId);
        return (ok, errors);
    }
}
