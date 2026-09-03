namespace WMS.Services;

public class AcceptanceOptions
{
    /// <summary>Valori DTSO che indicano documento "da accettare".</summary>
    public int[] PendingStatuses { get; set; } = [200, 201];

    /// <summary>Valore DTSO da scrivere su A_DOT alla chiusura del documento.</summary>
    public int AcceptedStatus { get; set; } = -4;

    /// <summary>
    /// Nome della colonna flag su A_LOC che individua la locazione di default per l'accettazione.
    /// TODO: verificare con Mecmar il nome colonna esatto (es. LCACC, LCRIC, ecc.).
    /// </summary>
    public string LocationFlagColumn { get; set; } = "LCACC";

    /// <summary>Causale TRD_InsertMov per lo scarico dal magazzino documento (L_DRCR).</summary>
    public string DischargeCausal { get; set; } = "SMI";

    /// <summary>Causale TRD_InsertMov per il carico merce in accettazione.</summary>
    public string LoadCausal { get; set; } = "CMI";

    /// <summary>
    /// IDNTF da usare per le notifiche S_NTF sulle anomalie di accettazione
    /// (quantità sotto/sopra atteso, merce in NC). 0 = notifiche disabilitate.
    /// Vedere catalogo A_NTF sul DB ERP per i valori disponibili.
    /// </summary>
    public int AnomalyNotificationId { get; set; } = 101;

    /// <summary>
    /// Regole ordinate per selezione automatica del report etichetta.
    /// La prima regola che fa match vince; usare Field="*" / Pattern="*" come fallback finale.
    /// </summary>
    public List<ReportRule> DefaultReportRules { get; set; } = [];
}

public class ReportRule
{
    /// <summary>Campo su cui applicare il pattern: "Family", "ArticleCode", o "*" (sempre vero).</summary>
    public string Field { get; set; } = "*";

    /// <summary>Pattern regex (o "*" per match incondizionato).</summary>
    public string Pattern { get; set; } = "*";

    /// <summary>Nome report da restituire quando il pattern fa match.</summary>
    public string ReportName { get; set; } = "";
}
