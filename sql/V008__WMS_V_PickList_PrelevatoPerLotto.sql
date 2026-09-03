-- ============================================================
-- V008 — WMS_V_PickList: QtaPrelevata aggregata per LOTTO
--
-- PROBLEMA
-- Un lotto (CONUM/LOCOD) ha N fasi in A_LAV, ognuna con la sua
-- bolla (OLCOD). L_ODLA lega ogni OLCOD allo stesso CONUM/LOCOD,
-- quindi WMS_V_PickList espone lo STESSO fabbisogno componenti
-- su tutte le bolle del lotto.
-- La QtaPrelevata però veniva letta con join su S_PAP.OLCOD =
-- L_ODLA.OLCOD: un prelievo registrato sulla bolla della fase 7
-- risultava "non prelevato" aprendo la bolla della fase 1.
--
-- Caso reale: lotto 13811/1, articolo 50002436 prelevato su
-- 0000760255 (fase "Controlli e collaudi") → il WMS lo mostrava
-- ancora da prelevare su 0000760249 (fase "PROGETTAZIONE").
-- XV_SITUAZIONE_PRELIEVI non aveva il problema perché aggrega
-- S_PAP per CONUM/LOCOD/PACOD (CTE partiPrelevate).
--
-- FIX
-- QtaPrelevata da CTE PartiPrelevate: SUM(S_PAP.PAQTP) per
-- CONUM/LOCOD/PACOD su tutte le bolle del lotto.
--
-- LIMITE NOTO
-- S_PAP non ha riferimento al lotto: se una bolla copre più lotti
-- (10 casi su bolle aperte al 2026-09-03) la quantità prelevata
-- viene attribuita a ciascuno di essi. Stesso limite di
-- XV_SITUAZIONE_PRELIEVI — non risolvibile con lo schema attuale.
-- ============================================================
CREATE OR ALTER VIEW dbo.WMS_V_PickList AS

WITH PartiPrelevate AS (
    -- Prelievi consolidati a livello di lotto: sommati su tutte le
    -- bolle/fasi che appartengono allo stesso CONUM/LOCOD.
    SELECT
        odla.CONUM,
        odla.LOCOD,
        pap.PACOD,
        SUM(pap.PAQTP) AS QtaPrelevata,
        MAX(pap.IDPAP) AS IDPAP
    FROM dbo.L_ODLA odla
    JOIN dbo.S_PAP  pap ON pap.OLCOD = odla.OLCOD
    GROUP BY odla.CONUM, odla.LOCOD, pap.PACOD
)

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
    ISNULL(pap.QtaPrelevata, 0)   AS QtaPrelevata,
    pap.IDPAP
FROM dbo.L_ODLA odla
JOIN dbo.S_ODL  odl  ON odl.OLCOD  = odla.OLCOD AND odl.OLSTF < 54
JOIN dbo.A_LOT  lot  ON lot.CONUM  = odla.CONUM AND lot.LOCOD  = odla.LOCOD
JOIN dbo.L_CMFE fe   ON fe.CONUM   = lot.CONUM  AND fe.LOCLP   = lot.LOCOD
LEFT JOIN dbo.a_THP thp  ON thp.IDTHP  = fe.IDTHP
JOIN dbo.A_PAR  par  ON par.PACOD  = fe.PACOD
LEFT JOIN dbo.L_MLPA mlpa ON mlpa.PACOD = fe.PACOD AND mlpa.LCPRC = 'Y'
LEFT JOIN PartiPrelevate pap ON pap.CONUM = odla.CONUM
                            AND pap.LOCOD = odla.LOCOD
                            AND pap.PACOD = fe.PACOD
GROUP BY odla.OLCOD, lot.CONUM, lot.LOCOD, fe.IDCMFE, fe.PACOD, fe.PADSC, fe.PAUDM,
         thp.THDSC, par.paf02, mlpa.QTLOC, pap.QtaPrelevata, pap.IDPAP

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
    ISNULL(pap.QtaPrelevata, 0)   AS QtaPrelevata,
    pap.IDPAP
FROM dbo.L_ODLA odla
JOIN dbo.S_ODL  odl  ON odl.OLCOD  = odla.OLCOD AND odl.OLSTF < 54
JOIN dbo.A_LOT  lp   ON lp.CONUM   = odla.CONUM AND lp.LOCOD   = odla.LOCOD
JOIN dbo.A_LOT  lf   ON lf.CONUM   = lp.CONUM   AND lf.LOCLP   = lp.LOCOD AND lf.LOCOD <> lp.LOCOD
LEFT JOIN dbo.a_THP thp  ON thp.IDTHP  = lf.IDTHP
JOIN dbo.A_PAR  par  ON par.PACOD  = lf.PACOD
LEFT JOIN dbo.L_MLPA mlpa ON mlpa.PACOD = lf.PACOD AND mlpa.LCPRC = 'Y'
LEFT JOIN PartiPrelevate pap ON pap.CONUM = odla.CONUM
                            AND pap.LOCOD = odla.LOCOD
                            AND pap.PACOD = lf.PACOD
GROUP BY odla.OLCOD, lp.CONUM, lp.LOCOD, lf.IDLOT, lf.PACOD, lf.PADSC, lf.PAUDM,
         thp.THDSC, par.paf02, mlpa.QTLOC, pap.QtaPrelevata, pap.IDPAP;
GO
