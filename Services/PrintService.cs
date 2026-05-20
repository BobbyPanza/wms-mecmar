using System.Drawing.Printing;
using System.Net.Http.Json;
using System.Text.Json;
using WMS.Models;

namespace WMS.Services;

/// <summary>
/// Routing stampa: Intesi Printer Manager o legacy Crystal a seconda della config
/// e del nome report (file .rpt → legacy, tutto il resto → Printer Manager).
/// </summary>
public class PrintService
{
    private readonly string? _pmUrl;       // IntesiPrinterManagerUrl
    private readonly string? _pmPrinter;   // IntesiPrinterManagerPrinter
    private readonly string? _pmPath;      // PrinterManagerEndpointPath (opzionale)
    private readonly string  _pmUser;      // UserCode da mandare al PM
    private readonly string? _legacyUrl;   // BaseUrl servizio legacy Crystal
    private readonly HttpClient _http;
    private readonly ILogger<PrintService> _log;

    /// <summary>True se almeno un canale è configurato.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_pmUrl) || !string.IsNullOrWhiteSpace(_legacyUrl);

    /// <summary>Canale preferenziale attivo: "PM", "Legacy" o "" se non configurato.</summary>
    public string ActiveChannel =>
        !string.IsNullOrWhiteSpace(_pmUrl) ? "PM" :
        !string.IsNullOrWhiteSpace(_legacyUrl) ? "Legacy" : "";

    /// <summary>Stampante di default da config (IntesiPrinterManagerPrinter).</summary>
    public string? DefaultPrinterName => _pmPrinter;

    public PrintService(IConfiguration config, HttpClient http, ILogger<PrintService> log)
    {
        var s = config.GetSection("PrintService");
        _pmUrl     = s["IntesiPrinterManagerUrl"];
        _pmPrinter = s["IntesiPrinterManagerPrinter"];
        _pmPath    = s["PrinterManagerEndpointPath"];
        _pmUser    = s["UserCode"] ?? "WMS";
        _legacyUrl = s["BaseUrl"];
        _http = http;
        _log  = log;
    }

    /// <summary>Elenco stampanti installate sul server (per il dialog di stampa).</summary>
    public IReadOnlyList<string> GetInstalledPrinters()
    {
        try
        {
            var list = new List<string>();
            foreach (string p in PrinterSettings.InstalledPrinters)
                list.Add(p);
            return list.OrderBy(x => x).ToList();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "PrintService.GetInstalledPrinters fallito");
            return [];
        }
    }

    /// <summary>
    /// Invia una richiesta di stampa.
    /// Routing: PM se URL valorizzato e reportName NON finisce con .rpt; altrimenti legacy.
    /// printerOverride: se valorizzato sovrascrive la stampante del template e quella di config.
    /// </summary>
    public async Task<(bool Ok, string Msg)> PrintAsync(
        PrintTemplate template,
        string userCode,
        string pageName,
        Dictionary<string, string> paramValues,
        int copies = 1,
        string? printerOverride = null)
    {
        var usePm = !string.IsNullOrWhiteSpace(_pmUrl)
                    && !template.ReportName.EndsWith(".rpt", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (usePm)
                return await SendToPrinterManagerAsync(template, userCode, pageName, paramValues, copies, printerOverride);
            else if (!string.IsNullOrWhiteSpace(_legacyUrl))
                return await SendToLegacyAsync(template, paramValues, printerOverride);
            else
                return (false, "Nessun servizio di stampa configurato (PrintService:IntesiPrinterManagerUrl o BaseUrl mancanti)");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "PrintService.PrintAsync fallito per template {Id}", template.Id);
            return (false, $"Errore: {ex.Message}");
        }
    }

    // ─── Canali ───────────────────────────────────────────────────────────────

    private async Task<(bool Ok, string Msg)> SendToPrinterManagerAsync(
        PrintTemplate template, string userCode, string pageName,
        Dictionary<string, string> paramValues, int copies, string? printerOverride)
    {
        var endpoint = _pmUrl!.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(_pmPath))
            endpoint += "/" + _pmPath.TrimStart('/');

        var printer = printerOverride ?? template.PrinterName ?? _pmPrinter;

        var payload = new
        {
            opts = new
            {
                printModelCode = template.ReportName,
                userCode       = _pmUser,
                printerName    = printer,
                description_1  = pageName,
                description_2  = (string?)null,
                copyQuantities = copies,
                jsonParams     = JsonSerializer.Serialize(paramValues)
            }
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        _log.LogInformation("PrintService → PM {Endpoint}: {Payload}", endpoint, payloadJson);

        var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payloadJson, System.Text.Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-http-method-override", "PrintReport");
        req.Headers.Add("Accept", "application/json");

        var resp = await _http.SendAsync(req);
        if (resp.IsSuccessStatusCode)
            return (true, "Stampa inviata al Printer Manager");

        var body = await resp.Content.ReadAsStringAsync();
        _log.LogWarning("PrintService: PM risposto {Status} — {Body}", (int)resp.StatusCode, body);
        return (false, $"Printer Manager {(int)resp.StatusCode}: {body}");
    }

    private async Task<(bool Ok, string Msg)> SendToLegacyAsync(
        PrintTemplate template, Dictionary<string, string> paramValues, string? printerOverride)
    {
        var endpoint = _legacyUrl!.TrimEnd('/') + "/print";

        var payload = new
        {
            report     = template.ReportName,
            printer    = printerOverride ?? template.PrinterName ?? _pmPrinter ?? "",
            parameters = paramValues
        };

        _log.LogDebug("PrintService → Legacy {Endpoint}", endpoint);

        var resp = await _http.PostAsJsonAsync(endpoint, payload);
        return resp.IsSuccessStatusCode
            ? (true, "Stampa inviata al servizio Crystal")
            : (false, $"Servizio Crystal risposto {(int)resp.StatusCode} {resp.ReasonPhrase}");
    }
}
