-- ============================================================
-- V003 — Viste WMS_V_AcceptanceDocs e WMS_V_AcceptanceLines
--        su FactoryMecmar per il modulo Accettazione merci WMS.
--
-- WMS_V_AcceptanceDocs — testate documento (A_DOT JOIN A_FOR).
--   Filtrare per DTDO e DTSTO dall'applicazione (configurabili
--   in AcceptanceOptions su appsettings.json).
--
-- WMS_V_AcceptanceLines — righe documento (A_DOR).
--   Espone solo righe con articolo valorizzato (PACOD IS NOT NULL)
--   e quantità positiva (DRQTI > 0).
--   Arricchire in futuro con JOIN A_PAR se servono dati articolo.
-- ============================================================

CREATE OR ALTER VIEW dbo.WMS_V_AcceptanceDocs AS
SELECT
    t.IDTES,
    t.DTDO,
    t.DTSTO,
    t.DTCOD,
    t.DTDTE,
    t.DTRIF,
    t.IDFOR,
    f.FORAG
FROM dbo.A_DOT t
JOIN dbo.A_FOR f ON f.IDFOR = t.IDFOR
WHERE t.DTDO IN ('RLA', 'RFL', 'DCF');
-- Per aggiungere/rimuovere tipi documento: ALTER VIEW, nessun deploy.
GO

-- ============================================================

CREATE OR ALTER VIEW dbo.WMS_V_AcceptanceLines AS
SELECT
    r.IDRIG,
    r.IDTES,
    r.PACOD,
    r.DRDSC,
    r.DRUMI,
    r.DRQTI,
    r.DRPOS,
    r.DRSTO
FROM dbo.A_DOR r
WHERE r.PACOD IS NOT NULL
  AND r.DRQTI > 0;
GO
