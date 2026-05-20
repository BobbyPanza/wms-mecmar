-- ============================================================
-- D001 — Template di stampa: Etichetta accettazione piccola
-- DB: Logic (WMS)
-- Idempotente: inserisce solo se non esiste già.
-- ============================================================

IF NOT EXISTS (
    SELECT 1 FROM dbo.WMS_PrintTemplate
    WHERE Context = 'ACCEPTANCE' AND ReportName = 'AccettazionePiccola'
)
BEGIN
    INSERT INTO dbo.WMS_PrintTemplate (Context, Name, ReportName, PrinterName, IsActive)
    VALUES ('ACCEPTANCE', 'Etichetta piccola accettazione', 'AccettazionePiccola', NULL, 1);

    DECLARE @TplId INT = CAST(SCOPE_IDENTITY() AS INT);

    INSERT INTO dbo.WMS_PrintTemplateParam
        (TemplateId, ParamName, AutoFillKey, Label, IsRequired, SortOrder)
    VALUES
        (@TplId, 'PartCode', 'PACOD', 'Codice Articolo', 1, 1),
        (@TplId, 'IdRig',    'IDRIG', 'ID Riga DDT',     1, 2);
END
GO
