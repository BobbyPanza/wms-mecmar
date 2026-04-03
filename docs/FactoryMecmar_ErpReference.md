# Riferimento ERP — database `FactoryMecmar` (campioni da query reali)

Connection: `ConnectionStrings:ErpDatabase` (es. `Server=localhost;Database=FactoryMecmar;…`).

---

## Anagrafica articoli — `dbo.A_PAR`

Colonne richieste dal WMS (con correzione nome famiglia):

| Nome reale | Note |
|------------|------|
| **PACOD** | Codice articolo (varchar 20) |
| **PADSC** | Descrizione |
| **FMCOD** | Famiglia / raggruppamento (in documentazione era indicato *PAFAM*: su questo DB **non esiste PAFAM**, usare **FMCOD**) |
| **PAF01** | Campo descrittivo aggiuntivo (varchar 80), spesso NULL |
| **PAPSO** | Numerico (es. peso/prezzo secondo gestionale), spesso NULL |

*Esempio (prime righe ordinate per PACOD):* codici tipo `STR9/87 F-213253`, descrizioni lunghe, `FMCOD` = `Generica`, `Tutti`, ecc.

Altri campi utili già presenti sulle parti: `PAUDM` (UM), `IDART`, `MTCOD`, coordinate `PADX1`…, `Drawing` (eventuale path aggiuntivo in anagrafica).

---

## Documenti articolo (PDF / immagini) — `dbo.A_DOC`

Collegamento: **`A_DOC.IDPAR = A_PAR.IDPAR`** (`IDPAR` è chiave su `A_PAR`).

- I **file** (PDF, PNG/JPG per anteprima, ecc.) stanno in **percorsi di rete** (es. UNC `\\srv05\...`, oppure unità mappate `O:\...`) **accessibili al processo IIS** (identity dell’application pool con diritti in lettura sulle share).
- Campo tipico del **path completo** del file: **`NTDOC`** (varchar 255) — dai campioni risultano `.pdf`, `.PNG`, `.png`.
- Altre colonne utili in UI: **`CDDOC`**, **`DTDOC`**, **`DSDOC`** (descrizione), **`ShowInPreview`**, **`DefaultPreview`**, **`TYDOC`**, **`IDDOC`** (progressivo documento per articolo).

Esempio elenco documenti per articolo:

```sql
SELECT d.IDPAR, d.IDDOC, d.NTDOC, d.DSDOC, d.DTDOC, d.ShowInPreview, d.DefaultPreview
FROM dbo.A_DOC AS d
WHERE d.IDPAR = @IdPar
ORDER BY d.IDDOC;
```

*Nota:* liste di prelievo e flussi di versamento documentati a parte, quando definite le tabelle ERP.

---

## Locazioni — `dbo.A_LOC`

Chiave composta come indicato:

| Colonna | Note |
|---------|------|
| **MGCOD** | Codice magazzino / ambito (es. ` Fornitori`, `01`) |
| **LCCOD** | Codice ubicazione (es. `F01918`, `01`) |

*Esempio:* coppie `MGCOD|LCCOD` tipo ` Fornitori|F01918`, ecc.

Per chiave “completa” in UI/scan spesso si concatena o si mostra `MGCOD` + `LCCOD` (definire formato unico per barcode se esiste).

---

## Giacenza per parte + locazione — `dbo.L_MLPA`

Abbinamento articolo–magazzino–locazione con quantità locale.

| Colonna | Uso tipico |
|---------|------------|
| **PACOD** | Articolo |
| **MGCOD** | Magazzino |
| **LCCOD** | Locazione |
| **QTLOC** | Quantità in locazione (può essere negativa in anomalie) |
| **QTMIN** / **QTMAX** | Soglie (se usate) |

*Esempio:* righe con `MAGFORN|F00xxx`, `01|01`, `QTLOC` valorizzato.

Query tipiche WMS:

- Giacenza per **articolo** (tutte le locazioni): `WHERE PACOD = @pacod` (eventuale filtro `MGCOD`).
- Contenuto per **locazione**: `WHERE MGCOD = @mg AND LCCOD = @lc` (o solo `LCCOD` se univoco nel vostro modello).

---

## Movimenti — `dbo.S_MOV`

Tracciamento movimenti di magazzino.

Campioni utili: **PACOD**, **MGCOD**, **LCCOD**, **CMCOD** (causale), **MOQTA** / **MOQTV**, **DTDOC**, **OPCOD** operatore.

*Nota:* in archivio possono comparire **date anomale** (es. anni 5016/5201) oltre a date recenti (2026): filtri per “movimenti recenti” o validazione data lato app.

---

## Causali — `dbo.A_CMM`

| Colonna | Note |
|---------|------|
| **CMCOD** | Codice causale (varchar 8) |
| **CMDSC** | Descrizione |
| **CMTYP** | Tipo numerico (es. `1` carico, `-1` scarico, `0` altro) — utile per UI e validazioni |

*Esempio:* `01` Carico per Acquisto, `03` Scarico per Vendita, `CARINV` CAR. DA INV., `CARVIN` Rettifica per inventario, ecc.

---

## Operatori — `dbo.A_OPR` (già integrato in app)

**OPCOD**, **OPDSC**, **OPPSW**, **GRCOD** per login.

---

## “Altro” utile per il WMS (da definire con voi)

| Area | Possibili oggetti |
|------|-------------------|
| **Liste di prelievo e versamento** | *In definizione — tabelle/query ERP da concordare* |
| **Documenti / riferimenti** | RFDOC, RIF, IDRIF in `S_MOV` e collegamenti a ordini |
| **Unità di misura / conversioni** | `PAUDM`, eventuali tabelle UM |
| **Immagini / PDF articolo** | **`A_DOC`** (`NTDOC` + join `IDPAR` su **`A_PAR`**) — percorsi rete leggibili da IIS |
| **Blocchi / articoli non movimentabili** | `IsBlocked`, flag su `A_PAR` |
| **Inventario** | Documenti inventario + righe conteggio (se distinti da `S_MOV`) |

Quando definite le tabelle per **liste prelievo da ERP**, aggiungiamo una sezione analoga con `SELECT` esempio.

---

## Riepilogo correzione nome colonna

- **PAFAM** → non presente: usare **`FMCOD`** su `A_PAR` per la “famiglia” / raggruppamento merceologico.
