-- ============================================================
-- V002 — Stored Procedure per prelievo produzione su FactoryMecmar
--
-- Flusso batch (da usare):
--   1. WMS_OpenPickSession  → apre S_SES, ritorna @IDSES
--   2. WMS_InsertPickLine   → S_PAP + S_SPP + TRD_InsertMov SCAR (ripetere per ogni articolo)
--   3. WMS_ClosePickSession → chiude S_SES (SESTO=2)
--
-- WMS_InsertPick è mantenuta per compatibilità (sessione per-riga).
-- I progressivi usano GetProgressivo (standard Intesi) — NON usare MAX().
-- ============================================================

-- ─── SP 1: apre sessione operatore ───────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.WMS_OpenPickSession
    @OLCOD  VARCHAR(10),
    @OPCOD  VARCHAR(15),
    @NOCOD  SMALLINT,
    @IDSES  INT OUTPUT,
    @ErrMsg VARCHAR(255) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @IDSES  = -1;
    SET @ErrMsg = '';
    BEGIN TRY
        EXEC @IDSES = dbo.GetProgressivo 'dbo.S_SES';
        INSERT INTO dbo.S_SES
            (IDSES, SESIE, NOCOD, OLCOD, OPCOD, SETYP, SESTO, TCCOD,
             SETTE, SETTL, SETTA, SETFG, SETFI, SEFOP, SENOP, RICUN)
        VALUES
            (@IDSES, SYSDATETIME(), @NOCOD, @OLCOD, @OPCOD, 'O', 1, 1,
             0, 0, 0, 0, 0, 0, 0, 0);
    END TRY
    BEGIN CATCH
        SET @IDSES  = -1;
        SET @ErrMsg = ERROR_MESSAGE();
    END CATCH
END
GO

-- ─── SP 2: registra una riga di prelievo su sessione esistente ────────────────
CREATE OR ALTER PROCEDURE dbo.WMS_InsertPickLine
    @OLCOD  VARCHAR(10),
    @PACOD  VARCHAR(20),
    @PADSC  VARCHAR(160),
    @PAUDM  VARCHAR(10),
    @PAQTP  NUMERIC(18,6),
    @OPCOD  VARCHAR(15),
    @NOCOD  SMALLINT,
    @IDSES  INT,           -- IDSES aperto con WMS_OpenPickSession
    @MGCOD  VARCHAR(15)  = '',  -- '' → locazione principale articolo
    @LCCOD  VARCHAR(15)  = '',  -- '' → locazione principale articolo
    @IDMOV  INT OUTPUT,
    @ErrMsg VARCHAR(255) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @IDMOV  = -1;
    SET @ErrMsg = '';
    BEGIN TRY
        BEGIN TRANSACTION;

        -- S_PAP: cerca o crea con progressivo esplicito
        DECLARE @IDPAP INT;
        SELECT @IDPAP = IDPAP FROM dbo.S_PAP WHERE OLCOD = @OLCOD AND PACOD = @PACOD;
        IF @IDPAP IS NULL
        BEGIN
            EXEC @IDPAP = dbo.GetProgressivo 'dbo.S_PAP';
            INSERT INTO dbo.S_PAP (OLCOD, PACOD, PADSC, PAQTP, PAUDM, PAQTR, IDPAP)
            VALUES (@OLCOD, @PACOD, @PADSC, 0, @PAUDM, 0, @IDPAP);
        END

        -- S_SPP: inserisci con progressivo esplicito, legato alla sessione aperta
        DECLARE @IDSPP INT;
        DECLARE @SESIE DATETIME2 = (SELECT SESIE FROM dbo.S_SES WHERE IDSES = @IDSES);
        EXEC @IDSPP = dbo.GetProgressivo 'dbo.S_SPP';
        INSERT INTO dbo.S_SPP
            (IDSPP, SESIE, NOCOD, PACOD, PAQTP, IDSES, IDPAP, PPSTP, TPREC, PAQTR)
        VALUES
            (@IDSPP, @SESIE, @NOCOD, @PACOD, @PAQTP, @IDSES, @IDPAP, @SESIE, 1, 0);

        -- Movimento SCAR (scarico produzione), IDTBR=5, collegato a IDSPP
        -- @MGCOD/@LCCOD = '' → TRD_InsertMov usa la locazione principale dell'articolo
        DECLARE @RetVal INT;
        EXEC @RetVal = dbo.TRD_InsertMov
            @sPACOD       = @PACOD,
            @sCMCOD       = 'SCAR',
            @fMOQTV       = @PAQTP,
            @sMGCOD       = @MGCOD,
            @sLCCOD       = @LCCOD,
            @iIDRIF       = @IDSPP,
            @iIDTBR       = 5,
            @operatorCode = @OPCOD,
            @sOLCOD       = @OLCOD;

        IF ISNULL(@RetVal, -1) <= 0
        BEGIN
            ROLLBACK;
            SET @ErrMsg = 'TRD_InsertMov SCAR fallita per ' + @PACOD;
            RETURN;
        END

        COMMIT;
        SET @IDMOV = @RetVal;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        SET @ErrMsg = ERROR_MESSAGE();
    END CATCH
END
GO

-- ─── SP 3: chiude sessione operatore ─────────────────────────────────────────
CREATE OR ALTER PROCEDURE dbo.WMS_ClosePickSession
    @IDSES INT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.S_SES SET SESTO = 2, SESFE = SYSDATETIME() WHERE IDSES = @IDSES;
END
GO

-- ─── SP legacy: sessione per-riga (mantenuta per compatibilità) ───────────────
CREATE OR ALTER PROCEDURE dbo.WMS_InsertPick
    @OLCOD  VARCHAR(10),
    @PACOD  VARCHAR(20),
    @PADSC  VARCHAR(160),
    @PAUDM  VARCHAR(10),
    @PAQTP  NUMERIC(18,6),
    @OPCOD  VARCHAR(15),
    @NOCOD  SMALLINT,
    @IDMOV  INT OUTPUT,
    @ErrMsg VARCHAR(255) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @IDMOV  = -1;
    SET @ErrMsg = '';
    BEGIN TRY
        DECLARE @IDSES INT, @SesErr VARCHAR(255);
        EXEC dbo.WMS_OpenPickSession @OLCOD, @OPCOD, @NOCOD, @IDSES OUTPUT, @SesErr OUTPUT;
        IF @IDSES <= 0
        BEGIN
            SET @ErrMsg = 'Apertura sessione fallita: ' + ISNULL(@SesErr,'');
            RETURN;
        END

        EXEC dbo.WMS_InsertPickLine
             @OLCOD  = @OLCOD, @PACOD = @PACOD, @PADSC = @PADSC,
             @PAUDM  = @PAUDM, @PAQTP = @PAQTP, @OPCOD = @OPCOD,
             @NOCOD  = @NOCOD, @IDSES = @IDSES,
             @IDMOV  = @IDMOV  OUTPUT,
             @ErrMsg = @ErrMsg OUTPUT;

        EXEC dbo.WMS_ClosePickSession @IDSES;
    END TRY
    BEGIN CATCH
        SET @ErrMsg = ERROR_MESSAGE();
    END CATCH
END
GO
