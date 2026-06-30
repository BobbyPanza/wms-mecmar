-- Vista documenti tecnici articolo per WMS_ArticleInfoDialog
-- RelativePath costruito con convenzione: prime 5 lettere PACOD = cartella
CREATE OR ALTER VIEW dbo.WMS_V_ArticleDocuments AS
SELECT
    PACOD,
    LEFT(PACOD, 5) + '/' + PACOD + '.pdf' AS RelativePath,
    CAST('Scheda tecnica' AS NVARCHAR(200))  AS Nome
FROM dbo.A_PAR
WHERE PACOD IS NOT NULL AND PACOD <> '';
