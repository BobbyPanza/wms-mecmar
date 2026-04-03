# Intesi Printer Manager — integrazione WMS

## Routing (implementato in `PrintingOrchestratorService`)

| Condizione | Canale |
|------------|--------|
| `IntesiPrinterManagerUrl` valorizzato **e** `reportName` **non** è file `.rpt` | POST verso Printer Manager |
| Altrimenti | Servizio legacy Crystal (`PrintService:BaseUrl`, endpoint relativo `print`) |

## Payload Printer Manager

```json
{
  "opts": {
    "printModelCode": "[mode_code]",
    "userCode": "[operator_code]",
    "printerName": "[IntesiPrinterManagerPrinter o printerName richiesta]",
    "description_1": "[page_name]",
    "description_2": null,
    "copyQuantities": null,
    "jsonParams": "{ ... parametri report serializzati ... }"
  }
}
```

- **`userCode`**: da `PrintRequest.UserCode` o claim `NameIdentifier` (es. OPCOD).
- **`jsonParams`**: JSON stringa dei parametri chiave/valore (es. ID, PACOD).

## Config appsettings (`PrintService`)

| Chiave | Descrizione |
|--------|-------------|
| `IntesiPrinterManagerUrl` | Base URL API (come parametro DB omonimo) |
| `IntesiPrinterManagerPrinter` | Stampante default PM |
| `PrinterManagerEndpointPath` | Path relativo opzionale (es. `api/print`); vuoto = POST sulla root del base URL |
| `BaseUrl` | Servizio legacy Crystal |

## Parametri da database ERP

In produzione, `IntesiPrinterManagerUrl` e `IntesiPrinterManagerPrinter` andranno letti dalla tabella parametri del gestionale; oggi sono mappati da **`ConfigurationPrintParameterSource`** → `appsettings` (sostituibile con implementazione `IPrintParameterSource` che esegue query SQL).
