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

        -- Deriva COCOD e COTYP dalla bolla: L_ODLA → A_LOT → A_COM.
        -- TRD_InsertMov NON ricava COCOD da OLCOD automaticamente; senza di esso
        -- ComputeLotToBeCommittedQuantity / ComputeCmfeUsedQuantity non vedono
        -- il prelievo e L_CMFE.PAQIM / A_LOT.LOQIM non vengono decrementati.
        -- COTYP serve a ComputeLotOdlRequiredQuantity / ComputeLotRequiredQuantity.
        DECLARE @COCOD CHAR(15), @COTYP CHAR(1);
        SELECT TOP 1 @COCOD = com.COCOD, @COTYP = com.COTYP
        FROM dbo.L_ODLA odla
        JOIN dbo.A_LOT  lot ON lot.CONUM = odla.CONUM AND lot.LOCOD = odla.LOCOD
        JOIN dbo.A_COM  com ON com.CONUM  = lot.CONUM
        WHERE odla.OLCOD = @OLCOD;

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
            @sOLCOD       = @OLCOD,
            @sCOCOD       = @COCOD;

        IF ISNULL(@RetVal, -1) <= 0
        BEGIN
            ROLLBACK;
            SET @ErrMsg = 'TRD_InsertMov SCAR fallita per ' + @PACOD;
            RETURN;
        END

        COMMIT;
        SET @IDMOV = @RetVal;

        -- ── Aggiorna impegni live (approccio delta) ─────────────────────────
        -- Formula: delta = qty * required_lot / required_bolla
        -- I denominatori (required_bolla) sono scalari uguali per tutti i prelievi
        -- della stessa OLCOD+PACOD: vengono calcolati una sola volta.
        -- Non-fatal: il movimento è già committato, FixEngagedQuantity correggerà.
        -- Floor a 0: PAQIM/LOQIM non possono scendere sotto zero.
        BEGIN TRY
            -- ─ L_CMFE.PAQIM ──────────────────────────────────────────────────
            DECLARE @OdlReqCmfe NUMERIC(18,6);
            SET @OdlReqCmfe = dbo.ComputeCmfeOdlRequiredQuantity(@OLCOD, @PACOD);

            IF ISNULL(@OdlReqCmfe, 0) > 0
                UPDATE t1
                SET t1.PAQIM =
                        CASE WHEN t1.PAQIM
                                  - ROUND(@PAQTP
                                          * dbo.ComputeCmfeRequiredQuantity(t1.CONUM, t1.FINUM, t1.LOCLP, t1.LOQDB)
                                          / @OdlReqCmfe, 5) < 0
                             THEN 0
                             ELSE ROUND(t1.PAQIM
                                        - @PAQTP
                                          * dbo.ComputeCmfeRequiredQuantity(t1.CONUM, t1.FINUM, t1.LOCLP, t1.LOQDB)
                                          / @OdlReqCmfe, 5)
                        END,
                    t1.SavedEngagedQuantity = 0
                FROM dbo.L_CMFE  t1
                JOIN dbo.A_LOT   t2 ON t1.LOCLP = t2.LOCOD AND t1.CONUM = t2.CONUM
                JOIN dbo.L_ODLA  t3 ON t3.CONUM  = t2.CONUM AND t3.LOCOD = t2.LOCOD
                WHERE t1.PACOD = @PACOD AND t1.PAQIM > 0 AND t3.OLCOD = @OLCOD
                  AND ISNULL(t1.PANES, 'N') = 'N';

            -- ─ A_LOT.LOQIM ───────────────────────────────────────────────────
            DECLARE @OdlReqLot NUMERIC(18,6);
            SET @OdlReqLot = dbo.ComputeLotOdlRequiredQuantity(@OLCOD, @PACOD, @COCOD, @COTYP);

            IF ISNULL(@OdlReqLot, 0) > 0
                UPDATE t1
                SET t1.LOQIM =
                        CASE WHEN t1.LOQIM
                                  - ROUND(@PAQTP
                                          * dbo.ComputeLotRequiredQuantity(t1.CONUM, t1.LOCOD, t1.LOCLM, t1.LOCLP,
                                                                            t1.LOQDB, t1.LOPSP, @COCOD, @COTYP)
                                          / @OdlReqLot, 5) < 0
                             THEN 0
                             ELSE ROUND(t1.LOQIM
                                        - @PAQTP
                                          * dbo.ComputeLotRequiredQuantity(t1.CONUM, t1.LOCOD, t1.LOCLM, t1.LOCLP,
                                                                            t1.LOQDB, t1.LOPSP, @COCOD, @COTYP)
                                          / @OdlReqLot, 5)
                        END,
                    t1.SavedEngagedQuantity = 0
                FROM dbo.A_LOT  t1
                JOIN dbo.A_LOT  t2 ON t1.LOCLP = t2.LOCOD AND t1.LOCOD <> t2.LOCOD AND t1.CONUM = t2.CONUM
                JOIN dbo.L_ODLA t3 ON t3.CONUM  = t2.CONUM AND t3.LOCOD = t2.LOCOD
                WHERE t1.PACOD = @PACOD AND t1.LOQIM > 0 AND t3.OLCOD = @OLCOD;
        END TRY
        BEGIN CATCH
            DECLARE @EngErr NVARCHAR(500) = N'WMS_InsertPickLine: aggiornamento impegni fallito per '
                + @PACOD + N'/' + @OLCOD + N': ' + ERROR_MESSAGE();
            RAISERROR(@EngErr, 10, 1);
        END CATCH
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
