-- Esempio lettura operatore per login WMS (tabella gestionale A_OPR)
-- Connection string: ConnectionStrings:ErpDatabase → es. Database=FactoryMecmar

-- Parametri applicativi: @Opcod (NVARCHAR), confronto password lato app con OPPSW
-- (aggiungere filtri su colonna stato se presente in A_OPR)

SELECT
    OPCOD,
    OPDSC,
    OPPSW,
    GRCOD
FROM dbo.A_OPR
WHERE OPCOD = @Opcod;

-- Variante case-insensitive su codice operatore (solo se necessario):
-- WHERE UPPER(LTRIM(RTRIM(OPCOD))) = UPPER(LTRIM(RTRIM(@Opcod)));
