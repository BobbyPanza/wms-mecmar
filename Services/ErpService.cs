using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using WMS.Models;

namespace WMS.Services;

/// <summary>
/// Legge dati reali da FactoryMecmar via Dapper.
/// IsConfigured = false → connessione non configurata, il chiamante usa il mock.
/// </summary>
public class ErpService
{
    private readonly string? _connStr;
    private readonly string? _vaultDocBaseUrl;
    private readonly string[] _allowedWarehouses;
    private readonly ILogger<ErpService> _log;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connStr);

    public ErpService(IConfiguration config, IOptions<WmsOptions> wmsOpts, ILogger<ErpService> log)
    {
        _connStr = config.GetConnectionString("ErpDatabase");
        _vaultDocBaseUrl = config["VaultDoc:BaseUrl"]?.TrimEnd('/');
        _allowedWarehouses = wmsOpts.Value.AllowedWarehouses;
        _log = log;
    }

    private SqlConnection Open() => new(_connStr!);

    // ─── Operatori ────────────────────────────────────────────────────────────

    /// <summary>
    /// Valida codice + PIN contro A_OPR.
    /// Ritorna (ok, nome, role) dove role è "SUP" per supervisori, "OP" per tutti gli altri.
    /// </summary>
    public async Task<(bool Ok, string? Name, string? Role, string? GroupCode)> TryLoginAsync(string opcod, string pin)
    {
        try
        {
            using var db = Open();
            var row = await db.QueryFirstOrDefaultAsync<OprRow>(
                "SELECT OPCOD, OPDSC, OPPSW, GRCOD FROM dbo.A_OPR WHERE OPCOD = @Opcod",
                new { Opcod = opcod.Trim().ToUpper() });

            if (row is null) return (false, null, null, null);
            if ((row.OPPSW ?? "") != pin) return (false, null, null, null);

            var grcod = row.GRCOD ?? "";
            var role  = string.Equals(grcod, "SUP", StringComparison.OrdinalIgnoreCase) ? "SUP" : "OP";
            return (true, row.OPDSC, role, grcod);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.TryLoginAsync fallito per {Opcod}", opcod);
            throw;
        }
    }

    // ─── Articoli ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Cerca un articolo in A_PAR + giacenze in L_MLPA + ultimi movimenti in S_MOV.
    /// Ritorna null se non trovato.
    /// </summary>
    public async Task<ArticleDto?> GetArticleAsync(string code)
    {
        try
        {
            using var db = Open();

            var art = await db.QueryFirstOrDefaultAsync<ParRow>(
                @"SELECT p.PACOD, p.PADSC, p.PAUDM,
                         COALESCE(p.PAPDR, 0) AS PAPDR,
                         COALESCE(pq.PAORD, 0) AS PAORD,
                         COALESCE(pq.PAIMP, 0) AS PAIMP
                  FROM dbo.A_PAR p
                  LEFT JOIN dbo.L_PAQT pq ON pq.PACOD = p.PACOD
                  WHERE p.PACOD = @Code",
                new { Code = code.Trim() });
            if (art is null) return null;

            var locs = (await db.QueryAsync<MlpaRow>(
                @"SELECT m.MGCOD, m.LCCOD, m.QTLOC, m.QTMIN, m.QTMAX
                  FROM dbo.L_MLPA m
                  WHERE m.PACOD = @Code AND m.QTLOC > 0
                  ORDER BY m.QTLOC DESC",
                new { Code = art.PACOD })).ToList();

            var locationDtos = locs.Select(l => new ArticleLocationDto(
                WarehouseCode:  l.MGCOD ?? "",
                WarehouseDesc:  l.MGCOD ?? "",
                LocationCode:   l.LCCOD ?? "",
                LocationDesc:   "",
                Quantity:       l.QTLOC,
                MinQty:         l.QTMIN ?? 0m,
                MaxQty:         l.QTMAX ?? 0m,
                Priority:       "S",
                IsMainWithdrawal: false
            )).ToList();

            var movs = (await db.QueryAsync<MovRow>(
                @"SELECT TOP 30
                      m.MOSTP, m.CMCOD, c.CMDSC, m.MOQTV, m.OPCOD, m.MGCOD, m.LCCOD
                  FROM dbo.S_MOV m
                  LEFT JOIN dbo.A_CMM c ON c.CMCOD = m.CMCOD
                  WHERE m.PACOD = @Code
                    AND m.MOSTP >= DATEADD(month, -6, GETDATE())
                  ORDER BY m.MOSTP DESC",
                new { Code = art.PACOD })).ToList();

            var movDtos = movs.Select(m => new MovementDto(
                Timestamp:    m.MOSTP ?? DateTime.MinValue,
                CausalCode:   m.CMCOD ?? "",
                CausalDesc:   m.CMDSC ?? "",
                Quantity:     m.MOQTV ?? 0m,
                OperatorCode: m.OPCOD ?? "",
                OperatorName: "",
                DocumentRef:  "",
                WarehouseCode: m.MGCOD ?? "",
                LocationCode:  m.LCCOD ?? ""
            )).ToList();

            var (orders, engagements) = await GetArticleAvailabilityAsync(art.PACOD!, db);

            return new ArticleDto(
                Code:            art.PACOD ?? code,
                Description:     art.PADSC ?? "",
                UoM:             art.PAUDM ?? "",
                TotalStock:      locationDtos.Sum(l => l.Quantity),
                MinStock:        locationDtos.Select(l => l.MinQty).FirstOrDefault(),
                Locations:       locationDtos,
                RecentMovements: movDtos,
                Ordered:         art.PAORD,
                Engaged:         art.PAIMP,
                MinStockPdr:     art.PAPDR,
                Orders:          orders,
                Engagements:     engagements
            );
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.GetArticleAsync fallito per {Code}", code);
            throw;
        }
    }

    private async Task<(List<ArticleOrderLineDto> Orders, List<ArticleOrderLineDto> Engagements)>
        GetArticleAvailabilityAsync(string pacod, SqlConnection db)
    {
        try
        {
            var rows = (await db.QueryAsync<OrdLineRow>(
                @"SELECT RowType, OrderCode, Description, Qty, DueDate
                  FROM dbo.WMS_V_ArticleAvailability
                  WHERE PACOD = @P ORDER BY DueDate",
                new { P = pacod })).ToList();

            // RowType 5 = ordine produzione, 7 = ordine acquisto
            var orders = rows
                .Where(r => r.RowType is 5 or 7)
                .Select(r => new ArticleOrderLineDto(r.OrderCode ?? "", r.Description ?? "", r.Qty, r.DueDate))
                .ToList();

            // RowType 3 = riga ordine cliente, 4 = impegno produzione, 6 = workplan
            var engaged = rows
                .Where(r => r.RowType is 3 or 4 or 6)
                .Select(r => new ArticleOrderLineDto(r.OrderCode ?? "", r.Description ?? "", r.Qty, r.DueDate))
                .ToList();

            return (orders, engaged);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "GetArticleAvailabilityAsync {Pacod}", pacod);
            return ([], []);
        }
    }

    private class OrdLineRow
    {
        public short     RowType     { get; set; }
        public string?   OrderCode   { get; set; }
        public string?   Description { get; set; }
        public decimal   Qty         { get; set; }
        public DateTime? DueDate     { get; set; }
    }

    // ─── Locazioni ────────────────────────────────────────────────────────────

    /// <summary>
    /// Cerca una locazione per codice (LCCOD) in A_LOC.
    /// Popola contenuto da L_MLPA e movimenti da S_MOV.
    /// Ritorna null se non trovata.
    /// </summary>
    public async Task<LocationDto?> GetLocationAsync(string locationCode)
    {
        try
        {
            using var db = Open();

            // Cerca per LCCOD (prende la prima corrispondenza se più magazzini)
            var loc = await db.QueryFirstOrDefaultAsync<LocRow>(
                "SELECT TOP 1 MGCOD, LCCOD FROM dbo.A_LOC WHERE LCCOD = @LcCod",
                new { LcCod = locationCode.Trim().ToUpper() });
            if (loc is null) return null;

            var mgCod = loc.MGCOD ?? "";
            var lcCod = loc.LCCOD ?? locationCode;

            var contents = (await db.QueryAsync<ContentRow>(
                @"SELECT TOP 200 m.PACOD, p.PADSC, p.PAUDM, m.QTLOC
                  FROM dbo.L_MLPA m
                  LEFT JOIN dbo.A_PAR p ON p.PACOD = m.PACOD
                  WHERE m.MGCOD = @MgCod AND m.LCCOD = @LcCod AND m.QTLOC > 0
                  ORDER BY m.QTLOC DESC",
                new { MgCod = mgCod, LcCod = lcCod })).ToList();

            var contentDtos = contents.Select(c => new LocationContentDto(
                ArticleCode: c.PACOD ?? "",
                ArticleDesc: c.PADSC ?? "",
                UoM:         c.PAUDM ?? "",
                Quantity:    c.QTLOC,
                Priority:    "S"
            )).ToList();

            var movs = (await db.QueryAsync<LocMovRow>(
                @"SELECT TOP 30
                      m.MOSTP, m.CMCOD, c.CMDSC, m.MOQTV, m.OPCOD, m.PACOD, m.MGCOD
                  FROM dbo.S_MOV m
                  LEFT JOIN dbo.A_CMM c ON c.CMCOD = m.CMCOD
                  WHERE m.MGCOD = @MgCod AND m.LCCOD = @LcCod
                    AND m.MOSTP >= DATEADD(month, -6, GETDATE())
                  ORDER BY m.MOSTP DESC",
                new { MgCod = mgCod, LcCod = lcCod })).ToList();

            var movDtos = movs.Select(m => new MovementDto(
                Timestamp:    m.MOSTP ?? DateTime.MinValue,
                CausalCode:   m.CMCOD ?? "",
                CausalDesc:   m.CMDSC ?? "",
                Quantity:     m.MOQTV ?? 0m,
                OperatorCode: m.OPCOD ?? "",
                OperatorName: "",
                DocumentRef:  m.PACOD ?? "",
                WarehouseCode: mgCod,
                LocationCode:  lcCod
            )).ToList();

            return new LocationDto(
                WarehouseCode: mgCod,
                WarehouseDesc: mgCod,
                LocationCode:  lcCod,
                LocationDesc:  "",
                LocationType:  "S",
                Lane: null, HPos: null, VPos: null,
                IsAcceptance: false,
                IsFiscal:     false,
                Contents:       contentDtos,
                RecentMovements: movDtos
            );
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.GetLocationAsync fallito per {Code}", locationCode);
            throw;
        }
    }

    // ─── Inventario ───────────────────────────────────────────────────────────

    /// <summary>
    /// Magazzini da A_MAG, filtrati per tipo 'I' (interni — esclude Fornitori/Clienti/Terzisti).
    /// </summary>
    public async Task<List<WarehouseDto>> GetWarehousesAsync()
    {
        try
        {
            using var db = Open();
            var sql = "SELECT MGCOD, MGDSC, MGTYP FROM dbo.A_MAG WHERE MGTYP = 'I'";
            if (_allowedWarehouses.Length > 0) sql += " AND MGCOD IN @Allowed";
            sql += " ORDER BY MGCOD";
            var rows = await db.QueryAsync<MagRow>(sql, new { Allowed = _allowedWarehouses });
            return rows.Select(r => new WarehouseDto(
                Code:        r.MGCOD ?? "",
                Description: r.MGDSC ?? "",
                Type:        r.MGTYP ?? ""
            )).ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.GetWarehousesAsync fallito");
            throw;
        }
    }

    /// <summary>
    /// Righe inventario: L_MLPA JOIN A_PAR per magazzino + prefisso zona opzionale.
    /// </summary>
    public async Task<List<InventoryCount>> GetInventoryRowsAsync(string mgcod, string zonePrefix)
    {
        try
        {
            using var db = Open();
            var sql = @"
                SELECT m.PACOD, p.PADSC, p.PAUDM, m.MGCOD, m.LCCOD, m.QTLOC
                FROM dbo.L_MLPA m
                LEFT JOIN dbo.A_PAR p ON p.PACOD = m.PACOD
                WHERE m.MGCOD = @MgCod";
            if (!string.IsNullOrWhiteSpace(zonePrefix))
                sql += " AND m.LCCOD LIKE @Zone";
            sql += " ORDER BY m.LCCOD, m.PACOD";

            var rows = await db.QueryAsync<InvRow>(sql,
                new { MgCod = mgcod, Zone = zonePrefix.Trim().ToUpper() + "%" });

            return rows.Select(r => new InventoryCount
            {
                WarehouseCode = r.MGCOD ?? mgcod,
                ArticleCode   = r.PACOD ?? "",
                ArticleDesc   = r.PADSC ?? "",
                UoM           = r.PAUDM ?? "",
                LocationCode  = r.LCCOD ?? "",
                ExpectedQty   = r.QTLOC
            }).ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.GetInventoryRowsAsync fallito per {MgCod}/{Zone}", mgcod, zonePrefix);
            throw;
        }
    }

    /// <summary>
    /// Applica rettifica inventario: CINV (delta > 0) o SINV (delta &lt; 0).
    /// Delta = 0 non genera movimento.
    /// </summary>
    public async Task<(bool Ok, string Message)> ExecuteInventoryAdjustmentAsync(
        string pacod, string mgcod, string lccod,
        decimal delta, string operatorCode, string sessionRef)
    {
        if (delta == 0m) return (true, "nessuna rettifica");
        var cmcod = delta > 0 ? "CINV" : "SINV";
        try
        {
            using var db = Open();
            var p = BuildMovParams(pacod, cmcod, Math.Abs(delta), mgcod, lccod, operatorCode, sessionRef);
            await db.ExecuteAsync("dbo.TRD_InsertMov", p, commandType: System.Data.CommandType.StoredProcedure);
            var idMov = p.Get<int>("@ReturnVal");
            if (idMov <= 0)
                return (false, $"{pacod}: rettifica {cmcod} fallita (id={idMov})");
            return (true, $"{cmcod} #{idMov}: {(delta > 0 ? "+" : "")}{delta}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.ExecuteInventoryAdjustmentAsync fallito per {Pacod}", pacod);
            throw;
        }
    }

    // ─── Spostamento semplice ─────────────────────────────────────────────────

    /// <summary>
    /// Giacenza di un articolo in una specifica locazione (LCCOD).
    /// Ritorna anche il MGCOD trovato in L_MLPA (locazione con più stock).
    /// </summary>
    public async Task<(string MgCod, decimal Qty)> GetStockAtLocationAsync(string pacod, string lccod)
    {
        try
        {
            using var db = Open();
            var row = await db.QueryFirstOrDefaultAsync<StockRow>(
                @"SELECT TOP 1 MGCOD, ISNULL(QTLOC, 0) AS QTLOC
                  FROM dbo.L_MLPA
                  WHERE PACOD = @Pacod AND LCCOD = @LcCod AND QTLOC > 0
                  ORDER BY QTLOC DESC",
                new { Pacod = pacod, LcCod = lccod.Trim().ToUpper() });
            return row is null ? ("", 0m) : (row.MGCOD ?? "", row.QTLOC);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.GetStockAtLocationAsync fallito per {Pacod}/{LcCod}", pacod, lccod);
            throw;
        }
    }

    /// <summary>
    /// Restituisce il MGCOD di una locazione da A_LOC. Null se non trovata.
    /// </summary>
    public async Task<string?> GetLocationWarehouseAsync(string lccod)
    {
        try
        {
            using var db = Open();
            return await db.QueryFirstOrDefaultAsync<string>(
                "SELECT TOP 1 MGCOD FROM dbo.A_LOC WHERE LCCOD = @LcCod",
                new { LcCod = lccod.Trim().ToUpper() });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.GetLocationWarehouseAsync fallito per {LcCod}", lccod);
            throw;
        }
    }

    /// <summary>
    /// Esegue uno spostamento: SMI su origine + CMI su destinazione via TRD_InsertMov.
    /// I due movimenti sono correlati dallo stesso referenceCode.
    /// </summary>
    public async Task<(bool Ok, string Message)> ExecuteSimpleMoveAsync(
        string pacod,
        string srcMgcod, string srcLccod,
        string dstMgcod, string dstLccod,
        decimal qty, string operatorCode)
    {
        var refCode = $"WMS-MOV-{DateTime.Now:yyyyMMddHHmmss}";
        try
        {
            using var db = Open();

            // 1 — SMI: scarico da locazione origine
            var pSmi = BuildMovParams(pacod, "SMI", qty, srcMgcod, srcLccod, operatorCode, refCode);
            await db.ExecuteAsync("dbo.TRD_InsertMov", pSmi, commandType: System.Data.CommandType.StoredProcedure);
            var idSmi = pSmi.Get<int>("@ReturnVal");
            if (idSmi <= 0)
                return (false, $"Scarico origine fallito (IDMOV={idSmi} — causale SMI valida?)");

            // 2 — CMI: carico su locazione destinazione
            var pCmi = BuildMovParams(pacod, "CMI", qty, dstMgcod, dstLccod, operatorCode, refCode);
            await db.ExecuteAsync("dbo.TRD_InsertMov", pCmi, commandType: System.Data.CommandType.StoredProcedure);
            var idCmi = pCmi.Get<int>("@ReturnVal");
            if (idCmi <= 0)
                return (false, $"Carico destinazione fallito (IDMOV={idCmi} — causale CMI valida?)");

            return (true, $"Spostamento registrato — SMI #{idSmi} / CMI #{idCmi}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.ExecuteSimpleMoveAsync fallito per {Pacod}", pacod);
            throw;
        }
    }

    private static DynamicParameters BuildMovParams(
        string pacod, string cmcod, decimal qty,
        string mgcod, string lccod,
        string operatorCode, string refCode)
    {
        var p = new DynamicParameters();
        p.Add("@sPACOD",        pacod);
        p.Add("@sCMCOD",        cmcod);
        p.Add("@fMOQTV",        qty);
        p.Add("@sMGCOD",        mgcod);
        p.Add("@sLCCOD",        lccod);
        p.Add("@operatorCode",  operatorCode);
        p.Add("@referenceCode", refCode);
        p.Add("@ReturnVal",     dbType: System.Data.DbType.Int32,
              direction: System.Data.ParameterDirection.ReturnValue);
        return p;
    }

    // ─── Rettifiche semplici ─────────────────────────────────────────────────

    /// <summary>
    /// Rettifica semplice: imposta la giacenza a newQty per articolo+locazione.
    /// Calcola il delta rispetto alla giacenza attuale e invia REP (positivo) o REN (negativo).
    /// Se delta=0 non genera movimento.
    /// </summary>
    public async Task<(bool Ok, string Message)> ExecuteAdjustmentAsync(
        string pacod, string mgcod, string lccod,
        decimal newQty, string operatorCode)
    {
        try
        {
            using var db = Open();
            var current = await db.QueryFirstOrDefaultAsync<decimal?>(
                "SELECT TOP 1 QTLOC FROM dbo.L_MLPA WHERE PACOD=@Pa AND MGCOD=@Mg AND LCCOD=@Lc",
                new { Pa = pacod, Mg = mgcod, Lc = lccod }) ?? 0m;

            var delta = newQty - current;
            if (delta == 0m) return (true, $"{pacod}: giacenza già a {newQty}");

            var cmcod = delta > 0 ? "REP" : "REN";
            var refCode = $"WMS-RTT-{DateTime.Now:yyyyMMddHHmmss}";
            var p = BuildMovParams(pacod, cmcod, Math.Abs(delta), mgcod, lccod, operatorCode, refCode);
            await db.ExecuteAsync("dbo.TRD_InsertMov", p, commandType: System.Data.CommandType.StoredProcedure);
            var idMov = p.Get<int>("@ReturnVal");
            if (idMov <= 0)
                return (false, $"{pacod}: rettifica {cmcod} fallita (id={idMov} — causale REP/REN valida?)");

            return (true, $"{cmcod} #{idMov}: {current} → {newQty} (delta {(delta > 0 ? "+" : "")}{delta})");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.ExecuteAdjustmentAsync fallito per {Pacod}", pacod);
            throw;
        }
    }

    // ─── Nodo dispositivo (A_NOD) ────────────────────────────────────────────

    /// <summary>
    /// Registra un nuovo nodo WMS in A_NOD (PRDCD=10006) e ritorna il CDNOD assegnato.
    /// Se esiste già un nodo con lo stesso CDNOD (da cookie), ne aggiorna il nome e ritorna quello.
    /// Se cdnodFromCookie=0 → crea sempre un nuovo nodo.
    /// </summary>
    public async Task<int> RegisterOrGetNodeAsync(int cdnodFromCookie, string deviceName)
    {
        try
        {
            using var db = Open();

            if (cdnodFromCookie > 0)
            {
                var exists = await db.QueryFirstOrDefaultAsync<int?>(
                    "SELECT CDNOD FROM dbo.A_NOD WHERE CDNOD = @Cdnod AND PRDCD = 10006",
                    new { Cdnod = cdnodFromCookie });
                if (exists.HasValue)
                {
                    await db.ExecuteAsync(
                        "UPDATE dbo.A_NOD SET PCNAM = @Name, TSCON = SYSDATETIME() WHERE CDNOD = @Cdnod",
                        new { Name = deviceName, Cdnod = cdnodFromCookie });
                    return cdnodFromCookie;
                }
            }

            // Nuovo nodo — trigger BEFORE INSERT assegna CDNOD automaticamente
            await db.ExecuteAsync(
                @"INSERT INTO dbo.A_NOD (PRDCD, CDGRP, CDNOD, PCNAM, PCSRV, TSCON)
                  VALUES (10006, 0, 0, @Name, 'N', SYSDATETIME())",
                new { Name = deviceName });

            return await db.QuerySingleAsync<int>(
                "SELECT MAX(CDNOD) FROM dbo.A_NOD WHERE PRDCD = 10006 AND PCNAM = @Name",
                new { Name = deviceName });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.RegisterOrGetNodeAsync fallito per {Name}", deviceName);
            throw;
        }
    }

    /// <summary>
    /// Aggiorna OPLOG e TMLOG al login operatore sul nodo.
    /// </summary>
    public async Task UpdateNodeLoginAsync(int cdnod, string operatorCode)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                "UPDATE dbo.A_NOD SET OPLOG = @OpCod, TMLOG = SYSDATETIME(), TSCON = SYSDATETIME() WHERE CDNOD = @Cdnod",
                new { OpCod = operatorCode, Cdnod = cdnod });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.UpdateNodeLoginAsync fallito per {Cdnod}", cdnod);
            throw;
        }
    }

    // ─── Prelievo produzione ─────────────────────────────────────────────────

    /// <summary>
    /// Carica la lista di prelievo per una bolla (OLCOD) da WMS_V_PickList
    /// (vista su tabelle base L_ODLA/A_LOT/L_CMFE/a_THP/A_PAR/L_MLPA/S_PAP).
    /// Ritorna null se la bolla non esiste o non ha righe.
    /// Include TUTTE le righe — il filtro "solo da fare" è gestito in UI.
    /// </summary>
    public async Task<ProductionPickListDto?> GetProductionPickListAsync(string olcod)
    {
        try
        {
            using var db = Open();

            // Testata bolla: commessa, stato, flag terminata
            var header = await db.QueryFirstOrDefaultAsync<PickHeaderRow>(
                @"SELECT
                      odl.OLCOD,
                      com.COCOD,
                      com.CTDSC,
                      lav.CONUM,
                      lav.LOCOD,
                      (SELECT STDSC FROM dbo.A_STA WHERE STCOD = odl.OLSTF) AS Stato,
                      CASE WHEN com.COSTO = 25 THEN 1 ELSE 0 END AS Terminata,
                      CASE WHEN lot.LOSTO = 35 THEN 1 ELSE 0 END AS LottoTerminato,
                      bv.OLCOD AS BollaVersamento
                  FROM dbo.S_ODL odl
                  JOIN dbo.L_ODLA lav  ON lav.OLCOD = odl.OLCOD
                  JOIN dbo.A_LOT lot   ON lot.CONUM  = lav.CONUM AND lot.LOCOD = lav.LOCOD
                  JOIN dbo.A_COM com   ON com.CONUM  = lot.CONUM
                  LEFT JOIN (
                      SELECT CONUM, LOCOD, MAX(OLCOD) AS OLCOD
                      FROM dbo.A_LAV WHERE LAUFC = 'Y' GROUP BY CONUM, LOCOD
                  ) bv ON bv.CONUM = lot.CONUM AND bv.LOCOD = lot.LOCOD
                  WHERE odl.OLCOD = @OlCod",
                new { OlCod = olcod.Trim().ToUpper() });

            if (header is null) return null;

            // Righe da WMS_V_PickList
            var rows = (await db.QueryAsync<PickListRow>(
                @"SELECT OLCOD, CONUM, LOCOD, PACOD, PADSC, PAUDM,
                         Handling, Ubicazione, Giacenza,
                         QtaDaPrelevare, QtaPrelevata, IDPAP
                  FROM dbo.WMS_V_PickList
                  WHERE OLCOD = @OlCod
                  ORDER BY Handling, PADSC",
                new { OlCod = olcod.Trim().ToUpper() })).ToList();

            if (rows.Count == 0) return null;

            var pickRows = rows.Select(r => new ProductionPickRowDto(
                PaCod:          r.PACOD ?? "",
                PaDsc:          r.PADSC ?? "",
                PaUdm:          r.PAUDM ?? "",
                Handling:       r.Handling ?? "(N.D.)",
                Ubicazione:     r.Ubicazione ?? "",
                QtaDaPrelevare: r.QtaDaPrelevare,
                QtaPrelevata:   r.QtaPrelevata,
                Giacenza:       r.Giacenza,
                Conum:          r.CONUM,
                LoCode:         r.LOCOD,
                IdPap:          r.IDPAP
            )).ToList();

            return new ProductionPickListDto(
                OlCod:          header.OLCOD ?? olcod,
                CoCod:          header.COCOD ?? "",
                ComDesc:        header.CTDSC ?? "",
                Conum:          header.CONUM,
                LoCode:         header.LOCOD,
                Stato:          header.Stato ?? "",
                Terminata:      header.Terminata == 1,
                LottoTerminato: header.LottoTerminato == 1,
                BollaVersamento: header.BollaVersamento,
                Rows:           pickRows
            );
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.GetProductionPickListAsync fallito per {OlCod}", olcod);
            throw;
        }
    }

    /// <summary>
    /// Apre una sessione di prelievo (S_SES) per il batch. Ritorna IDSES o -1 con errMsg.
    /// </summary>
    public async Task<(int IdSes, string ErrMsg)> OpenPickSessionAsync(
        string olcod, string operatorCode, int nodeId)
    {
        try
        {
            using var db = Open();
            var p = new DynamicParameters();
            p.Add("@OLCOD",  olcod);
            p.Add("@OPCOD",  operatorCode);
            p.Add("@NOCOD",  (short)nodeId);
            p.Add("@IDSES",  dbType: System.Data.DbType.Int32, size: 4,
                             direction: System.Data.ParameterDirection.Output);
            p.Add("@ErrMsg", dbType: System.Data.DbType.String, size: 255,
                             direction: System.Data.ParameterDirection.Output);
            await db.ExecuteAsync("dbo.WMS_OpenPickSession", p,
                commandType: System.Data.CommandType.StoredProcedure);
            var idSes  = p.Get<int>("@IDSES");
            var errMsg = p.Get<string>("@ErrMsg") ?? "";
            return (idSes, errMsg);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.OpenPickSessionAsync fallito {Olcod}", olcod);
            throw;
        }
    }

    /// <summary>
    /// Registra una riga di prelievo su sessione esistente.
    /// S_PAP (upsert) → S_SPP → TRD_InsertMov SCAR (IDTBR=5). Ritorna (ok, idMov, errMsg).
    /// </summary>
    public async Task<(bool Ok, int IdMov, string ErrMsg)> InsertPickLineAsync(
        string olcod, string pacod, string padsc, string paudm,
        decimal qty, string operatorCode, int nodeId, int idSes,
        string mgcod = "", string lccod = "")
    {
        try
        {
            using var db = Open();
            var p = new DynamicParameters();
            p.Add("@OLCOD",  olcod);
            p.Add("@PACOD",  pacod);
            p.Add("@PADSC",  padsc);
            p.Add("@PAUDM",  paudm);
            p.Add("@PAQTP",  qty);
            p.Add("@OPCOD",  operatorCode);
            p.Add("@NOCOD",  (short)nodeId);
            p.Add("@IDSES",  idSes);
            p.Add("@MGCOD",  mgcod);
            p.Add("@LCCOD",  lccod);
            p.Add("@IDMOV",  dbType: System.Data.DbType.Int32,
                             direction: System.Data.ParameterDirection.Output);
            p.Add("@ErrMsg", dbType: System.Data.DbType.String, size: 255,
                             direction: System.Data.ParameterDirection.Output);
            await db.ExecuteAsync("dbo.WMS_InsertPickLine", p,
                commandType: System.Data.CommandType.StoredProcedure);
            var idMov  = p.Get<int>("@IDMOV");
            var errMsg = p.Get<string>("@ErrMsg") ?? "";
            if (idMov <= 0)
                return (false, -1, errMsg.Length > 0 ? errMsg : "Riga non registrata");
            return (true, idMov, $"SCAR #{idMov} — {qty} {paudm}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.InsertPickLineAsync fallito {Olcod}/{Pacod}", olcod, pacod);
            throw;
        }
    }

    /// <summary>
    /// Chiude la sessione di prelievo (SESTO=2).
    /// </summary>
    public async Task ClosePickSessionAsync(int idSes)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync("dbo.WMS_ClosePickSession",
                new { IDSES = idSes },
                commandType: System.Data.CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.ClosePickSessionAsync fallito IDSES={IdSes}", idSes);
            throw;
        }
    }

    // ─── Liste di prelievo (Pick List service) ───────────────────────────────

    /// <summary>
    /// Carica le righe da WMS_V_PickList per una bolla e le converte in PickListRowDto.
    /// </summary>
    public async Task<List<PickListRowDto>> GetPickListRowsForOlCodAsync(string olCod)
    {
        try
        {
            using var db = Open();
            var rows = (await db.QueryAsync<PickListRow>(
                @"SELECT v.OLCOD, v.PACOD, v.PADSC, v.PAUDM,
                         v.Handling, v.Ubicazione, v.Giacenza,
                         v.QtaDaPrelevare, v.QtaPrelevata, v.CONUM, v.LOCOD,
                         COALESCE(p.PAF02,'') AS Paf02Real,
                         sp.Description       AS SpecsDesc
                  FROM dbo.WMS_V_PickList v
                  LEFT JOIN dbo.A_PAR p  ON p.PACOD  = v.PACOD
                  LEFT JOIN dbo.SpecsPart sp ON sp.ID = p.IDSpec
                  WHERE v.OLCOD = @OlCod
                  ORDER BY v.Handling, v.PADSC",
                new { OlCod = olCod.Trim().ToUpper() })).ToList();

            return rows
                .GroupBy(r => (Pacod: r.PACOD ?? "", Olcod: r.OLCOD ?? ""))
                .Select(g =>
                {
                    var first          = g.First();
                    var qtaDaPrelevare = g.Sum(r => r.QtaDaPrelevare);
                    // QtaPrelevata è identica su tutte le righe duplicate (stessa join S_PAP per OLCOD+PACOD)
                    var qtaPrelevata   = g.Max(r => r.QtaPrelevata);
                    var paf02          = g.Select(r => r.Paf02Real).FirstOrDefault(s => !string.IsNullOrEmpty(s))
                                        ?? g.Select(r => r.Ubicazione).FirstOrDefault(s => !string.IsNullOrEmpty(s))
                                        ?? "";
                    return new PickListRowDto
                    {
                        ArticleCode      = first.PACOD ?? "",
                        ArticleDesc      = first.PADSC ?? "",
                        UoM              = first.PAUDM ?? "",
                        PlannedQty       = Math.Max(0, qtaDaPrelevare - qtaPrelevata),
                        OlCod            = first.OLCOD,
                        Handling         = first.Handling ?? "",
                        Paf02            = paf02,
                        SpecsDescription = first.SpecsDesc
                    };
                })
                .Where(r => r.PlannedQty > 0)
                .ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.GetPickListRowsForOlCodAsync {OlCod}", olCod);
            throw;
        }
    }

    /// <summary>
    /// Per ogni OlCod fornito, ritorna le info di versamento se la fase è abilitata
    /// (A_LAV.FAABV='Y' e LAUFC='Y'). Un OlCod senza fase abilitata non compare nel risultato.
    /// </summary>
    public async Task<List<PickListVersamentoInfoDto>> GetVersamentoInfoForOlCodsAsync(
        IEnumerable<string> olCods)
    {
        var list = olCods.Select(c => c.Trim().ToUpper()).Distinct().ToList();
        if (list.Count == 0) return [];
        try
        {
            using var db = Open();
            return (await db.QueryAsync<PickListVersamentoInfoDto>(
                @"SELECT DISTINCT
                      odla.OLCOD  AS PickOlCod,
                      lav.OLCOD   AS BollaVersamento,
                      lot.PACOD   AS ArticleCode,
                      lot.PADSC   AS ArticleDesc,
                      lot.PAUDM   AS UoM,
                      lot.LOQTP   AS Qty
                  FROM dbo.L_ODLA odla
                  JOIN dbo.A_LOT  lot ON lot.CONUM = odla.CONUM AND lot.LOCOD = odla.LOCOD
                  JOIN dbo.A_LAV  lav ON lav.CONUM = lot.CONUM  AND lav.LOCOD = lot.LOCOD
                                     AND lav.FAABV = 'Y' AND lav.LAUFC = 'Y'
                  WHERE odla.OLCOD IN @OlCods
                    AND lav.OLCOD IS NOT NULL",
                new { OlCods = list })).ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.GetVersamentoInfoForOlCodsAsync");
            throw;
        }
    }

    /// <summary>
    /// Esegue il versamento di una riga: chiama WMS_InsertVersamentoLine su sessione già aperta.
    /// </summary>
    public async Task<(int IdMov, string ErrMsg)> InsertVersamentoLineAsync(
        string bollaVers, string pacod, string padsc, string paudm, decimal paqtb,
        string opCode, int nodeId, int idSes,
        string mgCod = "", string lcCod = "")
    {
        try
        {
            using var db = Open();
            var p = new DynamicParameters();
            p.Add("@BollaVers", bollaVers);
            p.Add("@PACOD",     pacod);
            p.Add("@PADSC",     padsc);
            p.Add("@PAUDM",     paudm);
            p.Add("@PAQTB",     paqtb);
            p.Add("@OPCOD",     opCode);
            p.Add("@NOCOD",     (short)nodeId);
            p.Add("@IDSES",     idSes);
            p.Add("@MGCOD",     mgCod);
            p.Add("@LCCOD",     lcCod);
            p.Add("@IDMOV",     dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);
            p.Add("@ErrMsg",    dbType: System.Data.DbType.String, size: 255, direction: System.Data.ParameterDirection.Output);
            await db.ExecuteAsync("dbo.WMS_InsertVersamentoLine", p, commandType: System.Data.CommandType.StoredProcedure);
            return (p.Get<int>("@IDMOV"), p.Get<string>("@ErrMsg") ?? "");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.InsertVersamentoLineAsync {BollaVers}/{Pacod}", bollaVers, pacod);
            throw;
        }
    }

    /// <summary>
    /// Giacenze batch: una query per N articoli.
    /// Ritorna dizionario ArticleCode → List&lt;ArticleLocationDto&gt; (solo locazioni con QTLOC > 0,
    /// ordinate per QTLOC DESC, con IsMainWithdrawal = LCPRC='Y').
    /// </summary>
    public async Task<Dictionary<string, List<ArticleLocationDto>>> GetStockBatchAsync(
        IEnumerable<string> articleCodes)
    {
        try
        {
            var codes = articleCodes.ToList();
            if (codes.Count == 0) return [];

            using var db = Open();
            var rows = new List<MlpaBatchRow>();
            foreach (var chunk in Chunk(codes, 1000))
                rows.AddRange(await db.QueryAsync<MlpaBatchRow>(
                    @"SELECT PACOD, MGCOD, LCCOD, QTLOC, LCPRC
                      FROM dbo.L_MLPA
                      WHERE PACOD IN @Codes AND QTLOC > 0
                      ORDER BY PACOD, QTLOC DESC",
                    new { Codes = chunk }));

            return rows.GroupBy(r => r.PACOD ?? "")
                       .ToDictionary(
                           g => g.Key,
                           g => g.Select(r => new ArticleLocationDto(
                               WarehouseCode:    r.MGCOD ?? "",
                               WarehouseDesc:    r.MGCOD ?? "",
                               LocationCode:     r.LCCOD ?? "",
                               LocationDesc:     "",
                               Quantity:         r.QTLOC,
                               MinQty:           0m,
                               MaxQty:           0m,
                               Priority:         r.LCPRC == "Y" ? "P" : "S",
                               IsMainWithdrawal: r.LCPRC == "Y"
                           )).ToList());
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.GetStockBatchAsync");
            throw;
        }
    }

    /// <summary>
    /// Wrapper generico per TRD_InsertMov. Ritorna IDMOV (>0) o &lt;=0 se fallito.
    /// </summary>
    public async Task<int> InsertMovAsync(ErpMovRequest req)
    {
        try
        {
            using var db = Open();
            var p = BuildMovParams(req.ArticleCode, req.CausalCode, req.Qty,
                                   req.WarehouseCode, req.LocationCode,
                                   req.OperatorCode, req.ReferenceCode ?? "");
            await db.ExecuteAsync("dbo.TRD_InsertMov", p,
                commandType: System.Data.CommandType.StoredProcedure);
            return p.Get<int>("@ReturnVal");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.InsertMovAsync {Art}/{Causal}", req.ArticleCode, req.CausalCode);
            throw;
        }
    }

    // ─── Documenti articolo (VaultDoc) ───────────────────────────────────────

    public async Task<List<ArticleDocument>> GetArticleDocumentsAsync(string pacod)
    {
        if (string.IsNullOrEmpty(_vaultDocBaseUrl)) return [];
        try
        {
            using var db = Open();
            var rows = await db.QueryAsync<VaultDocRow>(
                "SELECT RelativePath, Nome FROM dbo.WMS_V_ArticleDocuments WHERE PACOD = @Pacod",
                new { Pacod = pacod });
            return rows.Select(r => new ArticleDocument(r.Nome ?? "", $"{_vaultDocBaseUrl}/{r.RelativePath}")).ToList();
        }
        catch (Exception ex) { _log.LogError(ex, "ErpService.GetArticleDocumentsAsync {Pacod}", pacod); throw; }
    }

    // ─── Dettaglio articolo (dialog info) ────────────────────────────────────

    public async Task<ArticleInfoDto?> GetArticleInfoAsync(string code)
    {
        try
        {
            using var db = Open();
            var row = await db.QueryFirstOrDefaultAsync<ArticleInfoRow>(
                @"SELECT p.PACOD, p.PADSC, p.PAUDM,
                         COALESCE(p.FMCOD, '') AS FMCOD,
                         COALESCE(p.PAF02, '') AS PAF02,
                         thp.THDSC             AS Handling,
                         sp.Description        AS SpecsDesc
                  FROM dbo.A_PAR p
                  LEFT JOIN dbo.A_THP     thp ON thp.IDTHP = p.IDTHP
                  LEFT JOIN dbo.SpecsPart sp  ON sp.ID      = p.IDSpec
                  WHERE p.PACOD = @Code",
                new { Code = code.Trim().ToUpper() });
            if (row is null) return null;
            return new ArticleInfoDto(
                row.PACOD ?? "",
                row.PADSC ?? "",
                row.PAUDM ?? "",
                row.FMCOD ?? "",
                row.PAF02 ?? "",
                row.Handling,
                row.SpecsDesc
            );
        }
        catch (Exception ex) { _log.LogError(ex, "ErpService.GetArticleInfoAsync {Code}", code); throw; }
    }

    // ─── Gestione Locazioni ───────────────────────────────────────────────────

    /// <summary>Tutti i magazzini (MGCOD distinti da A_LOC), ordinati.</summary>
    public async Task<List<string>> GetAllWarehouseCodesAsync()
    {
        try
        {
            using var db = Open();
            var sql = "SELECT DISTINCT MGCOD FROM dbo.A_LOC WHERE MGCOD IS NOT NULL";
            if (_allowedWarehouses.Length > 0) sql += " AND MGCOD IN @Allowed";
            sql += " ORDER BY MGCOD";
            return (await db.QueryAsync<string>(sql, new { Allowed = _allowedWarehouses })).ToList();
        }
        catch (Exception ex) { _log.LogError(ex, "GetAllWarehouseCodesAsync"); throw; }
    }

    /// <summary>True se la coppia MGCOD+LCCOD esiste già in A_LOC.</summary>
    public async Task<bool> LocationExistsAsync(string mgCod, string lcCod)
    {
        try
        {
            using var db = Open();
            return await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.A_LOC WHERE MGCOD=@Mg AND LCCOD=@Lc",
                new { Mg = mgCod, Lc = lcCod.Trim().ToUpper() }) > 0;
        }
        catch (Exception ex) { _log.LogError(ex, "LocationExistsAsync {Mg}/{Lc}", mgCod, lcCod); throw; }
    }

    /// <summary>
    /// Crea una nuova locazione in A_LOC.
    /// NOTA: verificare con IT se A_LOC ammette INSERT diretto; se esiste SP, sostituire con quella.
    /// </summary>
    public async Task CreateLocationAsync(string mgCod, string lcCod)
    {
        try
        {
            using var db = Open();
            // LCSTO = Stato Locazione, NOT NULL — 9 = attiva/normale (default Mecmar)
            await db.ExecuteAsync(
                "INSERT INTO dbo.A_LOC (MGCOD, LCCOD, LCSTO) VALUES (@Mg, @Lc, 9)",
                new { Mg = mgCod, Lc = lcCod.Trim().ToUpper() });
        }
        catch (Exception ex) { _log.LogError(ex, "CreateLocationAsync {Mg}/{Lc}", mgCod, lcCod); throw; }
    }

    /// <summary>Locazioni abbinate a un articolo in L_MLPA, con giacenza e flag principale.</summary>
    public async Task<List<ManagedLocationDto>> GetArticleLocationsForMgmtAsync(string articleCode)
    {
        try
        {
            using var db = Open();
            return (await db.QueryAsync<ManagedLocationDto>(
                @"SELECT MGCOD AS WarehouseCode, LCCOD AS LocationCode,
                         QTLOC AS CurrentQty,
                         CASE WHEN LCPRC='Y' THEN 1 ELSE 0 END AS IsMain
                  FROM dbo.L_MLPA
                  WHERE PACOD = @Code
                  ORDER BY LCPRC DESC, LCCOD",
                new { Code = articleCode })).ToList();
        }
        catch (Exception ex) { _log.LogError(ex, "GetArticleLocationsForMgmtAsync {Art}", articleCode); throw; }
    }

    /// <summary>
    /// Abbina un articolo a una locazione (INSERT L_MLPA con QTLOC=0).
    /// NOTA: da verificare se A_LOC/L_MLPA ammettono scrittura diretta o serve SP.
    /// </summary>
    public async Task AssignArticleToLocationAsync(string articleCode, string mgCod, string lcCod)
    {
        try
        {
            using var db = Open();
            var exists = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.L_MLPA WHERE PACOD=@Pa AND MGCOD=@Mg AND LCCOD=@Lc",
                new { Pa = articleCode, Mg = mgCod, Lc = lcCod });
            if (exists > 0) return; // già abbinata
            await db.ExecuteAsync(
                "INSERT INTO dbo.L_MLPA (PACOD, MGCOD, LCCOD, QTLOC) VALUES (@Pa, @Mg, @Lc, 0)",
                new { Pa = articleCode, Mg = mgCod, Lc = lcCod.Trim().ToUpper() });
        }
        catch (Exception ex)
        { _log.LogError(ex, "AssignArticleToLocationAsync {Art}/{Mg}/{Lc}", articleCode, mgCod, lcCod); throw; }
    }

    /// <summary>Imposta LCPRC='Y' sulla locazione scelta e la rimuove da tutte le altre righe dell'articolo.</summary>
    public async Task SetMainLocationAsync(string articleCode, string mgCod, string lcCod)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                "UPDATE dbo.L_MLPA SET LCPRC='N' WHERE PACOD=@Pa",
                new { Pa = articleCode });
            await db.ExecuteAsync(
                "UPDATE dbo.L_MLPA SET LCPRC='Y' WHERE PACOD=@Pa AND MGCOD=@Mg AND LCCOD=@Lc",
                new { Pa = articleCode, Mg = mgCod, Lc = lcCod });
        }
        catch (Exception ex)
        { _log.LogError(ex, "SetMainLocationAsync {Art}/{Mg}/{Lc}", articleCode, mgCod, lcCod); throw; }
    }

    /// <summary>Rimuove il record da L_MLPA solo se QTLOC = 0. Ritorna false se giacenza presente.</summary>
    public async Task<bool> RemoveArticleLocationAsync(string articleCode, string mgCod, string lcCod)
    {
        try
        {
            using var db = Open();
            var qty = await db.ExecuteScalarAsync<decimal?>(
                "SELECT QTLOC FROM dbo.L_MLPA WHERE PACOD=@Pa AND MGCOD=@Mg AND LCCOD=@Lc",
                new { Pa = articleCode, Mg = mgCod, Lc = lcCod });
            if (qty is not null && qty != 0m) return false;
            await db.ExecuteAsync(
                "DELETE FROM dbo.L_MLPA WHERE PACOD=@Pa AND MGCOD=@Mg AND LCCOD=@Lc",
                new { Pa = articleCode, Mg = mgCod, Lc = lcCod });
            return true;
        }
        catch (Exception ex)
        { _log.LogError(ex, "RemoveArticleLocationAsync {Art}/{Mg}/{Lc}", articleCode, mgCod, lcCod); throw; }
    }

    // ─── Accettazione merce ──────────────────────────────────────────────────

    /// <summary>
    /// Elenco DDT da accettare da A_DOT filtrato per tipo e stato.
    /// Ogni documento include le righe articolo da A_DOR (solo righe con PACOD valorizzato).
    /// Colonne reali verificate sul DB: IDTES (PK), DTDO, DTSTO, DTCOD, DTDTE, IDFOR.
    /// </summary>
    public async Task<List<AcceptanceDocDto>> GetAcceptanceDocsAsync(int[] pendingStatuses)
    {
        try
        {
            using var db = Open();

            var headers = (await db.QueryAsync<AccDocHeaderRow>(
                @"SELECT IDTES, DTDO, DTSTO, IDFOR, DTCOD, DTDTE, FORAG
                  FROM dbo.WMS_V_AcceptanceDocs
                  WHERE DTSTO IN @Statuses
                  ORDER BY DTDTE DESC",
                new { Statuses = pendingStatuses })).ToList();

            if (headers.Count == 0) return [];

            var docIds = headers.Select(h => h.IDTES).ToList();
            var lines = new List<AccDocLineRow>();
            foreach (var chunk in Chunk(docIds, 1000))
                lines.AddRange(await db.QueryAsync<AccDocLineRow>(
                    @"SELECT v.IDRIG, v.IDTES, v.PACOD, v.DRDSC, v.DRUMI, v.DRQTI,
                             cr.MGCOD AS DRCR_MGCOD, cr.LCCOD AS DRCR_LCCOD
                      FROM dbo.WMS_V_AcceptanceLines v
                      LEFT JOIN (
                          SELECT IDRIG, MGCOD, LCCOD,
                                 ROW_NUMBER() OVER (PARTITION BY IDRIG ORDER BY ID) AS rn
                          FROM dbo.L_DRCR
                          WHERE CMTYP = 1
                      ) cr ON cr.IDRIG = v.IDRIG AND cr.rn = 1
                      WHERE v.IDTES IN @Ids
                      ORDER BY v.IDTES, v.DRPOS, v.IDRIG",
                    new { Ids = chunk }));

            return headers.Select(h => new AcceptanceDocDto(
                ErpDocId:     h.IDTES,
                DocType:      h.DTDO ?? "",
                DocumentRef:  h.DTCOD ?? h.IDTES.ToString(),
                SupplierName: h.FORAG ?? "",
                DocumentDate: h.DTDTE ?? DateTime.MinValue,
                Items: lines
                    .Where(r => r.IDTES == h.IDTES)
                    .Select(r => new AcceptanceItemDto
                    {
                        ErpLineId              = r.IDRIG,
                        ErpDocId               = h.IDTES,
                        ArticleCode            = r.PACOD ?? "",
                        ArticleDesc            = r.DRDSC ?? "",
                        UoM                    = r.DRUMI ?? "",
                        ExpectedQty            = r.DRQTI,
                        SuggestedWarehouseCode = r.DRCR_MGCOD ?? "",
                        SuggestedLocationCode  = r.DRCR_LCCOD ?? ""
                    }).ToList()
            )).ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.GetAcceptanceDocsAsync");
            throw;
        }
    }

    /// <summary>
    /// Righe di un singolo DDT da A_DOR (solo righe con articolo).
    /// Usata per ricaricare il dettaglio senza ricaricare tutta la lista.
    /// </summary>
    public async Task<List<AcceptanceItemDto>> GetAcceptanceLinesAsync(int erpDocId)
    {
        try
        {
            using var db = Open();
            var rows = (await db.QueryAsync<AccDocLineRow>(
                @"SELECT v.IDRIG, v.IDTES, v.PACOD, v.DRDSC, v.DRUMI, v.DRQTI,
                         cr.MGCOD AS DRCR_MGCOD, cr.LCCOD AS DRCR_LCCOD
                  FROM dbo.WMS_V_AcceptanceLines v
                  LEFT JOIN (
                      SELECT IDRIG, MGCOD, LCCOD,
                             ROW_NUMBER() OVER (PARTITION BY IDRIG ORDER BY ID) AS rn
                      FROM dbo.L_DRCR
                      WHERE CMTYP = 1
                  ) cr ON cr.IDRIG = v.IDRIG AND cr.rn = 1
                  WHERE v.IDTES = @DocId
                  ORDER BY v.DRPOS, v.IDRIG",
                new { DocId = erpDocId })).ToList();

            return rows.Select(r => new AcceptanceItemDto
            {
                ErpLineId              = r.IDRIG,
                ErpDocId               = erpDocId,
                ArticleCode            = r.PACOD ?? "",
                ArticleDesc            = r.DRDSC ?? "",
                UoM                    = r.DRUMI ?? "",
                ExpectedQty            = r.DRQTI,
                SuggestedWarehouseCode = r.DRCR_MGCOD ?? "",
                SuggestedLocationCode  = r.DRCR_LCCOD ?? ""
            }).ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.GetAcceptanceLinesAsync DocId={DocId}", erpDocId);
            throw;
        }
    }

    /// <summary>
    /// Restituisce la prima locazione con il flag di accettazione attivo in A_LOC.
    /// Colonna reale verificata: Acceptance (bit). Configurabile in AcceptanceOptions.LocationFlagColumn.
    /// </summary>
    public async Task<(string MgCod, string LcCod)?> GetAcceptanceDefaultLocationAsync(string flagColumn)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(flagColumn, @"^[A-Za-z_][A-Za-z0-9_]{0,63}$"))
        {
            _log.LogWarning("GetAcceptanceDefaultLocationAsync: nome colonna non valido '{Col}'", flagColumn);
            return null;
        }

        try
        {
            using var db = Open();
            var row = await db.QueryFirstOrDefaultAsync<LocRow>(
                $"SELECT TOP 1 MGCOD, LCCOD FROM dbo.A_LOC WHERE [{flagColumn}] = 1");
            if (row is null) return null;
            return (row.MGCOD ?? "", row.LCCOD ?? "");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.GetAcceptanceDefaultLocationAsync col={Col}", flagColumn);
            throw;
        }
    }

    /// <summary>
    /// Cerca locazioni in A_LOC per testo (LCCOD LIKE). Usata per il campo di ricerca locazione.
    /// Ritorna max 30 risultati.
    /// </summary>
    public async Task<List<(string MgCod, string LcCod)>> SearchLocationsAsync(string query)
    {
        try
        {
            using var db = Open();
            var sql = "SELECT TOP 30 MGCOD, LCCOD FROM dbo.A_LOC WHERE LCCOD LIKE @Q";
            if (_allowedWarehouses.Length > 0) sql += " AND MGCOD IN @Allowed";
            sql += " ORDER BY LCCOD";
            var rows = (await db.QueryAsync<LocRow>(sql,
                new { Q = query.Trim().ToUpper() + "%", Allowed = _allowedWarehouses })).ToList();
            return rows.Select(r => (r.MGCOD ?? "", r.LCCOD ?? "")).ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.SearchLocationsAsync");
            throw;
        }
    }

    /// <summary>
    /// Aggiorna DTSTO su A_DOT al valore "accettato" configurato.
    /// Chiamata a chiusura documento da AcceptanceService.CompleteDocAsync.
    /// </summary>
    public async Task<bool> CloseAcceptanceDocAsync(int erpDocId, int acceptedStatus)
    {
        try
        {
            using var db = Open();
            var rows = await db.ExecuteAsync(
                "UPDATE dbo.A_DOT SET DTSTO = @Status WHERE IDTES = @DocId",
                new { Status = acceptedStatus, DocId = erpDocId });
            return rows > 0;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.CloseAcceptanceDocAsync DocId={DocId}", erpDocId);
            throw;
        }
    }

    /// <summary>
    /// Esegue l'accettazione merce via TRD_InsertMov.
    /// Se srcMgcod/srcLccod sono valorizzati: SMI (scarico da L_DRCR) + causale carico (es. CMI).
    /// Altrimenti: solo causale carico (retrocompatibile).
    /// Ritorna IDMOV del carico (>0) oppure ≤0 se la causale non è valida.
    /// </summary>
    public async Task<(bool Ok, int IdMov, string Message)> ExecuteAcceptanceLoadAsync(
        string pacod, string mgcod, string lccod, decimal qty,
        string loadCausal, string dischargeCausal, string operatorCode,
        int erpDocId, int erpLineId, string docRef,
        string srcMgcod = "", string srcLccod = "")
    {
        var refCode = $"WMS-ACC-{docRef}-{erpLineId}-{DateTime.Now:HHmmss}";
        try
        {
            using var db = Open();

            // Scarico dal magazzino documento se la sorgente è nota
            if (!string.IsNullOrEmpty(srcLccod))
            {
                var pSmi = BuildMovParams(pacod, dischargeCausal, qty, srcMgcod, srcLccod, operatorCode, refCode);
                await db.ExecuteAsync("dbo.TRD_InsertMov", pSmi,
                    commandType: System.Data.CommandType.StoredProcedure);
                var idSmi = pSmi.Get<int>("@ReturnVal");
                if (idSmi <= 0)
                    return (false, idSmi,
                        $"Scarico {dischargeCausal} da {srcLccod} fallito (IDMOV={idSmi} — causale valida?)");
            }

            // Carico a destinazione
            var pCmi = BuildMovParams(pacod, loadCausal, qty, mgcod, lccod, operatorCode, refCode);
            await db.ExecuteAsync("dbo.TRD_InsertMov", pCmi,
                commandType: System.Data.CommandType.StoredProcedure);
            var idCmi = pCmi.Get<int>("@ReturnVal");
            if (idCmi <= 0)
                return (false, idCmi,
                    $"Carico {loadCausal} su {lccod} fallito (IDMOV={idCmi} — causale valida?)");

            var srcInfo = !string.IsNullOrEmpty(srcLccod) ? $"{srcLccod} → " : "";
            return (true, idCmi, $"{srcInfo}{lccod} +{qty} ({loadCausal} #{idCmi})");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ErpService.ExecuteAcceptanceLoadAsync {Pacod}/{DocId}", pacod, erpDocId);
            throw;
        }
    }

    private static IEnumerable<List<T>> Chunk<T>(IEnumerable<T> source, int size)
    {
        var list = source.ToList();
        for (int i = 0; i < list.Count; i += size)
            yield return list.Skip(i).Take(size).ToList();
    }

    // ─── Row types (Dapper) ───────────────────────────────────────────────────

    private class ArticleInfoRow
    {
        public string? PACOD    { get; set; }
        public string? PADSC    { get; set; }
        public string? PAUDM    { get; set; }
        public string? FMCOD    { get; set; }
        public string? PAF02    { get; set; }
        public string? Handling  { get; set; }
        public string? SpecsDesc { get; set; }
    }

    private class VaultDocRow
    {
        public string? RelativePath { get; set; }
        public string? Nome         { get; set; }
    }

    private class AccDocHeaderRow
    {
        public int       IDTES  { get; set; }   // A_DOT PK
        public string?   DTDO   { get; set; }   // tipo documento
        public short     DTSTO  { get; set; }   // stato documento
        public int       IDFOR  { get; set; }
        public string?   DTCOD  { get; set; }   // numero/codice documento
        public DateTime? DTDTE  { get; set; }   // data documento
        public string?   FORAG  { get; set; }   // A_FOR.FORAG
    }

    private class AccDocLineRow
    {
        public int      IDRIG  { get; set; }   // A_DOR PK
        public int      IDTES  { get; set; }   // A_DOR FK → A_DOT
        public string?  PACOD  { get; set; }
        public string?  DRDSC  { get; set; }   // descrizione riga
        public string?  DRUMI  { get; set; }   // unità di misura riga
        public decimal  DRQTI  { get; set; }   // quantità attesa
        public string?  DRCR_MGCOD { get; set; }  // L_DRCR — magazzino prelievo
        public string?  DRCR_LCCOD { get; set; }  // L_DRCR — locazione prelievo
    }

    private class MlpaBatchRow
    {
        public string? PACOD { get; set; }
        public string? MGCOD { get; set; }
        public string? LCCOD { get; set; }
        public decimal QTLOC { get; set; }
        public string? LCPRC { get; set; }
    }

    private class OprRow
    {
        public string? OPCOD { get; set; }
        public string? OPDSC { get; set; }
        public string? OPPSW { get; set; }
        public string? GRCOD { get; set; }
    }

    private class ParRow
    {
        public string?  PACOD  { get; set; }
        public string?  PADSC  { get; set; }
        public string?  PAUDM  { get; set; }
        public decimal  PAPDR  { get; set; }   // scorta minima
        public decimal  PAORD  { get; set; }   // totale ordinato (da L_PAQT)
        public decimal  PAIMP  { get; set; }   // totale impegnato (da L_PAQT)
    }

    private class MlpaRow
    {
        public string? MGCOD { get; set; }
        public string? LCCOD { get; set; }
        public decimal QTLOC { get; set; }
        public decimal? QTMIN { get; set; }
        public decimal? QTMAX { get; set; }
    }

    private class LocRow
    {
        public string? MGCOD { get; set; }
        public string? LCCOD { get; set; }
    }

    private class StockRow
    {
        public string?  MGCOD { get; set; }
        public decimal  QTLOC { get; set; }
    }

    private class MagRow
    {
        public string? MGCOD { get; set; }
        public string? MGDSC { get; set; }
        public string? MGTYP { get; set; }
    }

    private class InvRow
    {
        public string?  PACOD { get; set; }
        public string?  PADSC { get; set; }
        public string?  PAUDM { get; set; }
        public string?  MGCOD { get; set; }
        public string?  LCCOD { get; set; }
        public decimal  QTLOC { get; set; }
    }

    private class ContentRow
    {
        public string? PACOD { get; set; }
        public string? PADSC { get; set; }
        public string? PAUDM { get; set; }
        public decimal QTLOC { get; set; }
    }

    private class MovRow
    {
        public DateTime? MOSTP  { get; set; }
        public string?   CMCOD  { get; set; }
        public string?   CMDSC  { get; set; }
        public decimal?  MOQTV  { get; set; }
        public string?   OPCOD  { get; set; }
        public string?   MGCOD  { get; set; }
        public string?   LCCOD  { get; set; }
    }

    private class LocMovRow
    {
        public DateTime? MOSTP  { get; set; }
        public string?   CMCOD  { get; set; }
        public string?   CMDSC  { get; set; }
        public decimal?  MOQTV  { get; set; }
        public string?   OPCOD  { get; set; }
        public string?   PACOD  { get; set; }
        public string?   MGCOD  { get; set; }
    }

    private class PickHeaderRow
    {
        public string? OLCOD           { get; set; }
        public string? COCOD           { get; set; }
        public string? CTDSC           { get; set; }
        public int     CONUM           { get; set; }
        public int     LOCOD           { get; set; }
        public string? Stato           { get; set; }
        public int     Terminata       { get; set; }
        public int     LottoTerminato  { get; set; }
        public string? BollaVersamento { get; set; }
    }

    private class PickListRow
    {
        public string?  OLCOD          { get; set; }
        public int      CONUM          { get; set; }
        public int      LOCOD          { get; set; }
        public string?  PACOD          { get; set; }
        public string?  PADSC          { get; set; }
        public string?  PAUDM          { get; set; }
        public string?  Handling       { get; set; }
        public string?  Ubicazione     { get; set; }
        public decimal  Giacenza       { get; set; }
        public decimal  QtaDaPrelevare { get; set; }
        public decimal  QtaPrelevata   { get; set; }
        public int?     IDPAP          { get; set; }
        public string?  Paf02Real      { get; set; }
        public string?  SpecsDesc      { get; set; }
    }
}
