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

                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WMS_PickList')
                CREATE TABLE dbo.WMS_PickList (
                    Id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_WMS_PickList PRIMARY KEY,
                    Code             NVARCHAR(30)     NOT NULL,
                    Description      NVARCHAR(100)    NOT NULL DEFAULT '',
                    Status           NVARCHAR(20)     NOT NULL DEFAULT 'Open',
                    CreatedAt        DATETIME2        NOT NULL DEFAULT GETDATE(),
                    CreatedByOp      NVARCHAR(15)     NOT NULL DEFAULT '',
                    AssignedOperator NVARCHAR(15)     NULL,
                    ClosedAt         DATETIME2        NULL
                );
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.WMS_PickList') AND name='AssignedOperator')
                    ALTER TABLE dbo.WMS_PickList ADD AssignedOperator NVARCHAR(15) NULL;

                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WMS_PickListRow')
                CREATE TABLE dbo.WMS_PickListRow (
                    Id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_WMS_PickListRow PRIMARY KEY,
                    ListId           UNIQUEIDENTIFIER NOT NULL,
                    ArticleCode      NVARCHAR(20)     NOT NULL,
                    ArticleDesc      NVARCHAR(200)    NOT NULL DEFAULT '',
                    UoM              NVARCHAR(10)     NOT NULL DEFAULT '',
                    PlannedQty       NUMERIC(18,6)    NOT NULL DEFAULT 0,
                    OlCod            NVARCHAR(20)     NULL,
                    Handling         NVARCHAR(80)     NULL,
                    IdSpec           INT              NULL,
                    IdGroup          INT              NULL,
                    MainWarehouseCode NVARCHAR(15)    NOT NULL DEFAULT '',
                    MainLocationCode  NVARCHAR(15)    NOT NULL DEFAULT '',
                    IsMissing        BIT              NOT NULL DEFAULT 0,
                    IsExtraItem      BIT              NOT NULL DEFAULT 0,
                    RowStatus        NVARCHAR(20)     NOT NULL DEFAULT 'Pending',
                    SortOrder        INT              NOT NULL DEFAULT 0,
                    CONSTRAINT FK_WMS_PickListRow_List
                        FOREIGN KEY (ListId) REFERENCES dbo.WMS_PickList(Id) ON DELETE CASCADE
                );
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WMS_PickListRow_List')
                    CREATE INDEX IX_WMS_PickListRow_List ON dbo.WMS_PickListRow(ListId);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WMS_PickListRow_Article')
                    CREATE INDEX IX_WMS_PickListRow_Article ON dbo.WMS_PickListRow(ArticleCode);

                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WMS_PickListPick')
                CREATE TABLE dbo.WMS_PickListPick (
                    Id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_WMS_PickListPick PRIMARY KEY,
                    RowId         UNIQUEIDENTIFIER NOT NULL,
                    PickedQty     NUMERIC(18,6)    NOT NULL DEFAULT 0,
                    WarehouseCode NVARCHAR(15)     NOT NULL DEFAULT '',
                    LocationCode  NVARCHAR(15)     NOT NULL,
                    OperatorCode  NVARCHAR(15)     NOT NULL DEFAULT '',
                    StagedAt      DATETIME2        NOT NULL DEFAULT GETDATE(),
                    ExecutedAt    DATETIME2        NULL,
                    ErpMovId      INT              NULL,
                    ErpSesId      INT              NULL,
                    CONSTRAINT FK_WMS_PickListPick_Row
                        FOREIGN KEY (RowId) REFERENCES dbo.WMS_PickListRow(Id) ON DELETE CASCADE
                );
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WMS_PickListPick_Row')
                    CREATE INDEX IX_WMS_PickListPick_Row ON dbo.WMS_PickListPick(RowId);
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('dbo.WMS_PickListPick') AND name='ErpSesId')
                    ALTER TABLE dbo.WMS_PickListPick ADD ErpSesId INT NULL;

                IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'WMS_AcceptanceLine')
                CREATE TABLE dbo.WMS_AcceptanceLine (
                    Id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_WMS_AcceptanceLine PRIMARY KEY,
                    ErpDocId      INT              NOT NULL,
                    ErpLineId     INT              NOT NULL,
                    ArticleCode   NVARCHAR(20)     NOT NULL,
                    WarehouseCode NVARCHAR(15)     NOT NULL DEFAULT '',
                    LocationCode  NVARCHAR(15)     NOT NULL,
                    AcceptedQty   NUMERIC(18,6)    NOT NULL DEFAULT 0,
                    ExpectedQty   NUMERIC(18,6)    NOT NULL DEFAULT 0,
                    OperatorCode  NVARCHAR(15)     NOT NULL DEFAULT '',
                    ErpMovId      INT              NULL,
                    AcceptedAt    DATETIME2        NOT NULL DEFAULT GETDATE(),
                    DocumentRef   NVARCHAR(40)     NOT NULL DEFAULT '',
                    Notes         NVARCHAR(200)    NULL
                );
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WMS_AcceptanceLine_Doc')
                    CREATE INDEX IX_WMS_AcceptanceLine_Doc ON dbo.WMS_AcceptanceLine(ErpDocId);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WMS_AcceptanceLine_Line')
                    CREATE INDEX IX_WMS_AcceptanceLine_Line ON dbo.WMS_AcceptanceLine(ErpDocId, ErpLineId);

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

    // ─── Liste di prelievo ────────────────────────────────────────────────────

    public async Task<List<PickListHeaderDto>> GetPickListsAsync()
    {
        try
        {
            using var db = Open();
            var rows = (await db.QueryAsync<PickListHeaderRow>(
                @"SELECT l.Id, l.Code, l.Description, l.Status, l.CreatedAt, l.CreatedByOp,
                         l.AssignedOperator,
                         COUNT(r.Id)                                          AS TotalRows,
                         SUM(CASE WHEN r.RowStatus = 'Completed' THEN 1 ELSE 0 END) AS CompletedRows,
                         SUM(CASE WHEN r.IsMissing  = 1          THEN 1 ELSE 0 END) AS MissingRows
                  FROM dbo.WMS_PickList l
                  LEFT JOIN dbo.WMS_PickListRow r ON r.ListId = l.Id
                  WHERE l.Status IN ('Open','InProgress')
                  GROUP BY l.Id, l.Code, l.Description, l.Status, l.CreatedAt, l.CreatedByOp, l.AssignedOperator
                  ORDER BY l.CreatedAt DESC")).ToList();

            if (rows.Count == 0) return [];

            var ids = rows.Select(r => r.Id).ToList();
            var olCods = (await db.QueryAsync<(Guid ListId, string OlCod)>(
                @"SELECT DISTINCT ListId, OlCod FROM dbo.WMS_PickListRow
                  WHERE ListId IN @Ids AND OlCod IS NOT NULL",
                new { Ids = ids })).ToList();

            return rows.Select(r => new PickListHeaderDto(
                r.Id, r.Code, r.Description, r.Status, r.CreatedAt, r.CreatedByOp,
                r.AssignedOperator,
                r.TotalRows, r.CompletedRows, r.MissingRows,
                olCods.Where(o => o.ListId == r.Id).Select(o => o.OlCod).ToList()
            )).ToList();
        }
        catch (Exception ex) { _log.LogError(ex, "GetPickListsAsync"); throw; }
    }

    public async Task<Guid> CreatePickListAsync(
        string code, string description, string opCode, string? assignedOperator = null)
    {
        try
        {
            var id = Guid.NewGuid();
            using var db = Open();
            await db.ExecuteAsync(
                @"INSERT INTO dbo.WMS_PickList(Id, Code, Description, Status, CreatedByOp, AssignedOperator)
                  VALUES (@Id, @Code, @Desc, 'Open', @Op, @Assigned)",
                new { Id = id, Code = code, Desc = description, Op = opCode, Assigned = assignedOperator });
            return id;
        }
        catch (Exception ex) { _log.LogError(ex, "CreatePickListAsync"); throw; }
    }

    public async Task AddPickListRowAsync(Guid listId, PickListRowDto row)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                @"INSERT INTO dbo.WMS_PickListRow
                      (Id, ListId, ArticleCode, ArticleDesc, UoM, PlannedQty,
                       OlCod, Handling, IdSpec, IdGroup,
                       MainWarehouseCode, MainLocationCode,
                       IsMissing, IsExtraItem, RowStatus, SortOrder)
                  VALUES
                      (@Id, @ListId, @ArticleCode, @ArticleDesc, @UoM, @PlannedQty,
                       @OlCod, @Handling, @IdSpec, @IdGroup,
                       @MainWh, @MainLc,
                       0, @IsExtra, 'Pending', @SortOrder)",
                new
                {
                    row.Id, ListId = listId,
                    row.ArticleCode, row.ArticleDesc, row.UoM, row.PlannedQty,
                    row.OlCod, row.Handling, row.IdSpec, row.IdGroup,
                    MainWh = row.MainWarehouseCode, MainLc = row.MainLocationCode,
                    IsExtra = row.IsExtraItem, row.SortOrder
                });
        }
        catch (Exception ex) { _log.LogError(ex, "AddPickListRowAsync {Art}", row.ArticleCode); throw; }
    }

    public async Task<List<PickListRowDto>> GetPickListRowsAsync(Guid listId)
    {
        try
        {
            using var db = Open();
            var rows = (await db.QueryAsync<PickListRowDbRow>(
                @"SELECT Id, ListId, ArticleCode, ArticleDesc, UoM, PlannedQty,
                         OlCod, Handling, IdSpec, IdGroup,
                         MainWarehouseCode, MainLocationCode,
                         IsMissing, IsExtraItem, RowStatus, SortOrder
                  FROM dbo.WMS_PickListRow
                  WHERE ListId = @ListId
                  ORDER BY SortOrder, ArticleCode",
                new { ListId = listId })).ToList();

            if (rows.Count == 0) return [];

            var rowIds = rows.Select(r => r.Id).ToList();
            var picks = (await db.QueryAsync<PickDbRow>(
                @"SELECT Id, RowId, PickedQty, WarehouseCode, LocationCode,
                         OperatorCode, StagedAt, ExecutedAt, ErpMovId, ErpSesId
                  FROM dbo.WMS_PickListPick
                  WHERE RowId IN @Ids
                  ORDER BY StagedAt",
                new { Ids = rowIds })).ToList();

            return rows.Select(r =>
            {
                var dto = new PickListRowDto
                {
                    Id = r.Id, ListId = r.ListId,
                    ArticleCode = r.ArticleCode, ArticleDesc = r.ArticleDesc, UoM = r.UoM,
                    PlannedQty = r.PlannedQty, OlCod = r.OlCod,
                    Handling = r.Handling ?? "", IdSpec = r.IdSpec, IdGroup = r.IdGroup,
                    MainWarehouseCode = r.MainWarehouseCode ?? "",
                    MainLocationCode  = r.MainLocationCode  ?? "",
                    IsMissing   = r.IsMissing,
                    IsExtraItem = r.IsExtraItem,
                    SortOrder   = r.SortOrder,
                    Picks = picks.Where(p => p.RowId == r.Id)
                                 .Select(p => new StagedPickDto
                                 {
                                     Id = p.Id, RowId = p.RowId,
                                     PickedQty    = p.PickedQty,
                                     WarehouseCode = p.WarehouseCode ?? "",
                                     LocationCode  = p.LocationCode,
                                     OperatorCode  = p.OperatorCode ?? "",
                                     StagedAt  = p.StagedAt,
                                     ExecutedAt = p.ExecutedAt,
                                     ErpMovId  = p.ErpMovId,
                                     ErpSesId  = p.ErpSesId
                                 }).ToList()
                };
                return dto;
            }).ToList();
        }
        catch (Exception ex) { _log.LogError(ex, "GetPickListRowsAsync {ListId}", listId); throw; }
    }

    public async Task StagePickAsync(StagedPickDto pick)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                @"INSERT INTO dbo.WMS_PickListPick
                      (Id, RowId, PickedQty, WarehouseCode, LocationCode, OperatorCode)
                  VALUES
                      (@Id, @RowId, @PickedQty, @Wh, @Lc, @Op)",
                new { pick.Id, pick.RowId, pick.PickedQty,
                      Wh = pick.WarehouseCode, Lc = pick.LocationCode, Op = pick.OperatorCode });

            await UpdateListStatus(db, pick.RowId);
        }
        catch (Exception ex) { _log.LogError(ex, "StagePickAsync {RowId}", pick.RowId); throw; }
    }

    public async Task RemoveStagedPickAsync(Guid pickId)
    {
        try
        {
            using var db = Open();
            var rowId = await db.ExecuteScalarAsync<Guid?>(
                "SELECT RowId FROM dbo.WMS_PickListPick WHERE Id=@Id AND ExecutedAt IS NULL",
                new { Id = pickId });
            if (rowId is null) return;

            await db.ExecuteAsync(
                "DELETE FROM dbo.WMS_PickListPick WHERE Id=@Id AND ExecutedAt IS NULL",
                new { Id = pickId });
            await UpdateListStatus(db, rowId.Value);
        }
        catch (Exception ex) { _log.LogError(ex, "RemoveStagedPickAsync {Id}", pickId); throw; }
    }

    public async Task CancelStagedPicksForListAsync(Guid listId)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                @"DELETE p FROM dbo.WMS_PickListPick p
                  JOIN dbo.WMS_PickListRow r ON r.Id = p.RowId
                  WHERE r.ListId = @ListId AND p.ExecutedAt IS NULL",
                new { ListId = listId });
            var rowIds = (await db.QueryAsync<Guid>(
                "SELECT Id FROM dbo.WMS_PickListRow WHERE ListId=@ListId", new { ListId = listId })).ToList();
            foreach (var rid in rowIds)
                await UpdateListStatus(db, rid);
        }
        catch (Exception ex) { _log.LogError(ex, "CancelStagedPicksForListAsync {ListId}", listId); throw; }
    }

    public async Task SetRowMissingAsync(Guid rowId, bool missing)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                "UPDATE dbo.WMS_PickListRow SET IsMissing=@M, RowStatus=@S WHERE Id=@Id",
                new { M = missing, S = missing ? "Missing" : "Pending", Id = rowId });
        }
        catch (Exception ex) { _log.LogError(ex, "SetRowMissingAsync {Id}", rowId); throw; }
    }

    public async Task<List<(StagedPickDto Pick, string ArticleCode, string ArticleDesc,
                             string UoM, string? OlCod)>>
        GetPendingPicksForListAsync(Guid listId)
    {
        try
        {
            using var db = Open();
            var rows = (await db.QueryAsync<PendingPickRow>(
                @"SELECT p.Id, p.RowId, p.PickedQty, p.WarehouseCode, p.LocationCode,
                         p.OperatorCode, p.StagedAt,
                         r.ArticleCode, r.ArticleDesc, r.UoM, r.OlCod
                  FROM dbo.WMS_PickListPick p
                  JOIN dbo.WMS_PickListRow  r ON r.Id = p.RowId
                  WHERE r.ListId = @ListId AND p.ExecutedAt IS NULL
                  ORDER BY r.OlCod, r.ArticleCode",
                new { ListId = listId })).ToList();

            return rows.Select(r => (
                new StagedPickDto
                {
                    Id = r.Id, RowId = r.RowId, PickedQty = r.PickedQty,
                    WarehouseCode = r.WarehouseCode ?? "", LocationCode = r.LocationCode,
                    OperatorCode = r.OperatorCode ?? "", StagedAt = r.StagedAt
                },
                r.ArticleCode, r.ArticleDesc, r.UoM, r.OlCod
            )).ToList();
        }
        catch (Exception ex) { _log.LogError(ex, "GetPendingPicksForListAsync {ListId}", listId); throw; }
    }

    public async Task MarkPickExecutedAsync(Guid pickId, int erpMovId, int? erpSesId = null)
    {
        try
        {
            using var db = Open();
            var rowId = await db.ExecuteScalarAsync<Guid>(
                @"UPDATE dbo.WMS_PickListPick
                  SET ExecutedAt=GETDATE(), ErpMovId=@Mov, ErpSesId=@Ses
                  OUTPUT INSERTED.RowId
                  WHERE Id=@Id",
                new { Mov = erpMovId, Ses = erpSesId, Id = pickId });
            await UpdateListStatus(db, rowId);
        }
        catch (Exception ex) { _log.LogError(ex, "MarkPickExecutedAsync {Id}", pickId); throw; }
    }

    public async Task ClosePickListAsync(Guid listId)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                "UPDATE dbo.WMS_PickList SET Status='Closed', ClosedAt=GETDATE() WHERE Id=@Id",
                new { Id = listId });
        }
        catch (Exception ex) { _log.LogError(ex, "ClosePickListAsync {Id}", listId); throw; }
    }

    public async Task UpdatePickListStatusAsync(Guid listId)
    {
        try
        {
            using var db = Open();
            var counts = await db.QueryFirstOrDefaultAsync<(int Total, int Done, int Missing)>(
                @"SELECT COUNT(*) Total,
                         SUM(CASE WHEN RowStatus IN ('Completed','Missing') THEN 1 ELSE 0 END) Done,
                         SUM(CASE WHEN IsMissing=1 THEN 1 ELSE 0 END) Missing
                  FROM dbo.WMS_PickListRow WHERE ListId=@Id",
                new { Id = listId });
            var newStatus = counts == default ? "Open"
                          : counts.Total == 0  ? "Open"
                          : counts.Done == counts.Total ? "Completed"
                          : "InProgress";
            await db.ExecuteAsync(
                @"UPDATE dbo.WMS_PickList SET Status=@S,
                    ClosedAt=CASE WHEN @S='Completed' THEN GETDATE() ELSE NULL END
                  WHERE Id=@Id",
                new { S = newStatus, Id = listId });
        }
        catch (Exception ex) { _log.LogError(ex, "UpdatePickListStatusAsync {Id}", listId); throw; }
    }

    private async Task UpdateListStatus(SqlConnection db, Guid rowId)
    {
        var info = await db.QueryFirstOrDefaultAsync<(decimal Planned, decimal TotalPicked, bool Missing)>(
            @"SELECT r.PlannedQty Planned,
                     ISNULL(SUM(p.PickedQty),0) TotalPicked,
                     r.IsMissing Missing
              FROM dbo.WMS_PickListRow r
              LEFT JOIN dbo.WMS_PickListPick p ON p.RowId=r.Id
              WHERE r.Id=@Id GROUP BY r.PlannedQty, r.IsMissing",
            new { Id = rowId });
        if (info == default) return;
        var rowStatus = info.Missing ? "Missing"
                      : info.TotalPicked >= info.Planned ? "Completed"
                      : info.TotalPicked > 0 ? "Partial"
                      : "Pending";
        await db.ExecuteAsync(
            "UPDATE dbo.WMS_PickListRow SET RowStatus=@S WHERE Id=@Id",
            new { S = rowStatus, Id = rowId });

        var listId = await db.ExecuteScalarAsync<Guid>(
            "SELECT ListId FROM dbo.WMS_PickListRow WHERE Id=@Id", new { Id = rowId });
        await UpdatePickListStatusAsync(listId);
    }

    // ─── Accettazione merce ───────────────────────────────────────────────────

    /// <summary>Inserisce un versamento di accettazione su WMS_AcceptanceLine.</summary>
    public async Task InsertAcceptanceLineAsync(WmsAcceptanceLine line)
    {
        try
        {
            using var db = Open();
            await db.ExecuteAsync(
                @"INSERT INTO dbo.WMS_AcceptanceLine
                      (Id, ErpDocId, ErpLineId, ArticleCode, WarehouseCode, LocationCode,
                       AcceptedQty, ExpectedQty, OperatorCode, ErpMovId, AcceptedAt, DocumentRef, Notes)
                  VALUES
                      (@Id, @ErpDocId, @ErpLineId, @ArticleCode, @WarehouseCode, @LocationCode,
                       @AcceptedQty, @ExpectedQty, @OperatorCode, @ErpMovId, @AcceptedAt, @DocumentRef, @Notes)",
                new
                {
                    line.Id, line.ErpDocId, line.ErpLineId,
                    line.ArticleCode, line.WarehouseCode, line.LocationCode,
                    line.AcceptedQty, line.ExpectedQty, line.OperatorCode,
                    line.ErpMovId,
                    AcceptedAt = line.AcceptedAt == default ? DateTime.Now : line.AcceptedAt,
                    line.DocumentRef, line.Notes
                });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.InsertAcceptanceLineAsync ErpDoc={DocId} Line={LineId}", line.ErpDocId, line.ErpLineId);
            throw;
        }
    }

    /// <summary>Carica tutti i versamenti per un documento ERP (per popolare storico e avanzamento).</summary>
    public async Task<List<WmsAcceptanceLine>> GetAcceptanceLinesForDocAsync(int erpDocId)
    {
        try
        {
            using var db = Open();
            return (await db.QueryAsync<WmsAcceptanceLine>(
                @"SELECT Id, ErpDocId, ErpLineId, ArticleCode, WarehouseCode, LocationCode,
                         AcceptedQty, ExpectedQty, OperatorCode, ErpMovId, AcceptedAt, DocumentRef, Notes
                  FROM dbo.WMS_AcceptanceLine
                  WHERE ErpDocId = @DocId
                  ORDER BY AcceptedAt",
                new { DocId = erpDocId })).ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.GetAcceptanceLinesForDocAsync ErpDoc={DocId}", erpDocId);
            throw;
        }
    }

    /// <summary>Carica versamenti per più documenti in una sola query (usata nella list view).</summary>
    public async Task<List<WmsAcceptanceLine>> GetAcceptanceLinesForDocsAsync(IEnumerable<int> erpDocIds)
    {
        try
        {
            var ids = erpDocIds.ToList();
            if (ids.Count == 0) return [];
            using var db = Open();
            return (await db.QueryAsync<WmsAcceptanceLine>(
                @"SELECT Id, ErpDocId, ErpLineId, ArticleCode, WarehouseCode, LocationCode,
                         AcceptedQty, ExpectedQty, OperatorCode, ErpMovId, AcceptedAt, DocumentRef, Notes
                  FROM dbo.WMS_AcceptanceLine
                  WHERE ErpDocId IN @Ids
                  ORDER BY ErpDocId, ErpLineId, AcceptedAt",
                new { Ids = ids })).ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LogicService.GetAcceptanceLinesForDocsAsync");
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

    private class PickListHeaderRow
    {
        public Guid     Id               { get; set; }
        public string   Code             { get; set; } = "";
        public string   Description      { get; set; } = "";
        public string   Status           { get; set; } = "";
        public DateTime CreatedAt        { get; set; }
        public string   CreatedByOp      { get; set; } = "";
        public string?  AssignedOperator { get; set; }
        public int      TotalRows        { get; set; }
        public int      CompletedRows    { get; set; }
        public int      MissingRows      { get; set; }
    }

    private class PickListRowDbRow
    {
        public Guid     Id               { get; set; }
        public Guid     ListId           { get; set; }
        public string   ArticleCode      { get; set; } = "";
        public string   ArticleDesc      { get; set; } = "";
        public string   UoM              { get; set; } = "";
        public decimal  PlannedQty       { get; set; }
        public string?  OlCod            { get; set; }
        public string?  Handling         { get; set; }
        public int?     IdSpec           { get; set; }
        public int?     IdGroup          { get; set; }
        public string?  MainWarehouseCode { get; set; }
        public string?  MainLocationCode  { get; set; }
        public bool     IsMissing        { get; set; }
        public bool     IsExtraItem      { get; set; }
        public string   RowStatus        { get; set; } = "";
        public int      SortOrder        { get; set; }
    }

    private class PickDbRow
    {
        public Guid      Id            { get; set; }
        public Guid      RowId         { get; set; }
        public decimal   PickedQty     { get; set; }
        public string?   WarehouseCode { get; set; }
        public string    LocationCode  { get; set; } = "";
        public string?   OperatorCode  { get; set; }
        public DateTime  StagedAt      { get; set; }
        public DateTime? ExecutedAt    { get; set; }
        public int?      ErpMovId      { get; set; }
        public int?      ErpSesId      { get; set; }
    }

    private class PendingPickRow
    {
        public Guid     Id            { get; set; }
        public Guid     RowId         { get; set; }
        public decimal  PickedQty     { get; set; }
        public string?  WarehouseCode { get; set; }
        public string   LocationCode  { get; set; } = "";
        public string?  OperatorCode  { get; set; }
        public DateTime StagedAt      { get; set; }
        public string   ArticleCode   { get; set; } = "";
        public string   ArticleDesc   { get; set; } = "";
        public string   UoM           { get; set; } = "";
        public string?  OlCod         { get; set; }
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
