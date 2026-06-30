-- ============================================================
-- V001 — Vista WMS_V_PickList su FactoryMecmar
-- Legge la lista di prelievo per bolla di lavoro (OLCOD).
-- Unisce parti esterne (L_CMFE) e lotti interni (A_LOT figli).
-- Colonne: OLCOD, CONUM, LOCOD, PACOD, PADSC, PAUDM,
--          Handling (da a_THP), Ubicazione (A_PAR.paf02),
--          Giacenza (L_MLPA locazione principale LCPRC='Y'),
--          QtaDaPrelevare (somma qty), QtaPrelevata (S_PAP),
--          IDPAP (NULL se non ancora iniziata).
--
-- Filtro S_ODL.OLSTF < 54: esclude bolle già evase/chiuse,
-- riduce drasticamente il dataset di base della vista.
-- ============================================================
CREATE OR ALTER VIEW dbo.WMS_V_PickList AS

-- Parti esterne (L_CMFE)
SELECT
    odla.OLCOD,
    lot.CONUM,
    lot.LOCOD,
    fe.IDCMFE,
    NULL         AS IDLOT,
    fe.PACOD,
    fe.PADSC,
    fe.PAUDM,
    SUM(fe.LOQTP) AS QtaDaPrelevare,
    COALESCE(thp.THDSC, '(N.D.)') AS Handling,
    COALESCE(par.paf02, '')        AS Ubicazione,
    ISNULL(mlpa.QTLOC, 0)         AS Giacenza,
    ISNULL(pap.PAQTP, 0)          AS QtaPrelevata,
    pap.IDPAP
FROM dbo.L_ODLA odla
JOIN dbo.S_ODL  odl  ON odl.OLCOD  = odla.OLCOD AND odl.OLSTF < 54
JOIN dbo.A_LOT  lot  ON lot.CONUM  = odla.CONUM AND lot.LOCOD  = odla.LOCOD
JOIN dbo.L_CMFE fe   ON fe.CONUM   = lot.CONUM  AND fe.LOCLP   = lot.LOCOD
LEFT JOIN dbo.a_THP thp  ON thp.IDTHP  = fe.IDTHP
JOIN dbo.A_PAR  par  ON par.PACOD  = fe.PACOD
LEFT JOIN dbo.L_MLPA mlpa ON mlpa.PACOD = fe.PACOD AND mlpa.LCPRC = 'Y'
LEFT JOIN dbo.S_PAP  pap  ON pap.OLCOD  = odla.OLCOD AND pap.PACOD = fe.PACOD
GROUP BY odla.OLCOD, lot.CONUM, lot.LOCOD, fe.IDCMFE, fe.PACOD, fe.PADSC, fe.PAUDM,
         thp.THDSC, par.paf02, mlpa.QTLOC, pap.PAQTP, pap.IDPAP

UNION ALL

-- Lotti interni (A_LOT figli — sub-lotti)
SELECT
    odla.OLCOD,
    lp.CONUM,
    lp.LOCOD,
    NULL         AS IDCMFE,
    lf.IDLOT,
    lf.PACOD,
    lf.PADSC,
    lf.PAUDM,
    SUM(CASE WHEN lp.LOQTP > 0 THEN lf.LOQDB * lp.LOQDB / lp.LOQTP ELSE 0 END) AS QtaDaPrelevare,
    COALESCE(thp.THDSC, '(N.D.)') AS Handling,
    COALESCE(par.paf02, '')        AS Ubicazione,
    ISNULL(mlpa.QTLOC, 0)         AS Giacenza,
    ISNULL(pap.PAQTP, 0)          AS QtaPrelevata,
    pap.IDPAP
FROM dbo.L_ODLA odla
JOIN dbo.S_ODL  odl  ON odl.OLCOD  = odla.OLCOD AND odl.OLSTF < 54
JOIN dbo.A_LOT  lp   ON lp.CONUM   = odla.CONUM AND lp.LOCOD   = odla.LOCOD
JOIN dbo.A_LOT  lf   ON lf.CONUM   = lp.CONUM   AND lf.LOCLP   = lp.LOCOD AND lf.LOCOD <> lp.LOCOD
LEFT JOIN dbo.a_THP thp  ON thp.IDTHP  = lf.IDTHP
JOIN dbo.A_PAR  par  ON par.PACOD  = lf.PACOD
LEFT JOIN dbo.L_MLPA mlpa ON mlpa.PACOD = lf.PACOD AND mlpa.LCPRC = 'Y'
LEFT JOIN dbo.S_PAP  pap  ON pap.OLCOD  = odla.OLCOD AND pap.PACOD = lf.PACOD
GROUP BY odla.OLCOD, lp.CONUM, lp.LOCOD, lf.IDLOT, lf.PACOD, lf.PADSC, lf.PAUDM,
         thp.THDSC, par.paf02, mlpa.QTLOC, pap.PAQTP, pap.IDPAP;
GO
