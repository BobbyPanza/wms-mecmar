# PRD — Liste di Prelievo (Refactoring) e Gestione Locazioni

**Progetto:** WMS Mecmar  
**Data:** 2026-05-16  
**Autore:** Roberto Russo — Intesi S.r.l.  
**Stato:** Da triagere

---

## Problem Statement

### Liste di Prelievo

L'operatore di magazzino deve evadere liste di prelievo contenenti articoli da ritirare dal magazzino per alimentare la produzione o altri flussi. La pagina attuale (`/pick/list`) è interamente mock e non si connette ai dati reali. Mancano funzionalità critiche per l'uso quotidiano: ordinamento flessibile, dichiarazione mancanti, aggiunta di righe extra, dialogo di prelievo contestuale con giacenza in tempo reale, e soprattutto un modello di esecuzione staged (accumula tutti i pick → li esegui in blocco) che riduca errori e permetta di tornare indietro prima di scrivere sull'ERP.

Il modello attuale è progettato solo per liste da singola bolla. Il nuovo modello deve supportare sin dall'inizio anche liste multi-bolla (più ordini di produzione nella stessa lista di prelievo), lasciando aperta la strada al zone picking orchestrato dall'ufficio.

### Gestione Locazioni

Quando un nuovo scaffale o cassetto viene aggiunto al magazzino, il magazziniere non può operare dal terminale: deve attendere che l'ufficio IT crei la locazione nell'ERP desktop e abbini gli articoli. Non esiste nel WMS mobile un percorso rapido per: (a) creare una nuova locazione, (b) aggiungerla agli articoli che vi verranno posizionati, (c) impostare la locazione come principale per un articolo.

---

## Solution

### Liste di Prelievo

Progettare una struttura dati WMS (Logic DB) con testata/righe/movimenti staged. Ogni riga è collegata a una bolla di produzione (`OLCOD`). I prelievi vengono accumulati nel dialogo articolo e scritti sull'ERP solo quando l'operatore preme "Fai prelievi". L'ordinamento della lista è configurabile tramite una select in testata con criteri singoli (multi-livello previsto in futuro). Il modello è estendibile al zone picking gestito da ufficio.

### Gestione Locazioni

Creare una nuova pagina `/locations/manage` con due flussi: (1) crea locazione, (2) abbina articolo a locazione con flag principale.

---

## User Stories

### Liste di Prelievo

1. Come operatore, voglio creare una nuova lista di prelievo a partire da una bolla di produzione (`OLCOD`), così che il sistema la popoli automaticamente con tutti gli articoli da prelevare, un ID lista e il riferimento alla bolla.
2. Come operatore, voglio vedere l'elenco delle liste di prelievo disponibili con stato (Aperta / In corso / Completata) e percentuale di avanzamento, così da scegliere su quale lavorare.
3. Come operatore, voglio aprire una lista di prelievo e trovare un elenco di righe con codice articolo, quantità da prelevare, quantità già prelevata e locazione principale, così da sapere cosa devo fare.
4. Come operatore, voglio ordinare le righe della lista tramite una select in testata scegliendo tra: Codice Articolo, Tipo Handling, IDSpec (SpecsPart), IDGroup (GroupPart), Locazione Principale — così da ottimizzare il percorso fisico in magazzino.
5. Come operatore, voglio selezionare un articolo dalla lista cliccandoci sopra oppure scansionando il suo barcode nel campo in alto alla lista, così da aprire il dialogo di prelievo senza scorrere.
6. Come operatore, nel dialogo articolo voglio vedere: quantità da prelevare, quantità già prelevata, l'elenco dei prelievi già effettuati per questa riga (con locazione e quantità, in un pannello scorrevole), le locazioni dove ho giacenza disponibile, così da avere tutte le informazioni prima di prelevare.
7. Come operatore, nel dialogo articolo voglio scansionare la locazione di prelievo tramite un campo barcode con focus automatico, inserire la quantità e aggiungere il prelievo alla lista staged, così da preparare i movimenti senza ancora scriverli sull'ERP.
8. Come operatore, nel dialogo articolo voglio premere "Prelievo auto" per aggiungere automaticamente un prelievo dell'intera quantità residua dalla locazione principale, così da velocizzare i casi standard.
9. Come operatore, nel dialogo articolo voglio premere "Segnala mancante" per marcarlo come mancante, così che l'articolo vada in fondo alla lista evidenziato in rosso e il "Preleva tutto" lo ignori.
10. Come operatore, voglio premere "Preleva tutto" per aggiungere automaticamente prelievi dalla locazione principale per tutti gli articoli non mancanti e con giacenza sufficiente, così da preparare in un colpo solo una lista standard senza anomalie.
11. Come operatore, voglio che durante la fase di preparazione le righe mostrino: verde = totalmente coperto, giallo = parzialmente coperto, rosso = mancante dichiarato, bianco/grigio = non ancora gestito — così da vedere a colpo d'occhio lo stato di avanzamento.
12. Come operatore, voglio premere "Fai prelievi" per eseguire in blocco tutti i movimenti staged, così che il sistema scriva sull'ERP in un'unica operazione e aggiorni le giacenze.
13. Come operatore, dopo l'esecuzione voglio che le righe tornino al colore neutro mostrando solo le quantità residue (quelle non coperte da prelievi o mancanti), così da vedere cosa rimane ancora da fare.
14. Come operatore, voglio che per ogni riga con bolla associata l'esecuzione usi la SP corretta (`WMS_InsertPickLine` con il relativo `OLCOD`), così che i movimenti siano correttamente tracciati nel gestionale.
15. Come operatore, voglio abilitare uno switch "Mostra già prelevati" per vedere le righe completamente evase (default OFF), così da avere una lista compatta durante il lavoro.
16. Come operatore, voglio premere "Aggiungi riga" per aggiungere un articolo alla lista che non era previsto, con possibilità di scegliere a quale bolla collegarlo (dropdown se la lista ha più bolle), così da tracciare prelievi non pianificati.
17. Come operatore, voglio che una riga aggiunta manualmente senza bolla esegua un semplice `TRD_InsertMov` con causale `SCAR` al momento dell'esecuzione, così da mantenere la tracciabilità anche fuori bolla.
18. Come operatore, voglio che i prelievi già eseguiti (scritti sull'ERP, `ExecutedAt` valorizzato) non siano modificabili, così da non poter alterare la storia.
19. Come operatore, voglio accedere alla scheda articolo (pulsante "I") da ogni riga della lista, così da consultare dati tecnici o allegati senza uscire dal flusso.
20. Come operatore, voglio poter stampare un'etichetta contestuale dalla pagina lista prelievo, così da etichettare il materiale sul momento.

### Gestione Locazioni

21. Come responsabile magazzino, voglio creare una nuova locazione direttamente dal terminale (codice magazzino + codice locazione), così da non dover attendere l'ufficio per attivare un nuovo scaffale.
22. Come responsabile magazzino, voglio che il sistema avvisi se il codice locazione esiste già prima di confermare la creazione, così da evitare duplicati.
23. Come responsabile magazzino, voglio scegliere il magazzino da un elenco (non digitarlo a mano), così da evitare errori di battitura su un campo chiave.
24. Come responsabile magazzino, voglio scansionare o digitare il codice articolo e vedere l'elenco delle locazioni già abbinate (con indicazione della principale), così da avere il quadro completo prima di modificare.
25. Come responsabile magazzino, voglio aggiungere una locazione a un articolo (crea riga in `L_MLPA` con quantità zero), così da prepararla come destinazione prima ancora che vi sia stock.
26. Come responsabile magazzino, voglio impostare una locazione come principale (`LCPRC='Y'`), con aggiornamento esclusivo (la precedente principale perde il flag automaticamente).
27. Come responsabile magazzino, voglio rimuovere un abbinamento articolo-locazione solo se la giacenza è zero, con messaggio esplicativo in caso contrario.
28. Come operatore, voglio accedere a "Gestione Locazioni" dalla Home tramite una tile dedicata.

---

## Implementation Decisions

### Struttura DB — Logic DB (`LogicDatabase`)

**`WMS_PickList`** — testata lista di prelievo

| Colonna | Tipo | Note |
|---------|------|------|
| `Id` | UNIQUEIDENTIFIER PK | |
| `Code` | VARCHAR(30) | codice lista (barcode o auto-generato) |
| `Description` | NVARCHAR(100) | nome lista |
| `Status` | VARCHAR(20) | Open / InProgress / Completed / Cancelled |
| `CreatedAt` | DATETIME2 | |
| `CreatedByOp` | VARCHAR(15) | OPCOD |
| `ClosedAt` | DATETIME2 NULL | |

**`WMS_PickListRow`** — righe lista

| Colonna | Tipo | Note |
|---------|------|------|
| `Id` | UNIQUEIDENTIFIER PK | |
| `ListId` | UNIQUEIDENTIFIER FK | → WMS_PickList |
| `ArticleCode` | VARCHAR(20) | PACOD |
| `ArticleDesc` | NVARCHAR(200) | snapshot al momento di creazione |
| `UoM` | VARCHAR(10) | |
| `PlannedQty` | NUMERIC(18,6) | |
| `OlCod` | VARCHAR(20) NULL | bolla di produzione sorgente (NULL = fuori bolla) |
| `Handling` | NVARCHAR(80) NULL | tipo handling (da `a_THP.THDSC`) |
| `IdSpec` | INT NULL | da confermare: campo ERP A_PAR (SpecsPart) |
| `IdGroup` | INT NULL | da confermare: campo ERP A_PAR (GroupPart) |
| `MainLocationCode` | VARCHAR(15) NULL | snapshot locazione principale al momento caricamento |
| `MainWarehouseCode` | VARCHAR(15) NULL | |
| `IsMissing` | BIT | dichiarato mancante |
| `IsExtraItem` | BIT | aggiunto manualmente fuori lista |
| `Status` | VARCHAR(20) | Pending / Partial / Completed / Missing |
| `SortOrder` | INT | ordine originale di importazione |

**`WMS_PickListPick`** — prelievi staged e confermati

| Colonna | Tipo | Note |
|---------|------|------|
| `Id` | UNIQUEIDENTIFIER PK | |
| `RowId` | UNIQUEIDENTIFIER FK | → WMS_PickListRow |
| `PickedQty` | NUMERIC(18,6) | |
| `WarehouseCode` | VARCHAR(15) | |
| `LocationCode` | VARCHAR(15) | |
| `OperatorCode` | VARCHAR(15) | |
| `StagedAt` | DATETIME2 | momento aggiunta al dialogo |
| `ExecutedAt` | DATETIME2 NULL | NULL = staged, valorizzato = scritto su ERP |
| `ErpMovId` | INT NULL | IDMOV da TRD_InsertMov (o IDSPP da WMS_InsertPickLine) |

### Flusso di esecuzione "Fai prelievi"

Per ogni `WMS_PickListPick` con `ExecutedAt = NULL`:

1. Se la riga ha `OlCod` valorizzato → chiama `WMS_InsertPickLine(@OLCOD, @PACOD, qty, @LCCOD, @OPCOD, @IDSES)`
2. Se la riga non ha `OlCod` → chiama `TRD_InsertMov` con `@sCMCOD='SCAR'`
3. In entrambi i casi aggiorna `ExecutedAt = NOW()` e `ErpMovId` sul record

**Session ERP:** per le righe con bolla, aprire una `WMS_OpenPickSession` per bolla distinta prima del loop, chiuderla dopo. Se più righe della stessa bolla vengono eseguite nella stessa sessione, condividono lo stesso `@IDSES`.

### Criteri di ordinamento

La select di ordinamento opera su un `enum SortField`:

```
ArticleCode | Handling | IdSpec | IdGroup | MainLocation
```

Applicato lato C# con `IEnumerable<>.OrderBy(...)` dopo il caricamento. Multi-livello (primario/secondario) previsto come upgrade futuro — il modello UI (select singola → lista di `SortField`) deve già essere predisposto per passare a lista ordinata senza refactoring.

### IDSpec / IDGroup

Campi da **confermare con Mecmar/IT**: probabilmente `IDSPEC` e `IDGROUP` (o nomi simili) su `dbo.A_PAR`. Non risultano in `FactoryMecmar_ErpReference.md` — aggiungere alla checklist ERP e includere nella query di caricamento lista una volta confermati. Nel frattempo la riga `WMS_PickListRow` li prevede come `INT NULL`.

### Stato colori (staging)

| Stato riga | Colore bordo | Background |
|------------|-------------|------------|
| Pending (nessun pick staged) | grigio | neutro |
| Partial (staged < planned) | giallo | giallo scuro |
| Staged completo (staged ≥ planned) | verde | verde scuro |
| Missing dichiarato | rosso | rosso scuro |
| Completed (executed) | bianco/grigio tenue | neutro |

### Servizi

**`PickListService`** (scoped, Logic DB primario + ERP per giacenze e scrittura):

| Metodo | DB |
|--------|----|
| `GetPickListsAsync()` | Logic |
| `LoadListFromOlCodAsync(olCod, opCode)` | ERP → Logic (crea testata+righe da `WMS_V_PickList`) |
| `GetPickListDetailAsync(listId)` | Logic + ERP (giacenza real-time batch da `L_MLPA`) |
| `StagePickAsync(rowId, qty, mgCod, lcCod, opCode)` | Logic (INSERT WMS_PickListPick, ExecutedAt=NULL) |
| `RemoveStagedPickAsync(pickId)` | Logic (DELETE se ExecutedAt IS NULL) |
| `DeclareMissingAsync(rowId)` | Logic (UPDATE IsMissing=1, Status=Missing) |
| `StageAllFromMainAsync(listId, opCode)` | Logic (batch: "Preleva tutto") |
| `AddExtraRowAsync(listId, articleCode, plannedQty, olCod?, opCode)` | Logic |
| `ExecuteStagedPicksAsync(listId, opCode)` | ERP + Logic (esegue tutti i pending) |
| `GetRowPickHistoryAsync(rowId)` | Logic (prelievi eseguiti per la riga) |

### Gestione Locazioni — Servizio

**`LocationManagementService`** (singleton, ERP DB):

| Metodo | Operazione |
|--------|------------|
| `GetWarehousesAsync()` | `SELECT DISTINCT MGCOD FROM A_LOC` |
| `LocationExistsAsync(mgCod, lcCod)` | Verifica duplicati |
| `CreateLocationAsync(mgCod, lcCod)` | INSERT `A_LOC` (o SP se disponibile) |
| `GetArticleLocationsAsync(articleCode)` | Righe `L_MLPA` con `LCPRC` |
| `AssignArticleToLocationAsync(articleCode, mgCod, lcCod)` | INSERT `L_MLPA` qty=0 |
| `SetMainLocationAsync(articleCode, mgCod, lcCod)` | UPDATE `LCPRC` con esclusività |
| `RemoveArticleLocationAsync(articleCode, mgCod, lcCod)` | DELETE se `QTLOC = 0` |

### Modello C# (`WmsModels.cs`)

Nuovi tipi:

- `PickListHeaderDto` — proiezione sola lettura di `WMS_PickList` con progress
- `PickListRowDto` — include: `ArticleCode, PlannedQty, StagedQty, ExecutedQty, IsMissing, IsExtraItem, Handling, IdSpec, IdGroup, MainLocationCode, HasStock, Locations (List<ArticleLocationDto>), StagedPicks (List<StagedPickDto>)`
- `StagedPickDto` — `Id, PickedQty, LocationCode, ExecutedAt`
- `ArticleLocationDto` — già presente, riutilizzare con giacenza da `L_MLPA`
- `SortField` — enum per criteri ordinamento

Rimuovere: `PickListV2Dto`, `PickListV2ItemDto` (sostituiti dal nuovo modello).

### UI — `ListPick.razor`

Struttura pagine:
1. **Schermata selezione lista** — card con progress bar e status chip
2. **Schermata lista righe** — barra barcode in cima, select ordinamento, FAB "Aggiungi riga", bottone "Preleva tutto", bottone "Fai prelievi"
3. **Dialog articolo** (MudDialog service-based) — tutto il flusso di staging in un overlay, nessuna navigazione di pagina

Il dialog articolo contiene:
- Header: codice + descrizione + quantità (planned / staged / executed)
- Pannello scorrevole prelievi già eseguiti (scroll orizzontale solo loc+qty)
- Barcode field con focus automatico per scan locazione
- NumericField quantità
- Bottone "Aggiungi prelievo" (aggiunge staged pick)
- Bottone "Prelievo auto" (staged da locazione principale)
- Bottone "Segnala mancante"
- Elenco locazioni con giacenza (chips o mini-lista)

### UI — `LocationManage.razor`

- Nuova tile in `Home.razor`
- Pagina `/locations/manage` con `MudTabs`: "Crea Locazione" / "Abbina Articolo"
- Locazione principale: chip `Color.Warning` con icona stella
- "Rimuovi" disabilitato se giacenza > 0 con tooltip esplicativo

---

## Testing Decisions

**Principio:** testare comportamento osservabile (input/output del servizio), non i dettagli delle query SQL.

**Unit test — `PickListService`:**
- `StageAllFromMainAsync`: verifica che le righe `IsMissing=true` vengano ignorate
- Calcolo `StagedQty` e `ExecutedQty` per una riga con N prelievi misti (staged/executed)
- Colore/stato riga: `Partial` se staged < planned, `StagedComplete` se staged ≥ planned
- `ExecuteStagedPicksAsync`: verifica che solo i pick con `ExecutedAt = NULL` vengano processati

**Unit test — `LocationManagementService`:**
- `SetMainLocationAsync`: esclusività del flag LCPRC
- `RemoveArticleLocationAsync`: rifiuto se `QTLOC > 0`

**Test manuali (UI):**
- Caricamento lista da bolla: righe popolate correttamente con handling, locazione principale
- Flusso staging → esecuzione: colori intermedi → esecuzione → reset righe
- "Preleva tutto" con un articolo mancante: l'articolo mancante viene saltato
- "Aggiungi riga" con lista multi-bolla: il dropdown bolle è presente e funzionante
- Creazione locazione: codice già esistente → alert errore; codice nuovo → successo
- Impostazione locazione principale: la precedente perde il flag

**Prior art:** `Inventory.razor` per il pattern sessione+batch ERP; `CartMove.razor` per la gestione staged/evasione con feedback granulare per riga.

---

## Out of Scope

- Zone picking orchestrato da ufficio (modello di assegnazione dall'alto) — architettura compatibile, implementazione Fase 2
- Multi-livello di ordinamento (primario/secondario) — previsto nel modello, non implementato ora
- Generazione automatica di liste da ordini clienti o DDT — Fase 2
- Rotazione magazzino automatica — esclusa dal MVP
- Cancellazione locazioni esistenti — solo tramite ERP desktop
- Modifica delle giacenze tramite Gestione Locazioni (competenza di Rettifiche)
- Gestione UdM secondarie o conversioni per i prelievi

---

## Further Notes

- **Resilienza WiFi / perdita connessione:** ogni `StagePickAsync` scrive immediatamente in `WMS_PickListPick` (`ExecutedAt=NULL`) sul Logic DB — mai tenere i pick staged solo in memoria. Se il circuito Blazor Server cade (WiFi in magazzino), al reconnect `GetPickListDetailAsync` ricarica la lista inclusi tutti i pick staged già salvati: l'operatore non perde nulla. Configurare il reconnect Blazor con retry multipli e timeout generoso (`blazorReconnect`) per mostrare un overlay "Connessione persa — riprovo…" invece di portare subito alla pagina errore. L'operatore sarà comunque istruito a premere "Fai prelievi" frequentemente per limitare l'accumulo in staging.

- **IDSpec/IDGroup:** verificare i nomi reali delle colonne in `dbo.A_PAR` con Mecmar/IT prima di costruire la query di caricamento lista. Aggiungere a `FactoryMecmar_ErpReference.md` quando confermati.
- **Scritture ERP:** verificare con IT se `A_LOC` e `L_MLPA` ammettono INSERT/UPDATE diretti da applicazioni esterne o se esistono SP dedicate (stessa policy di `TRD_InsertMov`).
- **Session ERP per batch:** la gestione di `WMS_OpenPickSession` / `WMS_ClosePickSession` lato `ExecuteStagedPicksAsync` richiede un mapping `OlCod → IDSES` durante l'esecuzione. Se la SP non è transazionale, valutare una transazione a livello di Logic DB per il rollback.
- **Stampa da lista:** aggiungere contesto `"PICK"` in `WMS_PrintTemplate.TPLCTX` se si vuole abilitare la stampa etichette dalla pagina lista.
- **Sicurezza Gestione Locazioni:** operazioni di creazione/abbinamento locazione dovrebbero essere limitate per `GRCOD` — da concordare con Mecmar la mappatura ruoli.
