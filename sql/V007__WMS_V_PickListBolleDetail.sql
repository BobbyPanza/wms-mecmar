-- ============================================================
-- V007 — WMS_FN_PickListBolleDetail su Logic DB (WMS)
--
-- Applicare MANUALMENTE su Logic DB, NON tramite app.
--
-- Prima di eseguire, sostituire [FactoryMecmar] con il nome
-- effettivo del database ERP (es. cerca/sostituisci in SSMS).
--
-- Inline TVF — parametro @ListCode (codice lista di prelievo).
-- Ritorna solo gli articoli ancora da prelevare (QtaResidua > 0),
-- con riferimento commessa.
--
-- Utilizzo dal report Crystal (SQL Command):
--   SELECT * FROM dbo.WMS_FN_PickListBolleDetail('{?ListId}')
--   ORDER BY OlCod, Handling, ArticleCode
-- ============================================================

CREATE OR ALTER FUNCTION dbo.WMS_FN_PickListBolleDetail
(
    @ListId NVARCHAR(36)    -- GUID della lista (PrintAutoFill key: LISTID)
)
RETURNS TABLE AS RETURN
(
    SELECT
        olcods.OlCod,
        com.COCOD                    AS CommessaCodice,
        com.CTDSC                    AS CommessaDesc,
        v.PACOD                      AS ArticleCode,
        v.PADSC                      AS ArticleDesc,
        v.PAUDM                      AS UoM,
        v.Handling,
        v.Ubicazione                 AS Paf02,
        v.Giacenza,
        SUM(v.QtaDaPrelevare)                                   AS QtaDaPrelevare,
        MAX(ISNULL(v.QtaPrelevata, 0))                          AS QtaPrelevata,
        SUM(v.QtaDaPrelevare) - MAX(ISNULL(v.QtaPrelevata, 0)) AS QtaResidua
    FROM (
        SELECT DISTINCT r.OlCod
        FROM   dbo.WMS_PickListRow r
        WHERE  r.ListId = TRY_CONVERT(UNIQUEIDENTIFIER, @ListId)
          AND  r.OlCod IS NOT NULL
    ) olcods
    -- ↓ Sostituire [FactoryMecmar] con il nome reale del DB ERP
    JOIN [FactoryMecmar].[dbo].WMS_V_PickList v ON v.OLCOD = olcods.OlCod
    JOIN [FactoryMecmar].[dbo].A_COM com        ON com.CONUM = v.CONUM
    GROUP BY
        olcods.OlCod,
        com.COCOD, com.CTDSC,
        v.PACOD, v.PADSC, v.PAUDM,
        v.Handling, v.Ubicazione, v.Giacenza
    HAVING SUM(v.QtaDaPrelevare) - MAX(ISNULL(v.QtaPrelevata, 0)) > 0
);
GO
