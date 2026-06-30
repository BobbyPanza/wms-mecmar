using WMS.Models;

namespace WMS.Services;

public class SessionService
{
    public string? OperatorCode  { get; private set; }
    public string? OperatorName  { get; private set; }
    public string? OperatorRole  { get; private set; }
    public string? GroupCode     { get; private set; }
    public bool IsLoggedIn   => OperatorCode is not null;
    public bool IsSupervisor => OperatorRole is "SUP" or "ADM";

    /// <summary>
    /// CDNOD del dispositivo corrente (da A_NOD, salvato in localStorage).
    /// 0 finché non inizializzato da MainLayout.
    /// </summary>
    public int NodeId { get; private set; }
    public bool NodeReady => NodeId > 0;

    public void SetNodeId(int cdnod)
    {
        NodeId = cdnod;
        NotifyStateChanged();
    }

    public List<CartRow> Cart { get; } = [];
    public string PageTitle { get; private set; } = "WMS Mecmar";

    // Contesto stampa — impostato dalla pagina corrente
    public string PrintContext { get; private set; } = "";
    public Dictionary<string, string> PrintAutoFill { get; private set; } = new();

    public event Action? OnChange;

    public void Login(string code, string name, string role, string groupCode)
    {
        OperatorCode = code;
        OperatorName = name;
        OperatorRole = role;
        GroupCode    = groupCode;
        Cart.Clear();
        NotifyStateChanged();
    }

    public void Logout()
    {
        OperatorCode = null;
        OperatorName = null;
        OperatorRole = null;
        GroupCode    = null;
        Cart.Clear();
        NotifyStateChanged();
    }

    public void SetPageTitle(string title)
    {
        PageTitle = title;
        NotifyStateChanged();
    }

    /// <summary>
    /// Imposta il contesto stampa per la pagina corrente.
    /// autoFill: valori che vengono pre-compilati nei parametri del template (es. {"PACOD","ART001"}).
    /// </summary>
    public void SetPrintContext(string context, Dictionary<string, string>? autoFill = null)
    {
        PrintContext  = context;
        PrintAutoFill = autoFill ?? new();
        NotifyStateChanged();
    }

    public void ClearPrintContext()
    {
        PrintContext  = "";
        PrintAutoFill = new();
        NotifyStateChanged();
    }

    public void NotifyStateChanged() => OnChange?.Invoke();
}
