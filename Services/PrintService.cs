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
    private readonly string? _legacyUrl;   // BaseUrl servizio legacy Crystal
    private readonly HttpClient _http;
    private readonly ILogger<PrintService> _log;

    /// <summary>True se almeno un canale è configurato.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_pmUrl) || !string.IsNullOrWhiteSpace(_legacyUrl);

    /// <summary>Canale preferenziale attivo: "PM", "Legacy" o "" se non configurato.</summary>
    public string ActiveChannel =>
        !string.IsNullOrWhiteSpace(_pmUrl) ? "PM" :
        !string.IsNullOrWhiteSpace(_legacyUrl) ? "Legacy" : "";

    public PrintService(IConfiguration config, HttpClient http, ILogger<PrintService> log)
    {
        var s = config.GetSection("PrintService");
        _pmUrl     = s["IntesiPrinterManagerUrl"];
        _pmPrinter = s["IntesiPrinterManagerPrinter"];
        _pmPath    = s["PrinterManagerEndpointPath"];
        _legacyUrl = s["BaseUrl"];
        _http = http;
        _log  = log;
    }

    /// <summary>
    /// Invia una richiesta di stampa.
    /// Routing: PM se URL valorizzato e reportName NON finisce con .rpt; altrimenti legacy.
    /// </summary>
    public async Task<(bool Ok, string Msg)> PrintAsync(
        PrintTemplate template,
        string userCode,
        string pageName,
        Dictionary<string, string> paramValues)
    {
        var usePm = !string.IsNullOrWhiteSpace(_pmUrl)
                    && !template.ReportName.EndsWith(".rpt", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (usePm)
                return await SendToPrinterManagerAsync(template, userCode, pageName, paramValues);
            else if (!string.IsNullOrWhiteSpace(_legacyUrl))
                return await SendToLegacyAsync(template, paramValues);
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
        PrintTemplate template, string userCode, string pageName, Dictionary<string, string> paramValues)
    {
        var endpoint = _pmUrl!.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(_pmPath))
            endpoint += "/" + _pmPath.TrimStart('/');

        var payload = new
        {
            opts = new
            {
                printModelCode  = template.ReportName,
                userCode,
                printerName     = template.PrinterName ?? _pmPrinter,
                description_1   = pageName,
                description_2   = (string?)null,
                copyQuantities  = (int?)null,
                jsonParams      = JsonSerializer.Serialize(paramValues)
            }
        };

        _log.LogDebug("PrintService → PM {Endpoint}: {Payload}", endpoint,
            JsonSerializer.Serialize(payload));

        var resp = await _http.PostAsJsonAsync(endpoint, payload);
        return resp.IsSuccessStatusCode
            ? (true, "Stampa inviata al Printer Manager")
            : (false, $"Printer Manager risposto {(int)resp.StatusCode} {resp.ReasonPhrase}");
    }

    private async Task<(bool Ok, string Msg)> SendToLegacyAsync(
        PrintTemplate template, Dictionary<string, string> paramValues)
    {
        var endpoint = _legacyUrl!.TrimEnd('/') + "/print";

        var payload = new
        {
            report     = template.ReportName,
            printer    = template.PrinterName ?? _pmPrinter ?? "",
            parameters = paramValues
        };

        _log.LogDebug("PrintService → Legacy {Endpoint}", endpoint);

        var resp = await _http.PostAsJsonAsync(endpoint, payload);
        return resp.IsSuccessStatusCode
            ? (true, "Stampa inviata al servizio Crystal")
            : (false, $"Servizio Crystal risposto {(int)resp.StatusCode} {resp.ReasonPhrase}");
    }
}
