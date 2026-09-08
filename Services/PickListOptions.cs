namespace WMS.Services;

public class PickListOptions
{
    /// <summary>
    /// IDNTF da usare per le notifiche S_NTF alla chiusura lista
    /// quando ci sono righe incomplete o mancanti. 0 = notifiche disabilitate.
    /// Vedere catalogo A_NTF sul DB ERP per i valori disponibili.
    /// </summary>
    public int MissingNotificationId { get; set; } = 102;
}
