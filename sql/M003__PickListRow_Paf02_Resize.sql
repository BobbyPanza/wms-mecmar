-- ============================================================
-- M003 — Allarga Paf02 da NVARCHAR(50) a NVARCHAR(200)
-- DB: Logic (WMS) — idempotente
-- Motivazione: A_PAR.PAF02 è campo libero, può superare 50 caratteri.
-- ============================================================
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WMS_PickListRow')
      AND name = 'Paf02'
      AND max_length < 400   -- NVARCHAR(200) = 400 bytes
)
    ALTER TABLE dbo.WMS_PickListRow
        ALTER COLUMN Paf02 NVARCHAR(200) NULL;
GO
