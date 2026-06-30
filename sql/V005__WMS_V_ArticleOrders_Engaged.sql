-- ============================================================
-- WMS_V_ArticleAvailability — ordini e impegni per articolo
--
-- RowType (da GetInventoryBalanceAvailability):
--   3 = riga ordine cliente (impegno)
--   4 = impegno produzione (semilavorato)
--   5 = ordine di produzione
--   6 = workplan (impegno)
--   7 = ordine di acquisto
--
-- Il WMS filtra per RowType in C# dopo aver letto la vista.
-- Modificare filtri e colonne liberamente.
-- ============================================================

CREATE OR ALTER VIEW dbo.WMS_V_ArticleAvailability AS
SELECT
    p.PACOD,
    g.RowType,
    g.OrderCode,
    g.RefDescription                          AS Description,
    COALESCE(g.QtyEngaged, g.QtyOrdered, 0)  AS Qty,
    g.RowDate                                 AS DueDate
FROM dbo.A_PAR p
CROSS APPLY dbo.GetInventoryBalanceAvailability(p.PACOD, 0, 0) g
WHERE g.RowType IN (3, 4, 5, 6, 7)
  AND COALESCE(g.QtyEngaged, g.QtyOrdered, 0) > 0;
GO
