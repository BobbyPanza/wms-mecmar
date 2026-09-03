-- ============================================================
-- V010 — L_MLPA: colonne di verifica abbinamento articolo/locazione
--
-- RICHIESTA CLIENTE
-- Alla conferma di una rettifica (/adjustments) va registrato CHI ha
-- verificato l'abbinamento articolo/locazione e QUANDO, anche quando
-- la quantità viene confermata e quindi non si genera alcun movimento.
--
-- X_VerifiedUser  → A_OPR.OPCOD dell'operatore (varchar(15), come OPCOD)
-- X_VerifiedDate  → data/ora della verifica
--
-- Prefisso X_ = campo custom, non standard Intesi.
--
-- NOTA sui trigger di L_MLPA
-- TRG_ON_UPDATE_MLPA aggiorna LASTUPDATE solo se cambiano
-- QTLOC/QTMAX/QTMIN, e tocca WarehousePart solo se cambiano
-- PACOD/MGCOD. Lo stamp di verifica scrive solo le due colonne nuove,
-- quindi NON altera LASTUPDATE — che resta il timestamp dell'ultima
-- variazione di quantità. Voluto: sono due informazioni diverse.
--
-- Idempotente: si può rieseguire.
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.L_MLPA') AND name = 'X_VerifiedUser')
BEGIN
    ALTER TABLE dbo.L_MLPA ADD X_VerifiedUser varchar(15) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.L_MLPA') AND name = 'X_VerifiedDate')
BEGIN
    ALTER TABLE dbo.L_MLPA ADD X_VerifiedDate datetime NULL;
END
GO

-- Descrizioni per chi guarda la tabella da SSMS
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
               WHERE major_id = OBJECT_ID('dbo.L_MLPA') AND name = 'MS_Description'
                 AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.L_MLPA'), 'X_VerifiedUser', 'ColumnId'))
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description',
        @value = N'WMS custom: A_OPR.OPCOD dell''operatore che ha verificato l''abbinamento articolo/locazione (anche senza movimento)',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'L_MLPA',
        @level2type = N'COLUMN', @level2name = N'X_VerifiedUser';
GO

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties
               WHERE major_id = OBJECT_ID('dbo.L_MLPA') AND name = 'MS_Description'
                 AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.L_MLPA'), 'X_VerifiedDate', 'ColumnId'))
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description',
        @value = N'WMS custom: data/ora dell''ultima verifica dell''abbinamento articolo/locazione',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'L_MLPA',
        @level2type = N'COLUMN', @level2name = N'X_VerifiedDate';
GO
