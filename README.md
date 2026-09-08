# WMS Mecmar — v1.2.0

WMS mobile per magazzino, sviluppato per il cliente **Mecmar** (ref. Stefano Marcolongo).

Stack: **Blazor Server / .NET 10 · MudBlazor v9 · SQL Server (Dapper)**  
Hosting: **IIS** — terminazione HTTPS esterna, no `UseHttpsRedirection`

---

## Prerequisiti

- .NET 10 SDK
- SQL Server con database ERP Intesi (es. `FactoryMecmar`) e database `Logic` (WMS)
- IIS con modulo ASP.NET Core Hosting Bundle

---

## Setup

### 1. Script SQL su ERP (FactoryMecmar o nome effettivo)

Applicare in ordine (tutti idempotenti con `CREATE OR ALTER`):

```bash
sqlcmd -S <server> -d <ErpDb> -i sql/V001__WMS_V_PickList.sql
sqlcmd -S <server> -d <ErpDb> -i sql/V002__WMS_Pick_SPs.sql
sqlcmd -S <server> -d <ErpDb> -i sql/V003__WMS_V_AcceptanceDocs.sql
sqlcmd -S <server> -d <ErpDb> -i sql/V004__WMS_V_ArticleDocuments.sql
sqlcmd -S <server> -d <ErpDb> -i sql/V005__WMS_V_ArticleOrders_Engaged.sql
sqlcmd -S <server> -d <ErpDb> -i sql/V006__WMS_Versamento_SP.sql
```

### 2. Script SQL su Logic DB (WMS)

```bash
sqlcmd -S <server> -d Logic -i sql/M001__PickList_Improvements.sql
sqlcmd -S <server> -d Logic -i sql/M002__PickListRow_Paf02.sql
sqlcmd -S <server> -d Logic -i sql/M003__PickListRow_Paf02_Resize.sql
```

Per `V007__WMS_V_PickListBolleDetail.sql` (TVF per Crystal Reports liste prelievo): aprire il file, sostituire `[FactoryMecmar]` con il nome reale del DB ERP, poi applicare su Logic DB.

Lo schema WMS_* su Logic DB (tabelle, indici, template stampa) viene creato automaticamente all'avvio dall'app tramite `LogicService.EnsureSchemaAsync`.

### 3. Configurazione app

Copiare `appsettings.template.json` in `appsettings.json` e valorizzare:

```json
{
  "ConnectionStrings": {
    "ErpDatabase":   "Server=...;Database=FactoryMecmar;...",
    "LogicDatabase": "Server=...;Database=Logic;..."
  },
  "WmsOptions": {
    "AllowedWarehouses": []
  },
  "PrintService": {
    "IntesiPrinterManagerUrl":     "",
    "IntesiPrinterManagerPrinter": "",
    "PrinterManagerEndpointPath":  "",
    "BaseUrl":                     ""
  },
  "Acceptance": {
    "DocumentTypes":      ["RLA", "RFL", "DCF"],
    "PendingStatuses":    [200, 201],
    "AcceptedStatus":     -4,
    "LocationFlagColumn": "Acceptance",
    "LoadCausal":         "CMI"
  }
}
```

`appsettings.json` è in `.gitignore` — non committare password.

### 4. Publish su IIS

```powershell
# Ferma l'app
New-Item C:\intesi\WS\WMS\app_offline.htm -Force

# Pubblica
dotnet publish WMS.csproj -c Release -o C:\intesi\WS\WMS

# Riavvia
Remove-Item C:\intesi\WS\WMS\app_offline.htm
```

IIS: usare **sito root dedicato** (es. porta 8099) — montare come sub-app rompe SignalR.

---

## Architettura

```
Components/
  App.razor               HTML shell, base href, MudBlazor CSS/JS
  Routes.razor            @rendermode InteractiveServer (globale)
  Layout/
    MainLayout.razor      Tema dark slate navy, AppBar con bottone stampa,
                          redirect login, init nodo dispositivo (A_NOD)
    AdminLayout.razor     Layout area amministrazione
  Pages/                  Una pagina per transazione
  Pages/Admin/            Pannello admin (inventario sessioni, liste, accettazione)
  Shared/                 BarcodeInput, StockBadge, WmsTile,
                          ArticleInfoDialog, PickRowDialog, CloseListDialog
Services/
  SessionService.cs       Scoped — operatore loggato, NodeId, print context + AutoFill
  ErpService.cs           Singleton — lettura/scrittura ERP via Dapper
  LogicService.cs         Singleton — DB Logic (WMS_PickList, WMS_Cart, schema WMS)
  PickListService.cs      Singleton — orchestrazione liste prelievo (ERP + Logic)
  AcceptanceService.cs    Singleton — orchestrazione accettazione merci
  PrintService.cs         HttpClient — Intesi Printer Manager / Crystal Reports
Models/
  WmsModels.cs            DTO e record condivisi
sql/
  V001__WMS_V_PickList.sql          Vista lista prelievo (su ERP)
  V002__WMS_Pick_SPs.sql            SP pick session/line/versamento (su ERP)
  V003__WMS_V_AcceptanceDocs.sql    Viste accettazione DDT (su ERP)
  V004__WMS_V_ArticleDocuments.sql  Vista documenti articolo (su ERP)
  V005__WMS_V_ArticleOrders_Engaged.sql  Vista impegni per articolo (su ERP)
  V006__WMS_Versamento_SP.sql       SP versamento produzione (su ERP)
  V007__WMS_V_PickListBolleDetail.sql   TVF report Crystal Reports (su Logic)
  M001__PickList_Improvements.sql   Migrazione tabelle Logic
  M002/M003__PickListRow_Paf02.sql  Migrazione colonna Paf02 (Logic)
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
| Prelievo Produzione | `/production/pick` | WMS_V_PickList + SP batch | ✅ |
| Prelievo da Lista | `/pick/list` | ERP + Logic WMS_PickList | ✅ |
| Accettazione | `/acceptance` | ERP + Logic | ✅ |
| Inventario | `/inventory` | ERP + Logic | ✅ |
| Rettifiche semplici | `/adjustments` | TRD_InsertMov REP/REN | ✅ |
| Gestione Locazioni | `/locations/manage` | ERP A_LOC + L_MLPA | ✅ |
| Configurazione stampa | `/config` | Logic WMS_PrintTemplate | ✅ |
| Admin (inventari, liste, accettazioni, verifiche) | `/admin/*` | ERP + Logic | ✅ |

📖 **Documentazione completa** (operativa + tecnica, per Confluence): [docs/CONFLUENCE_WMS_Mecmar.md](docs/CONFLUENCE_WMS_Mecmar.md)

---

## Prelievo da Lista

Le liste sono persistite su Logic DB (`WMS_PickList` + `WMS_PickListRow`).

**Flusso:**
1. Operatore carica una bolla (`WMS_V_PickList` su ERP) → righe salvate su Logic
2. Ogni prelievo fisico viene *staged* su `WMS_PickListPick` (`ExecutedAt` NULL = staged; resiliente a cali WiFi)
3. Conferma batch: per ogni bolla apre sessione ERP, registra via `WMS_InsertPickLine`, chiude
4. Opzionale: versamento produzione tramite `WMS_InsertVersamentoLine`

**Filtro liste:** ogni operatore vede solo le liste assegnate a sé (toggle "Solo assegnate a me"). Alla creazione la lista viene auto-assegnata al creatore.

**Stampa:** contesto `PICK` con AutoFill `LISTCODE` (codice lista) e `LISTID` (GUID). Report Crystal via `WMS_FN_PickListBolleDetail` — elenca gli articoli ancora da prelevare dalle bolle della lista con riferimento commessa.

---

## Prelievo Produzione — flusso dati

```
WMS_OpenPickSession  (@OLCOD, @OPCOD, @NOCOD)  →  @IDSES   [S_SES SESTO=1]
  WMS_InsertPickLine (@OLCOD, @PACOD, …, @IDSES)  →  @IDMOV   ← per ogni articolo
    ├── S_PAP  upsert  (riga prelievo bolla+articolo)
    ├── S_SPP  insert  (evento prelievo fisico, TPREC=1)
    └── TRD_InsertMov SCAR  IDTBR=5  IDRIF=IDSPP
WMS_ClosePickSession (@IDSES)                   [S_SES SESTO=2]
```

Il trigger `TRG_ON_INSERT_SPP` aggiorna automaticamente `S_PAP.PAQTP`.

**Nodo dispositivo:** ogni terminale si registra in `A_NOD` con `PRDCD=10006`; il `CDNOD` viene salvato in `localStorage` e ricaricato ad ogni sessione.

---

## Stampa

Vedi [docs/IntesiPrinterManager.md](docs/IntesiPrinterManager.md).

- Bottone stampa in AppBar — visibile solo se la pagina ha un contesto attivo (`Session.PrintContext`)
- Routing automatico: Intesi Printer Manager se `IntesiPrinterManagerUrl` valorizzato e report non è `.rpt`; altrimenti Crystal Reports legacy (`BaseUrl`)
- Tabelle Logic: `WMS_PrintTemplate` + `WMS_PrintTemplateParam`
- Contesti attivi: `ARTICLE`, `LOCATION`, `MOVE`, `CART`, `INVENTORY`, `ACCEPTANCE`, `ADJUSTMENT`, `PICK`

---

## Note sviluppo

- `@rendermode InteractiveServer` **solo su `Routes.razor`** — non su `MainLayout` né sulle pagine
- `MudExpansionPanel`: usare `IsExpanded` + `IsExpandedChanged` (non `@bind-IsExpanded`)
- `MudSwitch` con handler custom: usare `Value` + `ValueChanged` (non `@bind-Value`) per evitare RZ10010
- Tuple C# sono value type: non usare `?.` — controllare `== default`
- Non scrivere mai direttamente su `S_MOV` — usare `TRD_InsertMov`
- Non creare oggetti su DB ERP dall'app in automatico — applicare manualmente gli script `sql/V*`
- Il nome del DB ERP non è fisso: non hardcodare `[FactoryMecmar]` nel codice o negli script condivisi
