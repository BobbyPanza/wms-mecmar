-- ============================================================
-- V006 — SP per versamento produzione da WMS
--
-- Flusso:
--   1. WMS_OpenPickSession  (già esistente) → apre S_SES, ritorna @IDSES
--   2. WMS_InsertVersamentoLine             → S_PAV + S_SPV + TRD_InsertMov CAR
--   3. WMS_ClosePickSession (già esistente) → chiude S_SES
--
-- S_SPV: IDSPV assegnato da trigger INSTEAD OF INSERT (TRG_ON_BEFORE_INSERT_SPV).
--        Il trigger TRG_ON_INSERT_SPV aggiorna S_PAV.PAQTB automaticamente.
-- ============================================================

CREATE OR ALTER PROCEDURE dbo.WMS_InsertVersamentoLine
    @BollaVers VARCHAR(10),       -- bolla di versamento (A_LAV.OLCOD con LAUFC='Y')
    @PACOD     VARCHAR(20),       -- articolo del lotto (A_LOT.PACOD)
    @PADSC     VARCHAR(160),
    @PAUDM     VARCHAR(10),
    @PAQTB     NUMERIC(18,6),     -- quantità buona da versare
    @OPCOD     VARCHAR(15),
    @NOCOD     SMALLINT,
    @IDSES     INT,               -- sessione aperta con WMS_OpenPickSession
    @MGCOD     VARCHAR(15) = '',  -- '' → locazione principale articolo
    @LCCOD     VARCHAR(15) = '',  -- '' → locazione principale articolo
    @IDMOV     INT OUTPUT,
    @ErrMsg    VARCHAR(255) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @IDMOV  = -1;
    SET @ErrMsg = '';
    BEGIN TRY
        BEGIN TRANSACTION;

        -- S_PAV: cerca o crea per bolla versamento + articolo
        DECLARE @IDPAV INT;
        SELECT @IDPAV = IDPAV FROM dbo.S_PAV WHERE OLCOD = @BollaVers AND PACOD = @PACOD;
        IF @IDPAV IS NULL
        BEGIN
            EXEC @IDPAV = dbo.GetProgressivo 'dbo.S_PAV';
            INSERT INTO dbo.S_PAV (IDPAV, OLCOD, PACOD, PADSC, PAUDM, PAQTP, PAQTB, PAQTS, PAQTC, PAQTR)
            VALUES (@IDPAV, @BollaVers, @PACOD, @PADSC, @PAUDM, @PAQTB, 0, 0, 0, 0);
        END

        -- S_SPV: inserisci con IDSPV=0 → TRG_ON_BEFORE_INSERT_SPV assegna il progressivo
        --        TRG_ON_INSERT_SPV aggiornerà S_PAV.PAQTB automaticamente
        DECLARE @SESIE DATETIME2 = (SELECT SESIE FROM dbo.S_SES WHERE IDSES = @IDSES);
        INSERT INTO dbo.S_SPV
            (IDSPV, SESIE, NOCOD, PAQTB, PAQTS, PAQTV, PAQTR, IDSES, IDPAV, VPSTP, OPAUT)
        VALUES
            (0, @SESIE, @NOCOD, @PAQTB, 0, 0, 0, @IDSES, @IDPAV, SYSDATETIME(), @OPCOD);

        -- Leggi IDSPV assegnato dal trigger (max per IDSES+IDPAV, sicuro in contesto monosessione)
        DECLARE @IDSPV INT;
        SELECT @IDSPV = MAX(IDSPV) FROM dbo.S_SPV WHERE IDSES = @IDSES AND IDPAV = @IDPAV;

        -- Movimento CAR (carico da produzione), IDTBR=6, collegato a IDSPV
        DECLARE @RetVal INT;
        EXEC @RetVal = dbo.TRD_InsertMov
            @sPACOD       = @PACOD,
            @sCMCOD       = 'CAR',
            @fMOQTV       = @PAQTB,
            @sMGCOD       = @MGCOD,
            @sLCCOD       = @LCCOD,
            @iIDRIF       = @IDSPV,
            @iIDTBR       = 6,
            @operatorCode = @OPCOD,
            @sOLCOD       = @BollaVers;

        IF ISNULL(@RetVal, -1) <= 0
        BEGIN
            ROLLBACK;
            SET @ErrMsg = 'TRD_InsertMov CAR fallita per ' + @PACOD;
            RETURN;
        END

        -- Se versamento completato (PAQTB >= PAQTP), chiudi A_LAV e S_ODL
        -- Il trigger TRG_ON_INSERT_SPV ha già aggiornato S_PAV.PAQTB nella stessa transazione
        DECLARE @PAQTB_TOT NUMERIC(18,6), @PAQTP_TOT NUMERIC(18,6);
        SELECT @PAQTB_TOT = PAQTB, @PAQTP_TOT = PAQTP FROM dbo.S_PAV WHERE IDPAV = @IDPAV;
        IF @PAQTP_TOT > 0 AND @PAQTB_TOT >= @PAQTP_TOT
        BEGIN
            UPDATE dbo.A_LAV SET LASTO = 45 WHERE OLCOD = @BollaVers AND LAUFC = 'Y';  -- 45 = Terminata
            UPDATE dbo.S_ODL SET OLSTF = 54 WHERE OLCOD = @BollaVers;                  -- 54 = Chiusa
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
