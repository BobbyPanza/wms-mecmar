# WMS Mecmar

WMS mobile per magazzino, sviluppato per il cliente **Mecmar** (ref. Stefano Marcolongo).

Stack: **Blazor Server / .NET 10 · MudBlazor v9 · SQL Server (Dapper)**  
Hosting: **IIS** — terminazione HTTPS esterna, no `UseHttpsRedirection`

---

## Prerequisiti

- .NET 10 SDK
- SQL Server con database `FactoryMecmar` (ERP Intesi) e database `Logic` (WMS)
- IIS con modulo ASP.NET Core Hosting Bundle

---

## Setup

### 1. Script SQL su FactoryMecmar

Applicare in ordine (una volta sola — tutti idempotenti con `CREATE OR ALTER`):

```bash
sqlcmd -S <server> -d FactoryMecmar -U <user> -P <pwd> -i sql/V001__WMS_V_PickList.sql
sqlcmd -S <server> -d FactoryMecmar -U <user> -P <pwd> -i sql/V002__WMS_Pick_SPs.sql
```

Vedi [sql/README.md](sql/README.md) per i dettagli.

### 2. Configurazione app

Copiare `appsettings.template.json` in `appsettings.json` e valorizzare:

```json
{
  "ConnectionStrings": {
    "ErpDatabase":   "Server=...;Database=FactoryMecmar;...",
    "LogicDatabase": "Server=...;Database=Logic;..."
  },
  "PrintService": {
    "IntesiPrinterManagerUrl":     "",
    "IntesiPrinterManagerPrinter": "",
    "PrinterManagerEndpointPath":  "",
    "BaseUrl":                     ""
  }
}
```

`appsettings.json` è in `.gitignore` — non committare password.

### 3. Publish su IIS

```bash
# Ferma l'app (crea app_offline.htm nella cartella publish)
echo. > C:\intesi\WS\WMS\app_offline.htm

# Pubblica
dotnet publish -c Release -o C:\intesi\WS\WMS

# Riavvia (rimuovi app_offline.htm)
del C:\intesi\WS\WMS\app_offline.htm
```

IIS: preferire **sito root dedicato** (es. porta 8099) — `localhost/wms` come sub-app rompe SignalR.

---

## Architettura

```
Components/
  App.razor               HTML shell, base href, MudBlazor CSS/JS
  Routes.razor            @rendermode InteractiveServer (globale)
  Layout/
    MainLayout.razor      Tema dark slate navy, AppBar, redirect login,
                          init nodo dispositivo (A_NOD)
  Pages/                  Una pagina per transazione
  Shared/                 BarcodeInput, StockBadge, WmsTile,
                          ArticleInfoButton, ArticleInfoDialog
Services/
  SessionService.cs       Scoped — operatore loggato, NodeId, print context
  ErpService.cs           Singleton — lettura/scrittura ERP via Dapper
  LogicService.cs         Singleton — DB Logic (WMS_Cart, schema WMS)
  MockWmsService.cs       Singleton — dati demo condivisi tra sessioni
  PrintService.cs         HttpClient — Intesi Printer Manager / Crystal
Models/
  WmsModels.cs            DTO e record condivisi
sql/
  V001__WMS_V_PickList.sql
  V002__WMS_Pick_SPs.sql
docs/                     Requisiti, reference ERP, checklist query
```

---

## Transazioni

| Pagina | Route | DB | Stato |
|--------|-------|----|-------|
| Login | `/login` | A_OPR (ERP) | ✅ |
| Home | `/home` | — | ✅ |
| Interrogazione unificata | `/query` | ERP | ✅ |
| Spostamento semplice | `/move/simple` | TRD_InsertMov SMI+CMI | ✅ |
| Spostamento carrello | `/move/cart` | ERP + Logic WMS_Cart | ✅ |
| Prelievo Produzione | `/production/pick` | WMS_V_PickList + batch SP | ✅ |
| Prelievo da lista | `/pick/list` | — | mock |
| Accettazione | `/acceptance` | — | mock |
| Inventario | `/inventory` | ERP + Logic | ✅ |
| Rettifiche semplici | `/adjustments` | TRD_InsertMov REP/REN | ✅ |

---

## Prelievo Produzione — flusso dati

La pagina accumula articoli in un carrello locale, poi registra tutto in una conferma unica.

**Lettura lista:** `WMS_V_PickList` (vista su `L_ODLA → A_LOT → L_CMFE` per parti esterne, `A_LOT` self-join per lotti interni).

**Scrittura batch:**

```
WMS_OpenPickSession  (@OLCOD, @OPCOD, @NOCOD)  →  @IDSES   [S_SES SESTO=1]
  WMS_InsertPickLine (@OLCOD, @PACOD, …, @IDSES)  →  @IDMOV   ← per ogni articolo
    ├── S_PAP  upsert  (riga prelievo bolla+articolo)
    ├── S_SPP  insert  (evento prelievo fisico, TPREC=1)
    └── TRD_InsertMov SCAR  IDTBR=5  IDRIF=IDSPP
WMS_ClosePickSession (@IDSES)                   [S_SES SESTO=2]
```

Il trigger `TRG_ON_INSERT_SPP` aggiorna automaticamente `S_PAP.PAQTP` (qtà prelevata cumulata).

**Nodo dispositivo:** ogni terminale si registra in `A_NOD` con `PRDCD=10006`; il `CDNOD` viene salvato in `localStorage` e ricaricato ad ogni sessione.

---

## Stampa

Vedi [docs/IntesiPrinterManager.md](docs/IntesiPrinterManager.md).

- Bottone stampa in AppBar — visibile solo se la pagina ha un contesto attivo (`Session.PrintContext`)
- Routing automatico: Intesi Printer Manager se `IntesiPrinterManagerUrl` valorizzato e report non è `.rpt`; altrimenti Crystal Reports legacy (`BaseUrl`)
- Tabelle Logic: `WMS_PrintTemplate` + `WMS_PrintTemplateParam`

---

## Note sviluppo

- `@rendermode InteractiveServer` **solo su `Routes.razor`** — non su `MainLayout` né sulle pagine
- `MudExpansionPanel`: usare `IsExpanded` + `IsExpandedChanged` (non `@bind-IsExpanded`) — genera solo warning MUD0002, non errori
- Tuple C# sono value type: non usare `?.` — controllare `== default`
- Progressivi ERP: usare sempre `GetProgressivo 'dbo.S_XXX'` — **mai `MAX()`**
- Non scrivere mai direttamente su `S_MOV` — usare `TRD_InsertMov`
