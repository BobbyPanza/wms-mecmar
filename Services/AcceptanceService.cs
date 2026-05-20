using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WMS.Models;

namespace WMS.Services;

/// <summary>
/// Orchestrazione accettazione merce: carica DDT da ERP, registra versamenti su Logic DB,
/// applica movimenti di carico su ERP, chiude il documento.
/// </summary>
public class AcceptanceService
{
    private readonly ErpService _erp;
    private readonly LogicService _logic;
    private readonly AcceptanceOptions _opts;
    private readonly ILogger<AcceptanceService> _log;

    public bool IsConfigured => _erp.IsConfigured && _logic.IsConfigured;

    public AcceptanceService(
        ErpService erp, LogicService logic,
        IOptions<AcceptanceOptions> options,
        ILogger<AcceptanceService> log)
    {
        _erp   = erp;
        _logic = logic;
        _opts  = options.Value;
        _log   = log;
    }

    // ─── Lettura documenti ────────────────────────────────────────────────────

    /// <summary>
    /// Elenco DDT pendenti da accettare, con avanzamento calcolato dai versamenti Logic DB.
    /// </summary>
    public async Task<List<AcceptanceDocDto>> LoadDocsAsync()
    {
        var docs = await _erp.GetAcceptanceDocsAsync(_opts.PendingStatuses);
        if (docs.Count == 0) return docs;

        var docIds = docs.Select(d => d.ErpDocId).ToList();
        var allLines = await _logic.GetAcceptanceLinesForDocsAsync(docIds);
        MergeVersamenti(docs, allLines);
        return docs;
    }

    /// <summary>
    /// Dettaglio completo di un singolo documento: righe ERP + versamenti Logic DB.
    /// </summary>
    public async Task<AcceptanceDocDto?> LoadDocDetailAsync(AcceptanceDocDto docHeader)
    {
        var items   = await _erp.GetAcceptanceLinesAsync(docHeader.ErpDocId);
        var versami = await _logic.GetAcceptanceLinesForDocAsync(docHeader.ErpDocId);

        var full = docHeader with { Items = items };
        MergeVersamenti([full], versami);
        return full;
    }

    // ─── Esecuzione versamento ────────────────────────────────────────────────

    /// <summary>
    /// Registra un versamento: carico ERP via TRD_InsertMov + insert Logic DB.
    /// Ritorna (ok, messaggio).
    /// </summary>
    public async Task<(bool Ok, string Message)> AcceptLineAsync(AcceptLineRequest req)
    {
        var (ok, idMov, msg) = await _erp.ExecuteAcceptanceLoadAsync(
            req.ArticleCode, req.WarehouseCode, req.LocationCode, req.Qty,
            _opts.LoadCausal, req.OperatorCode,
            req.ErpDocId, req.ErpLineId, req.DocumentRef);

        if (!ok) return (false, msg);

        await _logic.InsertAcceptanceLineAsync(new WmsAcceptanceLine
        {
            ErpDocId      = req.ErpDocId,
            ErpLineId     = req.ErpLineId,
            ArticleCode   = req.ArticleCode,
            WarehouseCode = req.WarehouseCode,
            LocationCode  = req.LocationCode,
            AcceptedQty   = req.Qty,
            ExpectedQty   = req.ExpectedQty,
            OperatorCode  = req.OperatorCode,
            ErpMovId      = idMov,
            AcceptedAt    = DateTime.Now,
            DocumentRef   = req.DocumentRef
        });

        return (true, msg);
    }

    // ─── Chiusura documento ───────────────────────────────────────────────────

    /// <summary>
    /// Chiude il documento aggiornando DTSO su A_DOT al valore "accettato" configurato.
    /// </summary>
    public async Task<(bool Ok, string Message)> CompleteDocAsync(int erpDocId)
    {
        var ok = await _erp.CloseAcceptanceDocAsync(erpDocId, _opts.AcceptedStatus);
        return ok
            ? (true,  $"Documento chiuso (stato → {_opts.AcceptedStatus})")
            : (false, "Aggiornamento stato documento fallito");
    }

    // ─── Locazione default ────────────────────────────────────────────────────

    /// <summary>
    /// Restituisce la locazione di default per l'accettazione (flag su A_LOC configurabile).
    /// </summary>
    public Task<(string MgCod, string LcCod)?> GetDefaultLocationAsync()
        => _erp.GetAcceptanceDefaultLocationAsync(_opts.LocationFlagColumn);

    /// <summary>
    /// Ricerca locazioni in A_LOC per testo (per autocomplete UI).
    /// </summary>
    public Task<List<(string MgCod, string LcCod)>> SearchLocationsAsync(string query)
        => _erp.SearchLocationsAsync(query);

    // ─── Selezione report etichetta ───────────────────────────────────────────

    /// <summary>
    /// Seleziona il nome del report etichetta applicando le DefaultReportRules configurate.
    /// Prima regola che fa match vince; ritorna "" se nessuna regola configurata.
    /// </summary>
    public string GetDefaultReport(string articleCode, string? family = null)
    {
        foreach (var rule in _opts.DefaultReportRules)
        {
            var value = rule.Field switch
            {
                "Family"      => family ?? "",
                "ArticleCode" => articleCode,
                _             => "*"
            };

            var matched = rule.Pattern == "*"
                || (rule.Field == "*")
                || Regex.IsMatch(value, rule.Pattern, RegexOptions.IgnoreCase);

            if (matched) return rule.ReportName;
        }
        return "";
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static void MergeVersamenti(
        List<AcceptanceDocDto> docs, List<WmsAcceptanceLine> allLines)
    {
        foreach (var doc in docs)
        {
            var docLines = allLines.Where(l => l.ErpDocId == doc.ErpDocId).ToList();
            foreach (var item in doc.Items)
            {
                var vers = docLines.Where(l => l.ErpLineId == item.ErpLineId).ToList();
                item.AcceptedQty = vers.Sum(l => l.AcceptedQty);
                item.Versamenti  = vers.Select(l => new AcceptanceVersamentoDto(
                    l.Id, l.AcceptedQty, l.WarehouseCode, l.LocationCode,
                    l.OperatorCode, l.AcceptedAt, l.ErpMovId
                )).ToList();
                item.Status = item.AcceptedQty >= item.ExpectedQty ? AcceptanceStatus.Done
                            : item.AcceptedQty > 0                 ? AcceptanceStatus.Partial
                            :                                         AcceptanceStatus.Pending;
            }
        }
    }
}
