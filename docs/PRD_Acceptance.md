# PRD — Accettazione Merci

**Versione:** 1.0  
**Data:** 2026-05-20  
**Riferimento:** WMS Mecmar, pagina `/acceptance`  
**Stato attuale:** scaffold mock in `Components/Pages/Acceptance.razor`

---

## Problem Statement

L'operatore di magazzino deve ricevere la merce in ingresso (DDT da fornitori e rientri conto lavoro) in modo tracciabile: verificare le quantità arrivate rispetto al documento ERP, assegnare la merce a locazioni fisiche, stampare etichette per ogni articolo accettato, e chiudere il documento segnalando eventuali discrepanze. Attualmente questa operazione avviene su carta o fuori dal WMS, senza tracciabilità digitale e senza integrazione con i movimenti di magazzino.

---

## Solution

Una pagina `/acceptance` completamente integrata con l'ERP che:

1. Elenca i DDT pendenti (tipi e stati configurabili) letti da `A_DOT`/`A_FOR`/righe ERP.
2. Permette di accettare riga per riga, confermando o correggendo le quantità e dichiarando la locazione di destinazione.
3. Supporta accettazioni parziali per riga (più versamenti su locazioni diverse).
4. Persiste le accettazioni parziali in tabelle WMS Logic (`WMS_AcceptanceLine`) con operatore e timestamp.
5. Registra il carico a magazzino via `TRD_InsertMov` (causale configurabile, default `CMI`).
6. Aggiorna lo stato del documento ERP (`A_DOT.DTSO`) al valore "accettato" configurabile quando tutte le righe sono completate.
7. Offre stampa etichetta per ogni articolo accettato tramite il `PrintService` esistente.

Il comportamento (tipi documento, stati, locazione default, causali) è interamente **configurabile via `appsettings.json`**, senza modifiche al codice.

---

## User Stories

### Lista documenti

1. Come operatore, voglio vedere la lista dei DDT da accettare appena entro nella pagina, così so subito cosa c'è in coda.
2. Come operatore, voglio che i documenti mostrino: tipo documento, numero/riferimento, data, ragione sociale fornitore (`FORAG`) e un indicatore di avanzamento (righe accettate / totale), così valuto il carico di lavoro a colpo d'occhio.
3. Come operatore, voglio poter scansionare il barcode di un DDT per aprirlo direttamente senza cercarlo nella lista.
4. Come operatore, voglio poter scansionare il barcode di un articolo e arrivare direttamente alla riga corrispondente nel documento che lo contiene, così risparmio tempo se conosco già l'articolo.
5. Come operatore, voglio che i documenti completamente accettati siano visivamente distinti (icona / colore) e, per default, nascosti dalla lista principale ma recuperabili con un toggle.
6. Come responsabile, voglio che la lista mostri solo i tipi documento configurati (es. `RLA`, `RFL`, `DCF`) e solo negli stati "da accettare" configurati (es. 200, 201), così non vedo documenti di altri flussi.

### Dettaglio documento

7. Come operatore, voglio vedere la testata del documento (tipo, numero, data, fornitore, eventuali note) nella schermata di dettaglio.
8. Come operatore, voglio vedere l'elenco delle righe del documento con: codice articolo, descrizione, UM, quantità attesa, quantità già accettata, residuo, stato riga (Pendente / Parziale / Completata).
9. Come operatore, voglio che le righe già completamente accettate siano visivamente marcate e non apribili per nuovi versamenti.
10. Come operatore, voglio poter scansionare il barcode di un articolo per aprire direttamente la sua riga dal dettaglio documento.
11. Come operatore, voglio vedere per ogni riga l'elenco dei versamenti già effettuati (locazione, quantità, operatore, orario), così so come è distribuita la merce già ricevuta.

### Accettazione riga

12. Come operatore, voglio che al momento di accettare un articolo mi venga proposta automaticamente la **locazione di accettazione di default** (quella contrassegnata con il flag su `A_LOC`), così non devo ricordarla a memoria.
13. Come operatore, voglio poter **cambiare la locazione** scansionando un barcode oppure selezionandola da una tendina filtrata, così posso distribuire la merce su più locazioni se una non basta.
14. Come operatore, voglio poter fare più versamenti parziali sulla stessa riga (locazioni diverse o stessa locazione in momenti diversi), così gestisco situazioni in cui la merce arriva su più sponde o non entra tutta in una locazione.
15. Come operatore, voglio che il campo quantità sia precompilato con il **residuo da accettare**, così ho meno da digitare nel caso normale in cui tutta la merce è arrivata.
16. Come operatore, voglio poter ridurre la quantità accettata rispetto al residuo per dichiarare una mancanza parziale, così il sistema registra lo scostamento.
17. Come operatore, voglio che la conferma di un versamento esegua immediatamente il carico a magazzino via `TRD_InsertMov` e mostri conferma visiva, così so che il movimento è avvenuto.
18. Come operatore, voglio un riepilogo chiaro (articolo, quantità, locazione, documento) prima di confermare, così evito errori.

### Stampa etichette

19. Come operatore, voglio poter stampare l'etichetta dell'articolo appena accettato con un pulsante nella schermata di accettazione riga, così l'etichetta è sempre disponibile senza uscire dal flusso.
20. Come operatore, voglio che venga proposta automaticamente la **stampante predefinita del mio terminale** (basata sul nodo dispositivo `A_NOD`), così non devo selezionarla ogni volta.
21. Come operatore, voglio poter cambiare la stampante al volo prima di stampare, così posso usare una stampante diversa se quella di default non è disponibile.
22. Come operatore, voglio che il **report di default** per l'articolo venga selezionato automaticamente (in base a logica configurabile: prefisso codice, famiglia, ecc.) e che io possa scegliere un report alternativo dalla lista, così le etichette sono sempre quelle giuste per tipo di materiale.
23. Come operatore, voglio che il sistema ricordi la mia ultima scelta di stampante per il terminale corrente (via `localStorage` come già fa il nodo), così non devo riselezionarla ad ogni sessione.

### Chiusura documento

24. Come operatore, voglio un pulsante "Completa documento" che diventa attivo solo quando tutte le righe sono accettate (anche con scostamenti dichiarati), così chiudo formalmente la ricezione.
25. Come operatore, voglio poter chiudere un documento anche se alcune righe hanno quantità inferiore all'atteso, dichiarando esplicitamente i mancanti, così il documento viene ugualmente chiuso con evidenza degli scostamenti.
26. Come operatore, voglio che alla chiusura il sistema aggiorni lo stato del documento ERP (`A_DOT.DTSO`) al valore "accettato" configurabile, così l'ERP riflette il nuovo stato.
27. Come operatore, voglio conferma visiva e sonora (snackbar) quando un documento è stato completato, così ho feedback immediato.
28. Come responsabile, voglio che la notifica di scostamento quantità (mancanti) sia gestita separatamente (fuori dal WMS), ma che il WMS persista i dati necessari per generarla.

### Configurabilità

29. Come amministratore, voglio configurare i tipi documento da mostrare (`Acceptance:DocumentTypes`, es. `["RLA","RFL","DCF"]`) in `appsettings.json` senza modificare il codice.
30. Come amministratore, voglio configurare i codici stato ERP "da accettare" (`Acceptance:PendingStatuses`, es. `[200,201]`) e lo stato "accettato" (`Acceptance:AcceptedStatus`, es. `-4`) in `appsettings.json`.
31. Come amministratore, voglio configurare il nome della colonna flag su `A_LOC` che identifica la locazione di accettazione di default (`Acceptance:LocationFlagColumn`), così se il campo cambia non serve ricompilare.
32. Come amministratore, voglio configurare la causale ERP usata per il carico all'accettazione (`Acceptance:LoadCausal`, default `CMI`) in `appsettings.json`.
33. Come amministratore, voglio configurare la logica di selezione del report di default per etichetta (es. mappatura famiglia → nome report, o prefisso codice → nome report) in `appsettings.json`, così posso personalizzare per articolo senza modificare il codice.

---

## Implementation Decisions

### Moduli da costruire / modificare

#### 1. `AcceptanceOptions` (nuovo — configurazione)
Record POCO con binding diretto da `appsettings.json`, sezione `Acceptance`:

- `DocumentTypes: string[]` — tipi DTDO da mostrare
- `PendingStatuses: int[]` — stati DTSO da mostrare nella lista
- `AcceptedStatus: int` — valore DTSO da scrivere a chiusura
- `LocationFlagColumn: string` — nome colonna flag su `A_LOC`
- `LoadCausal: string` — causale TRD_InsertMov per carico (default `CMI`)
- `DefaultReportRules: List<ReportRule>` — regole per report default (campo, pattern, reportName)

Registrato come `IOptions<AcceptanceOptions>` in `Program.cs`.

#### 2. Tabella Logic DB — `WMS_AcceptanceLine` (nuova)

Registrata e creata da `LogicService.EnsureSchemaAsync` come le altre tabelle:

| Colonna | Tipo | Note |
|---------|------|------|
| `Id` | `uniqueidentifier` PK | GUID riga |
| `ErpDocId` | `int` | ID del documento ERP (`A_DOT.IDDDOT`) |
| `ErpLineId` | `int` | ID riga ERP |
| `ArticleCode` | `varchar(20)` | PACOD |
| `WarehouseCode` | `varchar(15)` | MGCOD locazione destinazione |
| `LocationCode` | `varchar(15)` | LCCOD locazione destinazione |
| `AcceptedQty` | `numeric(18,6)` | Quantità accettata in questo versamento |
| `ExpectedQty` | `numeric(18,6)` | Copia della qty attesa sulla riga ERP |
| `OperatorCode` | `char(15)` | OPCOD |
| `ErpMovId` | `int` nullable | IDMOV restituito da TRD_InsertMov |
| `AcceptedAt` | `datetime2` | Timestamp versamento |
| `DocumentRef` | `varchar(40)` | Numero DDT (per display rapido) |
| `Notes` | `nvarchar(200)` nullable | Note libere operatore |

#### 3. `ErpService` — nuovi metodi (estensione)

- `GetAcceptanceDocsAsync(AcceptanceOptions)` → `List<AcceptanceDocDto>`: query su `A_DOT JOIN A_FOR` filtrata per `DTDO` e `DTSO`.
- `GetAcceptanceLinesAsync(int erpDocId)` → `List<AcceptanceLineErpDto>`: righe del documento (tabella da mappare con cliente, probabilmente `A_DOR`).
- `GetAcceptanceLocationsAsync()` → `List<LocationDto>`: locazioni con flag accettazione attivo (query `A_LOC WHERE {flagColumn} = 1`).
- `CloseAcceptanceDocAsync(int erpDocId, int acceptedStatus)` → `bool`: `UPDATE A_DOT SET DTSO = @status WHERE IDDDOT = @id`.
- `ExecuteAcceptanceLoadAsync(...)` → `(bool ok, int idMov)`: wrapper `TRD_InsertMov` con causale configurabile.

#### 4. `LogicService` — nuovi metodi (estensione)

- `GetAcceptanceLinesAsync(int erpDocId)` → `List<WmsAcceptanceLine>`: righe versamento già registrate per un documento.
- `InsertAcceptanceLineAsync(WmsAcceptanceLine)` → `Guid`: insert singola riga.

#### 5. `AcceptanceService` (nuovo — deep module)

Orchestrazione completa, testabile in isolamento:

- `LoadDocsAsync()` → lista documenti pendenti con avanzamento calcolato
- `LoadDocDetailAsync(int erpDocId)` → testata + righe ERP + versamenti WMS già effettuati
- `AcceptLineAsync(AcceptLineRequest)` → chiama ERP `TRD_InsertMov` + insert Logic + aggiorna stato riga in memoria
- `CompleteDocAsync(int erpDocId)` → valida completezza + `UPDATE A_DOT.DTSO` + snackbar
- `GetDefaultLocationAsync()` → query `A_LOC` con flag configurabile
- `GetDefaultReportAsync(string articleCode, string? family)` → applica `DefaultReportRules` configurabili

#### 6. Modelli WmsModels.cs — estensioni

- Estendere `AcceptanceDocDto` con: `int ErpDocId`, `string DocType`, `int ErpStatus`
- Estendere `AcceptanceItemDto` con: `int ErpLineId`, `int ErpDocId`, `List<AcceptanceLineRecord> Versamenti`
- Aggiungere `AcceptanceLineRecord` (record immutabile per display storico versamenti)
- Aggiungere `AcceptLineRequest` (DTO input per `AcceptanceService.AcceptLineAsync`)

#### 7. `Acceptance.razor` — migrazione mock → reale

- Iniettare `AcceptanceService` al posto di `MockWmsService`
- Aggiungere pannello "storico versamenti" per riga (expand/collapse)
- Aggiungere selezione locazione da tendina (oltre a scan)
- Aggiungere pulsante "Completa documento" con conferma
- Aggiungere pulsante stampa etichetta post-accettazione con picker stampante
- Mantenere il flusso a 3 pannelli esistente (lista doc → righe doc → accettazione riga)

### Schema tabelle ERP da chiarire con cliente

- Nome tabella righe DDT (probabilmente `A_DOR`) — colonne: IDDDOT (FK), IDPRO (riga), PACOD, PADSC, PAUDM, qty attesa
- Nome colonna flag locazione accettazione in `A_LOC`
- Eventuali note/testo libero su `A_DOT`

### Flusso dati accettazione riga

```
Operatore conferma versamento
  → AcceptanceService.AcceptLineAsync
      → ErpService.ExecuteAcceptanceLoadAsync (TRD_InsertMov causale CMI, IDFOR come SCOCOD)
          → IDMOV
      → LogicService.InsertAcceptanceLineAsync (persiste con IDMOV)
      → Aggiorna stato riga in memoria (Partial/Done)
      → Se tutte le righe Done → abilita "Completa documento"
```

### Selezione stampante per terminale

Riusa il meccanismo `A_NOD` già presente (CDNOD salvato in `localStorage`). Aggiungere in `SessionService` o `PrintService` la preferenza stampante per nodo, persistita in `localStorage` del browser.

### Report default per articolo

`AcceptanceOptions.DefaultReportRules` è una lista ordinata di regole valutate in sequenza:

```json
"DefaultReportRules": [
  { "Field": "Family",      "Pattern": "Elettronica", "ReportName": "etichetta_elettronica.rpt" },
  { "Field": "ArticleCode", "Pattern": "^EL",         "ReportName": "etichetta_elettronica.rpt" },
  { "Field": "*",           "Pattern": "*",            "ReportName": "etichetta_default.rpt"    }
]
```

Prima regola che fa match vince (fallback `*`). Logica in `AcceptanceService.GetDefaultReportAsync`.

---

## Testing Decisions

### Principio

Testare solo comportamento osservabile (output di metodi pubblici, chiamate a interfacce esterne), non dettagli implementativi interni.

### Moduli da testare

| Modulo | Tipo test | Razionale |
|--------|-----------|-----------|
| `AcceptanceService` | Unit (mock di ErpService + LogicService) | È il cervello del flusso: calcola stato righe, applica regole, orchestra le chiamate |
| `AcceptanceOptions` binding | Integration (WebApplicationFactory) | Verificare che la configurazione sia letta correttamente da appsettings |
| `LogicService.InsertAcceptanceLineAsync` | Integration (DB test locale) | Schema + roundtrip dati |
| Report rule matching in `GetDefaultReportAsync` | Unit | Regole match con casi edge (nessuna regola, solo fallback, pattern regex) |

### Prior art

Seguire il pattern dei test già presenti per `InventoryService`/`ErpService` (se esistenti) — altrimenti usare `xUnit` + `Moq` come stack già in uso nel progetto.

---

## Out of Scope

- **Notifiche scostamento quantità** — gestite esternamente (l'utente se ne occupa). Il WMS persiste i dati (qty attesa vs accettata) in `WMS_AcceptanceLine` come fonte di verità.
- **Creazione DDT da zero nel WMS** — i documenti arrivano sempre dall'ERP.
- **Gestione resi / note credito** — flusso separato da definire.
- **Integrazione con ordini di acquisto (`A_OFA`)** — possibile evoluzione futura.
- **Multi-magazzino** — per ora si assume che tutte le accettazioni vadano su un unico magazzino (quello della locazione di default); da estendere se necessario.
- **Blocco concorrenza** (due operatori sullo stesso DDT) — da valutare se necessario in produzione.

---

## Further Notes

- Il campo `DTSO` su `A_DOT` è lo stesso usato anche per altri stati del documento (non solo accettazione): la scrittura diretta `UPDATE A_DOT SET DTSO = @val` è sufficiente se non esiste una SP dedicata nel gestionale. Da verificare con Mecmar se esiste `TRD_CloseAcceptanceDoc` o simile.
- La causale `CMI` (carico da spostamento) è quella già usata nel carrello; se Mecmar vuole una causale dedicata per gli acquisti (es. `CAR` o `CARACQ`) va configurata in `Acceptance:LoadCausal`.
- La colonna flag su `A_LOC` (locazione di default accettazione) è **TBD**: da concordare con Mecmar il nome esatto prima di implementare `GetAcceptanceLocationsAsync`.
- La tabella righe DDT (`A_DOR` o altro nome) e le sue colonne sono **TBD**: blocca le query ERP finché non confermato.
- Il meccanismo di preferenza stampante per terminale è un'estensione leggera del sistema `A_NOD` esistente; non richiede nuove tabelle, solo `localStorage`.
