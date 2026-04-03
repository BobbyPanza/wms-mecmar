# Query / viste / procedure richieste dal WMS (database gestionale ERP)

**Origine dati:** database del gestionale (oggi di esempio: `FactoryMecmar` su `localhost`).  
**Configurazione app:** connection string nome **`ErpDatabase`** in `appsettings.json` / variabili ambiente / secret (non committare password).

**Riferimento tabelle esplorate (Mecmar):** vedi **`FactoryMecmar_ErpReference.md`** (A_PAR, A_LOC, L_MLPA, S_MOV, A_CMM, A_OPR; nota **FMCOD** al posto di PAFAM).

Per ogni punto sotto servono **nome oggetto SQL** (tabella/vista/SP) e **firma** (parametri + colonne restituite). Puoi rispondere incollando le query che generi o indicando “uso vista X già esistente”.

---

## Convenzioni utili

- Indica se la logica è **sola lettura** o se il WMS deve **scrivere** (INSERT/UPDATE) o chiamare una **SP** che il gestionale espone.
- Per codici articolo/locazione: precisa **tipo/collation** e se esistono **codici alternativi** (EAN, ecc.).
- Per quantità/giacenze: unità di misura, arrotondamenti, magazzini filtrabili.

---

## 1. Autenticazione

### Definito: operatori su `A_OPR`

Tabella gestionale: **`A_OPR`**.

| Colonna | Uso previsto nel WMS |
|---------|----------------------|
| **OPCOD** | Codice operatore (login / username) |
| **OPDSC** | Descrizione / nome visualizzato |
| **OPPSW** | Password (confermare se testo chiaro, hash o altro confronto lato app) |
| **GRCOD** | Codice gruppo (ruolo / permessi magazzino da mappare in seguito) |

Query tipo per login (adattare filtri se servono solo operatori attivi):

```sql
SELECT OPCOD, OPDSC, OPPSW, GRCOD
FROM A_OPR
WHERE OPCOD = @Opcod;   -- oppure confronto case-insensitive se la colazione lo richiede
```

Da concordare: eventuali colonne **flag** (es. operatore disabilitato), **magazzino di default**, altri filtri `WHERE`.

| # | Scopo | Input atteso | Output atteso (colonne minime) | Note |
|---|--------|----------------|-------------------------------|------|
| 1.1 | Validare **utente** e **password** contro **`A_OPR`** | OPCOD (login), password da confrontare con **OPPSW** | OK: OPCOD, OPDSC, GRCOD | Implementazione app: vedi `WmsAuth:UseErpOperators` in config. |

---

## 2. Interrogazione universale

| # | Scopo | Input | Output | Note |
|---|--------|-------|--------|------|
| 2.1 | Verificare se un **codice** è un **articolo** | stringa codice | sì/no; codice articolo normalizzato; descrizione breve | Opzionale se usate solo 2.2/2.3. |
| 2.2 | Verificare se un **codice** è una **locazione** | stringa codice | sì/no; codice locazione normalizzato; descrizione | |
| 2.3 | **Giacenza articolo per locazione** | codice articolo | righe: locazione, quantità disponibile, eventuale UM, magazzino | Ordinamento preferito se definito. |
| 2.4 | **Contenuto locazione** (articoli presenti) | codice locazione | righe: articolo, descrizione, quantità, UM | |

---

## 3. Spostamento semplice

| # | Scopo | Input | Output | Note |
|---|--------|-------|--------|------|
| 3.1 | **Locazioni sorgente** per articolo ordinate per giacenza decrescente | articolo | lista locazioni + qty disponibile | Query su `L_MLPA WHERE PACOD=@cod AND QTLOC>0 ORDER BY QTLOC DESC` |
| 3.2 | **Registrare spostamento** | vedi firma SP sotto | IDMOV (int) oppure -1 se errore | **Due chiamate**: SMI (scarico origine) + CMI (carico destinazione) |

### Definita: `dbo.TRD_InsertMov`

Unica SP per tutti i movimenti di magazzino. Non scrivere mai direttamente su `S_MOV`.

```sql
EXEC dbo.TRD_InsertMov
    @sPACOD       VARCHAR(20),          -- [obbligatorio] codice articolo
    @sCMCOD       VARCHAR(10),          -- [obbligatorio] codice causale (deve avere CMTYP = 1 o -1)
    @fMOQTV       NUMERIC(18,6),        -- [obbligatorio] quantità POSITIVA (segno gestito da CMTYP)
    @sMGCOD       VARCHAR(15) = '',     -- codice magazzino (vuoto = usa locazione principale LCPRC='Y')
    @sLCCOD       VARCHAR(15) = '',     -- codice locazione  (vuoto = usa locazione principale LCPRC='Y')
    @iIDTBR       INT        = NULL,    -- id riga documento di riferimento (es. riga DDT)
    @iIDRIF       INT        = NULL,    -- id documento di riferimento
    @iMoveRect    SMALLINT   = 0,       -- flag rettifica (0 = movimento normale)
    @nodeID       INT        = 0,       -- nodo terminale (lasciare 0 da WMS)
    @operatorCode CHAR(15)   = NULL,    -- codice operatore → colonna OPCOD in S_MOV
    @referenceCode VARCHAR(80) = NULL,  -- riferimento libero → colonna RFDOC in S_MOV
    @movementDate DATETIME2(6) = NULL,  -- data movimento (NULL = CURRENT_TIMESTAMP → MOSTP)
    @sCOCOD       CHAR(15)   = NULL,    -- codice cliente/fornitore
    @sOLCOD       CHAR(10)   = NULL,    -- codice riga ordine
    @iIDCRN       INT        = NULL,    -- id commessa/ordine di produzione
    @iPLCOD       INT        = NULL,    -- id pallet (se gestione UDC)
    @fPrice       NUMERIC(18,6) = 0,    -- prezzo unitario (per valorizzazione)
    @PackageId    INT        = NULL,    -- id collo
    @PackageCode  VARCHAR(40) = NULL,   -- codice collo
    @IDPAR        INT        = NULL     -- IDPAR articolo (alternativa a PACOD per lookup)
```

**Valore di ritorno:** `IDMOV` (INT) = progressivo del movimento inserito in `S_MOV`; ritorna `-1` se `CMTYP` non è `1` o `-1` (causale non valida → nessuna scrittura).

**Comportamento interno:**
- Legge `CMTYP` da `A_CMM`: deve essere `1` (carico) o `-1` (scarico), altrimenti non scrive nulla.
- Aggiorna `L_MLPA`: se la riga articolo+magazzino+locazione non esiste la crea; se esiste fa `QTLOC = QTLOC + (CMTYP × fMOQTV)`.
- Se MGCOD/LCCOD sono stringa vuota, usa la locazione con `LCPRC = 'Y'` (locazione principale) per quell'articolo.
- Gestisce anche `L_MLPR` (riserve per commessa) se `@iIDCRN` valorizzato, e `L_PAPL` (pallet) se `@iPLCOD` valorizzato.
- `MOSTP` in `S_MOV` = `@movementDate` (o `CURRENT_TIMESTAMP` se NULL) — **non esiste scrittura su DTDOC** (campo legacy con date anomale).

**Causali per spostamento semplice:**

| Chiamata | `@sCMCOD` | `@fMOQTV` | `@sMGCOD/@sLCCOD` |
|----------|-----------|-----------|-------------------|
| 1 — scarico origine | `SMI` (CMTYP = -1) | qty positiva | magazzino + locazione origine |
| 2 — carico destinazione | `CMI` (CMTYP = 1)  | qty positiva | magazzino + locazione destinazione |

Passare lo stesso `@referenceCode` su entrambe le chiamate per correlare i due movimenti.

---

## 4. Prelievo produzione (implementato)

### Vista di lettura: `dbo.WMS_V_PickList` ✅

Legge la lista articoli da prelevare per bolla di lavoro (`OLCOD`).  
Script: `sql/V001__WMS_V_PickList.sql`

Unisce:
- **Parti esterne** (`L_CMFE`): componenti richiesti dalla distinta del lotto
- **Lotti interni** (`A_LOT` figli): sub-lotti con qty proporzionale

Colonne restituite: `OLCOD, CONUM, LOCOD, PACOD, PADSC, PAUDM, Handling, Ubicazione, Giacenza, QtaDaPrelevare, QtaPrelevata, IDPAP`

| Campo | Fonte |
|-------|-------|
| `Handling` | `a_THP.THDSC` (tipo movimentazione articolo) |
| `Ubicazione` | `A_PAR.paf02` (campo libero = locazione logistica articolo) |
| `Giacenza` | `L_MLPA` con `LCPRC='Y'` (locazione principale) |
| `QtaPrelevata` | `S_PAP.PAQTP` (aggiornata automaticamente da trigger su S_SPP) |
| `IDPAP` | NULL se non ancora iniziata la riga di prelievo |

### Stored Procedure di scrittura ✅

Script: `sql/V002__WMS_Pick_SPs.sql`

**Flusso batch (una sessione per N articoli):**

```
WMS_OpenPickSession  (@OLCOD, @OPCOD, @NOCOD) → @IDSES
  WMS_InsertPickLine (@OLCOD, @PACOD, …, @IDSES) → @IDMOV   ← ripetere per ogni articolo
WMS_ClosePickSession (@IDSES)
```

**Flusso per riga singola** (SP legacy, ora wrapper del batch):
```
WMS_InsertPick (@OLCOD, @PACOD, …) → @IDMOV
```

Catena dati per ogni riga:
1. `S_PAP` — riga di prelievo per bolla+articolo (upsert, progressivo via `GetProgressivo`)
2. `S_SPP` — evento di prelievo fisico, TPREC=1, legato a IDSES e IDPAP
3. `TRD_InsertMov` SCAR, IDTBR=5, IDRIF=IDSPP — scarico da magazzino
4. Il trigger `TRG_ON_INSERT_SPP` aggiorna automaticamente `S_PAP.PAQTP`

**Nota sui progressivi:** usare sempre `GetProgressivo 'dbo.S_SES'` / `'dbo.S_PAP'` / `'dbo.S_SPP'` — **non usare MAX()**. I trigger INSTEAD OF INSERT delle tre tabelle usano `SetProgressiveToTempTable` ma falliscono se chiamati con ID=0 dentro una transazione con TRD_InsertMov.

### Nodo dispositivo: `dbo.A_NOD` ✅

Ogni device WMS si registra in `A_NOD` con `PRDCD=10006`.  
`CDNOD=0` in INSERT → trigger assegna progressivo automaticamente.  
Il CDNOD viene salvato in `localStorage` del browser e ricaricato ad ogni sessione.

| # | Scopo | Input | Output | Note |
|---|--------|-------|--------|------|
| 4.1 | **Lista articoli bolla** | OLCOD | Vedi `WMS_V_PickList` | ✅ |
| 4.2 | **Testata bolla** | OLCOD | Stato, commessa, flag terminata, bolla versamento | ✅ da `S_ODL+L_ODLA+A_LOT+A_COM+A_STA` |
| 4.3 | **Registra prelievo** | OLCOD, PACOD, PAQTP, OPCOD, NOCOD | IDMOV | ✅ `WMS_InsertPickLine` |

---

## 5. Rettifiche semplici

| # | Scopo | Input | Output | Note |
|---|--------|-------|--------|------|
| 5.1 | **Lettura giacenza** corrente articolo+locazione | articolo, locazione | quantità | |
| 5.2 | **Applicare rettifica** | articolo, locazione, nuova qty (o delta), utente | esito | Preferibilmente SP gestionale. |

---

## 6. Inventario

| # | Scopo | Input | Output | Note |
|---|--------|-------|--------|------|
| 6.1 | **Liste inventario** inviate dal gestionale | filtri | testata lista | |
| 6.2 | **Righe inventario** | id lista | articolo, locazione, qty teorica, … | |
| 6.3 | **Conferma / rettifica** a consuntivo | lista righe contate | esito | SP o tabella staging + processo. |

---

## 7. Carrello

| # | Scopo | Input | Output | Note |
|---|--------|-------|--------|------|
| 7.1 | Dati anagrafici/giacenza per **compilazione carrello** | come interrogazione + movimenti | | Molte parti possono restare solo su DB WMS; indicare cosa deve **tornare** all’ERP a evasione. |
| 7.2 | **Evasione carrello** verso destinazioni | righe carrello + destinazioni | esito | SP o API gestionale. |

---

## 8. Scheda articolo (Info “I”)

**Definito (Mecmar):** PDF e immagini (JPG/PNG) sono file su **percorsi di rete** leggibili dall’**IIS**; metadati in **`dbo.A_DOC`** con **`A_DOC.IDPAR = A_PAR.IDPAR`** — path tipico colonna **`NTDOC`**. Dettaglio in **`FactoryMecmar_ErpReference.md`**.

| # | Scopo | Input | Output | Note |
|---|--------|-------|--------|------|
| 8.1 | **Anagrafica / dati tecnici** estesi | `PACOD` o `IDPAR` | campi da `A_PAR` | |
| 8.2 | **Elenco documenti** (PDF + preview) | `IDPAR` | righe `A_DOC` (`NTDOC`, `DSDOC`, `ShowInPreview`, …) | Filtrare per estensione o `TYDOC` se necessario |
| 8.3 | **Lettura file** | path UNC/share | stream o URL interno | Identity IIS con accesso in lettura alle share |

---

## Cosa inviare tu (checklist)

Per ogni riga che vi riguarda, prepara almeno uno tra:

- `SELECT …` (vista consigliata per stabilità), oppure  
- `CREATE VIEW … AS …`, oppure  
- `CREATE PROCEDURE …` con parametri e set di risultati.

Indica anche **indici** già presenti su codice articolo/locazione se critici per performance.

Quando le hai, le integriamo nel layer Dapper/EF del progetto sostituendo i provider “demo”.
