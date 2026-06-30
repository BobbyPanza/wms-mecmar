-- ============================================================
-- M002 — Aggiunge Paf02 (ubicazione old-style) a WMS_PickListRow
-- DB: Logic (WMS) — idempotente
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.WMS_PickListRow')
      AND name = 'Paf02'
)
    ALTER TABLE dbo.WMS_PickListRow
        ADD Paf02 NVARCHAR(200) NULL;
GO
