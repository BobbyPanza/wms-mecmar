using Dapper;
using Microsoft.Data.SqlClient;
using WMS.Models;

namespace WMS.Services;

/// <summary>
/// Accesso al database Logic (WMS proprio).
/// Gestisce la persistenza del carrello operatore su WMS_Cart.
/// </summary>
public class LogicService
{
    private readonly string? _connStr;
    private readonly ILogger<LogicService> _log;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connStr);

    public LogicService(IConfiguration config, ILogger<LogicService> log)
    {
        _connStr = config.GetConnectionString("LogicDatabase");
        _log = log;
    }

    private SqlConnection Open() => new(_connStr!);

    /// <summary>
    /// Crea il database Logic e le tabelle WMS se non esistono.
    /// Da chiamare all'avvio dell'applicazione (Program.cs).
    /// </summary>
    public async Task EnsureSchemaAsync()
    {
        if (!IsConfigured) return;
        try
        {
            // Crea il DB se non esiste (connessione a master)
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_connStr!);
            var dbName  = builder.InitialCatalog;
            builder.InitialCatalog = "master";
            using (var master = new SqlConnection(builder.ConnectionString))
            {
                await master.ExecuteAsync($"""
                    IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = '{dbName}')
                        CREATE DATABASE [{dbName}];
                    """);
            }

            using var db = Open();
            await db.ExecuteAsync("""
                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WMS_Cart')
                CREATE TABLE dbo.WMS_Cart (
                    Id           UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_WMS_Cart PRIMARY KEY,
                    OperatorCode NVARCHAR(15)     NOT NULL,
                    ArticleCode  NVARCHAR(20)     NOT NULL,
                    ArticleDesc  NVARCHAR(200)    NOT NULL DEFAULT '',
                    UoM          NVARCHAR(10)     NOT NULL DEFAULT '',
                    SrcWarehouse NVARCHAR(15)     NOT NULL,
                    SrcLocation  NVARCHAR(15)     NOT NULL,
                    Qty          NUMERIC(18,6)    NOT NULL,
                    DstWarehouse NVARCHAR(15)     NULL,
                    DstLocation  NVARCHAR(15)     NULL,
                    CreatedAt    DATETIME2        NOT NULL DEFAULT GETDATE()
                );
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WMS_Cart_Operator')
                    CREATE INDEX IX_WMS_Cart_Operator ON dbo.WMS_Cart(OperatorCode);

                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WMS_InvSession')
                CREATE TABLE dbo.WMS_InvSession (
                    Id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_WMS_InvSession PRIMARY KEY,
                    OperatorCode  NVARCHAR(15)     NOT NULL,
                    WarehouseCode NVARCHAR(15)     NOT NULL,
                    WarehouseDesc NVARCHAR(100)    NOT NULL DEFAULT '',
                    Zone          NVARCHAR(50)     NOT NULL DEFAULT '',
                    StartedAt     DATETIME2        NOT NULL DEFAULT GETDATE(),
                    ClosedAt      DATETIME2        NULL
                );

                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WMS_InvCount')
                CREATE TABLE dbo.WMS_InvCount (
                    Id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_WMS_InvCount PRIMARY KEY,
                    SessionId     UNIQUEIDENTIFIER NOT NULL,
                    WarehouseCode NVARCHAR(15)     NOT NULL,
                    ArticleCode   NVARCHAR(20)     NOT NULL,
                    ArticleDesc   NVARCHAR(200)    NOT NULL DEFAULT '',
                    UoM           NVARCHAR(10)     NOT NULL DEFAULT '',
                    LocationCode  NVARCHAR(15)     NOT NULL,
                    LocationDesc  NVARCHAR(100)    NOT NULL DEFAULT '',
                    ExpectedQty   NUMERIC(18,6)    NOT NULL DEFAULT 0,
                    CountedQty    NUMERIC(18,6)    NULL,
                    CountedAt     DATETIME2        NULL,
                    Applied       BIT              NOT NULL DEFAULT 0
                );
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WMS_InvCount_Session')
                    CREATE INDEX IX_WMS_InvCount_Session ON dbo.WMS_InvCount(SessionId);

                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WMS_PrintTemplate')
                CREATE TABLE dbo.WMS_PrintTemplate (
                    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WMS_PrintTemplate PRIMARY KEY,
                    Context     NVARCHAR(50)  NOT NULL,
                    Name        NVARCHAR(100) NOT NULL,
                    ReportName  NVARCHAR(200) NOT NULL,
                    PrinterName NVARCHAR(100) NULL,
                    IsActive    BIT           NOT NULL DEFAULT 1
                );

                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WMS_PrintTemplateParam')
                CREATE TABLE dbo.WMS_PrintTemplateParam (
                    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WMS_PrintTemplateParam PRIMARY KEY,
                    TemplateId  INT           NOT NULL,
                    ParamName   NVARCHAR(50)  NOT NULL,
                    AutoFillKey NVARCHAR(50)  NULL,
                    Label       NVARCHAR(100) NOT NULL,
                    IsRequired  BIT           NOT NULL DEFAULT 0,
                    SortOrder   INT           NOT NULL DEFAULT 0,
                    CONSTRAINT FK_WMS_PrintTemplateParam_Tpl
                        FOREIGN KEY (TemplateId) REFERENCES dbo.WMS_PrintTemplate(Id) ON DELETE CASCADE
                );
                """);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.EnsureSchemaAsync fallito");
            throw;
        }
    }

    // ─── Carrello ─────────────────────────────────────────────────────────────

    /// <summary>Carica tutte le righe carrello non ancora evase per l'operatore.</summary>
    public async Task<List<CartRow>> LoadCartAsync(string operatorCode)
    {
        try
        {
            using var db = Open();
            var rows = await db.QueryAsync<CartDbRow>(
                @"SELECT Id, ArticleCode, ArticleDesc, UoM,
                         SrcWarehouse, SrcLocation, Qty,
                         DstWarehouse, DstLocation
                  FROM dbo.WMS_Cart
                  WHERE OperatorCode = @Op
                  ORDER BY CreatedAt",
                new { Op = operatorCode });

            return rows.Select(r => new CartRow
            {
                Id              = r.Id,
                ArticleCode     = r.ArticleCode,
                ArticleDesc     = r.ArticleDesc,
                UoM             = r.UoM,
                SourceWarehouse = r.SrcWarehouse,
                SourceLocation  = r.SrcLocation,
                Quantity        = r.Qty,
                DestWarehouse   = r.DstWarehouse,
                DestLocation    = r.DstLocation
            }).ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.LoadCartAsync fallito per {Op}", operatorCode);
            throw;
        }
    }

    /// <summary>Inserisce una nuova riga nel carrello persistente.</summary>
    public async Task AddRowAsync(string operatorCode, CartRow row)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                @"INSERT INTO dbo.WMS_Cart
                      (Id, OperatorCode, ArticleCode, ArticleDesc, UoM, SrcWarehouse, SrcLocation, Qty)
                  VALUES
                      (@Id, @Op, @ArticleCode, @ArticleDesc, @UoM, @SrcWh, @SrcLoc, @Qty)",
                new
                {
                    row.Id, Op = operatorCode,
                    row.ArticleCode, row.ArticleDesc, row.UoM,
                    SrcWh  = row.SourceWarehouse,
                    SrcLoc = row.SourceLocation,
                    Qty    = row.Quantity
                });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.AddRowAsync fallito per {ArticleCode}", row.ArticleCode);
            throw;
        }
    }

    /// <summary>Aggiorna la destinazione di una riga (fase evasione).</summary>
    public async Task AssignDestinationAsync(Guid id, string dstWarehouse, string dstLocation)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                @"UPDATE dbo.WMS_Cart
                  SET DstWarehouse = @DstWh, DstLocation = @DstLoc
                  WHERE Id = @Id",
                new { DstWh = dstWarehouse, DstLoc = dstLocation, Id = id });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.AssignDestinationAsync fallito per {Id}", id);
            throw;
        }
    }

    /// <summary>Rimuove una singola riga (movimento eseguito o cancellazione manuale).</summary>
    public async Task RemoveRowAsync(Guid id)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync("DELETE FROM dbo.WMS_Cart WHERE Id = @Id", new { Id = id });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.RemoveRowAsync fallito per {Id}", id);
            throw;
        }
    }

    /// <summary>Svuota l'intero carrello dell'operatore.</summary>
    public async Task ClearCartAsync(string operatorCode)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                "DELETE FROM dbo.WMS_Cart WHERE OperatorCode = @Op",
                new { Op = operatorCode });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.ClearCartAsync fallito per {Op}", operatorCode);
            throw;
        }
    }

    // ─── Inventario ───────────────────────────────────────────────────────────

    /// <summary>Cerca una sessione inventario aperta per l'operatore.</summary>
    public async Task<InventorySession?> GetOpenInventorySessionAsync(string operatorCode)
    {
        try
        {
            using var db = Open();
            var sr = await db.QueryFirstOrDefaultAsync<InvSessionRow>(
                @"SELECT TOP 1 Id, OperatorCode, WarehouseCode, WarehouseDesc, Zone, StartedAt
                  FROM dbo.WMS_InvSession
                  WHERE OperatorCode = @Op AND ClosedAt IS NULL
                  ORDER BY StartedAt DESC",
                new { Op = operatorCode });
            if (sr is null) return null;

            var counts = (await db.QueryAsync<InvCountRow>(
                @"SELECT Id, WarehouseCode, ArticleCode, ArticleDesc, UoM,
                         LocationCode, LocationDesc, ExpectedQty, CountedQty, Applied
                  FROM dbo.WMS_InvCount
                  WHERE SessionId = @SessId
                  ORDER BY LocationCode, ArticleCode",
                new { SessId = sr.Id })).ToList();

            return new InventorySession
            {
                Id            = sr.Id,
                OperatorCode  = sr.OperatorCode,
                WarehouseCode = sr.WarehouseCode,
                WarehouseDesc = sr.WarehouseDesc,
                Zone          = sr.Zone,
                StartedAt     = sr.StartedAt,
                Counts        = counts.Select(c => new InventoryCount
                {
                    Id           = c.Id,
                    WarehouseCode = c.WarehouseCode,
                    ArticleCode  = c.ArticleCode,
                    ArticleDesc  = c.ArticleDesc,
                    UoM          = c.UoM,
                    LocationCode = c.LocationCode,
                    LocationDesc = c.LocationDesc,
                    ExpectedQty  = c.ExpectedQty,
                    CountedQty   = c.CountedQty,
                    Applied      = c.Applied
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.GetOpenInventorySessionAsync fallito per {Op}", operatorCode);
            throw;
        }
    }

    /// <summary>Crea una nuova sessione inventario e inserisce tutte le righe.</summary>
    public async Task CreateInventorySessionAsync(InventorySession session)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                @"INSERT INTO dbo.WMS_InvSession(Id, OperatorCode, WarehouseCode, WarehouseDesc, Zone)
                  VALUES(@Id, @Op, @WhCode, @WhDesc, @Zone)",
                new { session.Id, Op = session.OperatorCode, WhCode = session.WarehouseCode,
                      WhDesc = session.WarehouseDesc, Zone = session.Zone });

            foreach (var c in session.Counts)
            {
                await db.ExecuteAsync(
                    @"INSERT INTO dbo.WMS_InvCount
                          (Id, SessionId, WarehouseCode, ArticleCode, ArticleDesc, UoM,
                           LocationCode, LocationDesc, ExpectedQty)
                      VALUES
                          (@Id, @SessId, @Wh, @Art, @Desc, @Uom, @Lc, @LcDesc, @Exp)",
                    new { c.Id, SessId = session.Id, Wh = c.WarehouseCode,
                          Art = c.ArticleCode, Desc = c.ArticleDesc, Uom = c.UoM,
                          Lc = c.LocationCode, LcDesc = c.LocationDesc, Exp = c.ExpectedQty });
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.CreateInventorySessionAsync fallito");
            throw;
        }
    }

    /// <summary>Aggiunge una singola riga extra a una sessione già esistente (articolo fuori zona).</summary>
    public async Task AddInventoryCountAsync(Guid sessionId, InventoryCount count)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                @"INSERT INTO dbo.WMS_InvCount
                      (Id, SessionId, WarehouseCode, ArticleCode, ArticleDesc, UoM,
                       LocationCode, LocationDesc, ExpectedQty, CountedQty, CountedAt)
                  VALUES
                      (@Id, @SessId, @Wh, @Art, @Desc, @Uom, @Lc, @LcDesc, @Exp,
                       @CntQty, CASE WHEN @CntQty IS NULL THEN NULL ELSE GETDATE() END)",
                new { count.Id, SessId = sessionId, Wh = count.WarehouseCode,
                      Art = count.ArticleCode, Desc = count.ArticleDesc, Uom = count.UoM,
                      Lc = count.LocationCode, LcDesc = count.LocationDesc,
                      Exp = count.ExpectedQty, CntQty = count.CountedQty });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.AddInventoryCountAsync fallito per {Art}", count.ArticleCode);
            throw;
        }
    }

    /// <summary>Salva il conteggio di una riga.</summary>
    public async Task UpdateCountAsync(Guid id, decimal countedQty)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                "UPDATE dbo.WMS_InvCount SET CountedQty = @Qty, CountedAt = GETDATE() WHERE Id = @Id",
                new { Qty = countedQty, Id = id });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.UpdateCountAsync fallito per {Id}", id);
            throw;
        }
    }

    /// <summary>Marca una riga come applicata (rettifica TRD_InsertMov già eseguita).</summary>
    public async Task MarkCountAppliedAsync(Guid id)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync("UPDATE dbo.WMS_InvCount SET Applied = 1 WHERE Id = @Id", new { Id = id });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.MarkCountAppliedAsync fallito per {Id}", id);
            throw;
        }
    }

    /// <summary>Chiude la sessione inventario.</summary>
    public async Task CloseInventorySessionAsync(Guid sessionId)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                "UPDATE dbo.WMS_InvSession SET ClosedAt = GETDATE() WHERE Id = @Id",
                new { Id = sessionId });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.CloseInventorySessionAsync fallito per {Id}", sessionId);
            throw;
        }
    }

    /// <summary>Elimina una sessione aperta senza applicare rettifiche.</summary>
    public async Task AbandonInventorySessionAsync(Guid sessionId)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync("DELETE FROM dbo.WMS_InvCount WHERE SessionId = @Id", new { Id = sessionId });
            await db.ExecuteAsync("DELETE FROM dbo.WMS_InvSession WHERE Id = @Id",      new { Id = sessionId });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.AbandonInventorySessionAsync fallito per {Id}", sessionId);
            throw;
        }
    }

    // ─── Template di stampa ───────────────────────────────────────────────────

    /// <summary>Restituisce tutti i template (tutti i contesti, inclusi inattivi). Per la pagina di configurazione.</summary>
    public async Task<List<PrintTemplate>> GetAllPrintTemplatesAsync()
    {
        try
        {
            using var db = Open();
            var templates = (await db.QueryAsync<PrintTemplate>(
                "SELECT Id, Context, Name, ReportName, PrinterName, IsActive FROM dbo.WMS_PrintTemplate ORDER BY Context, Name")).ToList();

            if (templates.Count > 0)
            {
                var ids = templates.Select(t => t.Id).ToList();
                var parms = (await db.QueryAsync<PrintTemplateParam>(
                    "SELECT Id, TemplateId, ParamName, AutoFillKey, Label, IsRequired, SortOrder FROM dbo.WMS_PrintTemplateParam WHERE TemplateId IN @Ids ORDER BY SortOrder",
                    new { Ids = ids })).ToList();
                foreach (var t in templates)
                    t.Params = parms.Where(p => p.TemplateId == t.Id).ToList();
            }
            return templates;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.GetAllPrintTemplatesAsync fallito");
            throw;
        }
    }

    /// <summary>Restituisce i template attivi per il contesto specificato, con i relativi parametri.</summary>
    public async Task<List<PrintTemplate>> GetPrintTemplatesAsync(string context)
    {
        try
        {
            using var db = Open();
            var templates = (await db.QueryAsync<PrintTemplate>(
                "SELECT Id, Context, Name, ReportName, PrinterName, IsActive FROM dbo.WMS_PrintTemplate WHERE Context = @Ctx AND IsActive = 1",
                new { Ctx = context })).ToList();

            if (templates.Count > 0)
            {
                var ids = templates.Select(t => t.Id).ToList();
                var parms = (await db.QueryAsync<PrintTemplateParam>(
                    "SELECT Id, TemplateId, ParamName, AutoFillKey, Label, IsRequired, SortOrder FROM dbo.WMS_PrintTemplateParam WHERE TemplateId IN @Ids ORDER BY SortOrder",
                    new { Ids = ids })).ToList();

                foreach (var t in templates)
                    t.Params = parms.Where(p => p.TemplateId == t.Id).ToList();
            }

            return templates;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.GetPrintTemplatesAsync fallito per {Ctx}", context);
            throw;
        }
    }

    /// <summary>Inserisce o aggiorna un template (Id=0 → INSERT, Id>0 → UPDATE).</summary>
    public async Task<int> UpsertPrintTemplateAsync(PrintTemplate t)
    {
        try
        {
            using var db = Open();
            if (t.Id == 0)
            {
                return await db.ExecuteScalarAsync<int>(
                    @"INSERT INTO dbo.WMS_PrintTemplate(Context, Name, ReportName, PrinterName, IsActive)
                      VALUES (@Context, @Name, @ReportName, @PrinterName, @IsActive);
                      SELECT CAST(SCOPE_IDENTITY() AS INT);", t);
            }
            else
            {
                await db.ExecuteAsync(
                    "UPDATE dbo.WMS_PrintTemplate SET Context=@Context, Name=@Name, ReportName=@ReportName, PrinterName=@PrinterName, IsActive=@IsActive WHERE Id=@Id", t);
                return t.Id;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.UpsertPrintTemplateAsync fallito");
            throw;
        }
    }

    /// <summary>Elimina un template (e in cascade i suoi parametri).</summary>
    public async Task DeletePrintTemplateAsync(int id)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync("DELETE FROM dbo.WMS_PrintTemplate WHERE Id = @Id", new { Id = id });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.DeletePrintTemplateAsync fallito per {Id}", id);
            throw;
        }
    }

    /// <summary>Sostituisce tutti i parametri di un template (delete + re-insert).</summary>
    public async Task ReplaceParamsAsync(int templateId, IEnumerable<PrintTemplateParam> parms)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync("DELETE FROM dbo.WMS_PrintTemplateParam WHERE TemplateId = @Id", new { Id = templateId });
            foreach (var p in parms.OrderBy(x => x.SortOrder))
            {
                await db.ExecuteAsync(
                    @"INSERT INTO dbo.WMS_PrintTemplateParam(TemplateId, ParamName, AutoFillKey, Label, IsRequired, SortOrder)
                      VALUES (@TemplateId, @ParamName, @AutoFillKey, @Label, @IsRequired, @SortOrder)",
                    new { TemplateId = templateId, p.ParamName, p.AutoFillKey, p.Label, p.IsRequired, p.SortOrder });
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.ReplaceParamsAsync fallito per template {Id}", templateId);
            throw;
        }
    }

    // ─── Row type Dapper ──────────────────────────────────────────────────────

    private class InvSessionRow
    {
        public Guid     Id            { get; set; }
        public string   OperatorCode  { get; set; } = "";
        public string   WarehouseCode { get; set; } = "";
        public string   WarehouseDesc { get; set; } = "";
        public string   Zone          { get; set; } = "";
        public DateTime StartedAt     { get; set; }
    }

    private class InvCountRow
    {
        public Guid     Id            { get; set; }
        public string   WarehouseCode { get; set; } = "";
        public string   ArticleCode   { get; set; } = "";
        public string   ArticleDesc   { get; set; } = "";
        public string   UoM           { get; set; } = "";
        public string   LocationCode  { get; set; } = "";
        public string   LocationDesc  { get; set; } = "";
        public decimal  ExpectedQty   { get; set; }
        public decimal? CountedQty    { get; set; }
        public bool     Applied       { get; set; }
    }

    private class CartDbRow
    {
        public Guid    Id           { get; set; }
        public string  ArticleCode  { get; set; } = "";
        public string  ArticleDesc  { get; set; } = "";
        public string  UoM          { get; set; } = "";
        public string  SrcWarehouse { get; set; } = "";
        public string  SrcLocation  { get; set; } = "";
        public decimal Qty          { get; set; }
        public string? DstWarehouse { get; set; }
        public string? DstLocation  { get; set; }
    }
}
