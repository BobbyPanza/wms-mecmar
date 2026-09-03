# CLAUDE.md — WMS Mecmar

Progetto WMS custom per cliente **Mecmar** (ref. Stefano Marcolongo).
Stack: **Blazor Web App (.NET 10) + MudBlazor v9 + SQL Server**.
Repo separato da HAMMErp — non mescolare codice o dipendenze.

---

## Documenti di riferimento

| File | Contenuto |
|------|-----------|
| [docs/WMS.md](docs/WMS.md) | **Requisiti completi del sistema** — spec funzionali, flussi operativi, regole di business |
| [docs/FactoryMecmar_ErpReference.md](docs/FactoryMecmar_ErpReference.md) | Schema tabelle ERP reali (A_PAR, A_LOC, L_MLPA, S_MOV, A_CMM, A_OPR, A_MAG) con campioni di dati |
| [docs/ErpQuery_Checklist.md](docs/ErpQuery_Checklist.md) | Checklist query/viste/SP necessarie al WMS — da completare man mano |
| [docs/ErpLogin_A_OPR.example.sql](docs/ErpLogin_A_OPR.example.sql) | Query SQL di esempio per autenticazione operatore via A_OPR |
| [docs/IntesiPrinterManager.md](docs/IntesiPrinterManager.md) | Integrazione Intesi Printer Manager (payload, routing, Crystal Reports) |

---

## Database

### ERP — `FactoryMecmar` (sola lettura + SP)
- Connection string: `ConnectionStrings:ErpDatabase`
- **Mai scrivere direttamente su `S_MOV`** — usare sempre `TRD_InsertMov`
- Firma `TRD_InsertMov` (tutti opzionali tranne i primi 3):
  `@sPACOD, @sCMCOD, @fMOQTV` (qty **positiva**, segno da CMTYP)`, @sMGCOD='', @sLCCOD='', @iIDTBR=NULL, @iIDRIF=NULL, @iMoveRect=0, @nodeID=0, @operatorCode=NULL, @referenceCode=NULL, @movementDate=NULL, @sCOCOD=NULL, @sOLCOD=NULL, @iIDCRN=NULL, @iPLCOD=NULL, @fPrice=0, @PackageId=NULL, @PackageCode=NULL, @IDPAR=NULL`
- Ritorna IDMOV (int) o -1 se CMTYP non è ±1 (causale non valida, nessuna scrittura)
- MGCOD/LCCOD='' → usa locazione principale articolo (LCPRC='Y' in L_MLPA)
- `MOSTP` in S_MOV = @movementDate o CURRENT_TIMESTAMP — **DTDOC non viene scritto** dalla SP
- Causali: `SMI+CMI` (spostamento: 2 chiamate), `SCAR` (prelievo produzione), `SINV/CINV` (inventario), `REP/REN` (rettifiche)
- Colonna famiglia articoli: `FMCOD` (non `PAFAM`) — vedi FactoryMecmar_ErpReference.md
- Colonne custom aggiunte dal WMS (prefisso `X_`): `L_MLPA.X_VerifiedUser` / `X_VerifiedDate` — verifica abbinamento articolo/locazione in rettifica (vedi `sql/V010`)
- `L_MLPA.LASTUPDATE` è gestito da `TRG_ON_UPDATE_MLPA` e cambia **solo** se cambiano QTLOC/QTMAX/QTMIN — non usarlo come "ultima volta che qualcuno ha guardato la riga"

### Logic — DB WMS proprio (lettura/scrittura)
- Connection string: `ConnectionStrings:LogicDatabase` → `Server=localhost;Database=Logic;…`
- Servizio: `LogicService` (singleton)
- **`WMS_Cart`** — carrello persistente per operatore (PK = Guid, chiave OperatorCode)
  - Righe senza destinazione = carrello in accumulo; con destinazione = pronte per evasione
  - I movimenti vengono eseguiti tutti in un colpo (`ConfirmEvadi`) → riga rimossa al successo
- Tabelle stampa da creare: `WMS_PrintTemplate` + `WMS_PrintTemplateParam`

### Login operatori
- Tabella `A_OPR` su FactoryMecmar: `OPCOD, OPDSC, OPPSW, GRCOD`
- Per ora: mock in `MockWmsService.Operators` — da sostituire con query reale

---

## Architettura

```
Components/
  App.razor           — HTML shell, base href, MudBlazor CSS/JS
  Routes.razor        — @rendermode InteractiveServer (GLOBALE — non su MainLayout)
  Layout/
    MainLayout.razor  — MudThemeProvider dark slate, AppBar, redirect login
  Pages/              — una pagina per transazione
  Shared/             — BarcodeInput, StockBadge, WmsTile, ArticleInfoButton, ArticleInfoDialog
Services/
  SessionService.cs   — Scoped (per circuito): operatore loggato, carrello, titolo pagina
  MockWmsService.cs   — Singleton: dati mock condivisi tra tutti i circuiti
Models/
  WmsModels.cs        — tutti i DTO e record
wwwroot/
  app.css             — CSS mobile-first con variabili :root (--wms-primary, --wms-surface, ecc.)
```

### Regole render mode
- `@rendermode InteractiveServer` **solo su `Routes.razor`** — si propaga a tutto
- **Non metterlo su `MainLayout`** (riceve `Body` RenderFragment → errore serializzazione)
- Non metterlo sulle singole pagine (ridondante)

---

## UI — MudBlazor v9

- Tema dark slate navy — palette in `MainLayout._theme.PaletteDark`, variabili CSS in `app.css :root`
- `MudTabs`: non usare `PanelClass` (MUD0002)
- `MudExpansionPanel`: usare `IsExpanded="x" IsExpandedChanged="v => x = v"` (non `@bind-IsExpanded`)
- `MudDialog` service-based: cascading parameter `IMudDialogInstance` (non `MudDialogInstance`)
- Parametri componenti custom: non chiamarli `Color` (confligge con enum MudBlazor) — es. `TileColor`
- Tuple C# sono value type: non usare `?.` — controllare `== default`

---

## Hosting

- **IIS**, no `UseHttpsRedirection` nel codice (IIS gestisce terminazione SSL)
- Publish: `C:\intesi\WS\WMS`
- Per publish con IIS attivo: creare `app_offline.htm` → publish → rimuovere
- Preferire **sito root dedicato** (es. porta 8099) — `localhost/wms` come sub-app rompe SignalR

---

## Transazioni

| Pagina | Route | Stato |
|--------|-------|-------|
| Login | `/login` | ✅ mock |
| Home | `/home` | ✅ mock |
| Interrogazione unificata | `/query` | ✅ ERP |
| Interrogazione Articolo | `/query/article` | ✅ mock (assorbita da unificata) |
| Interrogazione Locazione | `/query/location` | ✅ mock (assorbita da unificata) |
| Spostamento Semplice | `/move/simple` | ✅ ERP (TRD_InsertMov SMI+CMI) |
| Spostamento Carrello | `/move/cart` | ✅ ERP + Logic DB |
| Prelievo Produzione | `/pick/production` | ✅ mock |
| Prelievo da Lista | `/pick/list` | ✅ mock (tabelle ERP da definire) |
| Accettazione | `/acceptance` | ✅ mock (tabelle ERP da definire) |
| Inventario | `/inventory` | ✅ ERP + Logic DB |
| Rettifiche semplici | `/adjustments` | ✅ ERP (TRD_InsertMov REP/REN) |

---

## Stampa

Vedi [docs/IntesiPrinterManager.md](docs/IntesiPrinterManager.md).
- Flusso: leggi `WMS_PrintTemplate` per contesto → auto-fill parametri → form parametri manuali → chiama Printer Manager
- Routing automatico in `PrintService`: PM se `IntesiPrinterManagerUrl` valorizzato e report non è `.rpt`; altrimenti legacy Crystal (`BaseUrl`)
- Bottone stampa in AppBar — visibile solo se pagina ha contesto attivo (`Session.PrintContext ≠ ""`)
- Contesti attivi: ARTICLE, LOCATION, MOVE, CART, INVENTORY, ACCEPTANCE, ADJUSTMENT, PICK
- AutoFill PICK: `LISTCODE` (codice lista), `LISTID` (GUID lista)
- Config: sezione `PrintService` in appsettings (IntesiPrinterManagerUrl, IntesiPrinterManagerPrinter, PrinterManagerEndpointPath, BaseUrl)
- Tabelle Logic: `WMS_PrintTemplate` (Id, Context, Name, ReportName, PrinterName, IsActive) + `WMS_PrintTemplateParam` (TemplateId, ParamName, AutoFillKey, Label, IsRequired, SortOrder)
- `AutoFillKey` nei parametri: chiave del dizionario `Session.PrintAutoFill` compilato dalla pagina (es. "PACOD", "LCCOD")

---

## Regole

- Non modificare file di HAMMErp da questo progetto
- Non committare password — usare `appsettings.Development.json` (gitignored) o user secrets
- `MockWmsService` è singleton — stato mutable condiviso tra sessioni (intenzionale per demo)
- `SessionService` è scoped (per utente) — non iniettarlo come singleton
