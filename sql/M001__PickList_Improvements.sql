-- ============================================================
-- M001 — Miglioramenti tabelle staging pick list
-- DB: Logic (WMS)
-- Idempotente: usa IF NOT EXISTS / IF EXISTS ovunque.
-- ============================================================

-- 1. AssignedOperator su WMS_PickList (chi è assegnato alla lista)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WMS_PickList')
      AND name = 'AssignedOperator'
)
    ALTER TABLE dbo.WMS_PickList
        ADD AssignedOperator NVARCHAR(15) NULL;
GO

-- 2. ErpSesId su WMS_PickListPick (traccia a quale S_SES appartiene il pick eseguito)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WMS_PickListPick')
      AND name = 'ErpSesId'
)
    ALTER TABLE dbo.WMS_PickListPick
        ADD ErpSesId INT NULL;
GO

-- ============================================================
-- SP per FactoryMecmar: crea intestazione lista
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.WMS_CreatePickList
    @Code             NVARCHAR(30),
    @Description      NVARCHAR(100)    = '',
    @AssignedOperator NVARCHAR(15)     = NULL,
    @CreatedBy        NVARCHAR(15)     = '',
    @ListId           UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @ListId = NEWID();
    INSERT INTO dbo.WMS_PickList
        (Id, Code, Description, AssignedOperator, Status, CreatedByOp)
    VALUES
        (@ListId, @Code, @Description, @AssignedOperator, 'Open', @CreatedBy);
END
GO

-- ============================================================
-- SP per FactoryMecmar: aggiunge una riga alla lista
-- ============================================================
CREATE OR ALTER PROCEDURE dbo.WMS_AddPickListRow
    @ListId           UNIQUEIDENTIFIER,
    @OlCod            NVARCHAR(20)     = NULL,   -- bolla di lavoro / commessa
    @ArticleCode      NVARCHAR(20),
    @ArticleDesc      NVARCHAR(200)    = '',
    @UoM              NVARCHAR(10)     = '',
    @PlannedQty       NUMERIC(18,6),
    @Handling         NVARCHAR(50)     = NULL,
    @IdSpec           INT              = NULL,
    @IdGroup          INT              = NULL,
    @MainWarehouseCode NVARCHAR(15)   = '',
    @MainLocationCode  NVARCHAR(15)   = '',
    @SortOrder        INT              = 0
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.WMS_PickListRow
        (Id, ListId, ArticleCode, ArticleDesc, UoM, PlannedQty,
         OlCod, Handling, IdSpec, IdGroup,
         MainWarehouseCode, MainLocationCode,
         IsMissing, IsExtraItem, RowStatus, SortOrder)
    VALUES
        (NEWID(), @ListId, @ArticleCode, @ArticleDesc, @UoM, @PlannedQty,
         @OlCod, @Handling, @IdSpec, @IdGroup,
         @MainWarehouseCode, @MainLocationCode,
         0, 0, 'Pending', @SortOrder);
END
GO
