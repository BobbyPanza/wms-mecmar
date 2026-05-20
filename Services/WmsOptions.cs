namespace WMS.Services;

public class WmsOptions
{
    /// <summary>
    /// Magazzini abilitati per le operazioni WMS (MGCOD).
    /// Se vuoto, nessun filtro — tutti i magazzini sono visibili.
    /// Es: ["01", "MEC"]
    /// </summary>
    public string[] AllowedWarehouses { get; set; } = [];
}
