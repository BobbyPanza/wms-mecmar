# WMS Mecmar — Documentazione operativa e tecnica

> **Versione applicativo:** 1.2.0
> **Ultimo aggiornamento documento:** 2026-09-08
> **Repository:** `BobbyPanza/wms-mecmar` (privato)
> **Cliente:** Mecmar S.p.A. — referente cliente: Stefano Marcolongo
> **Destinatari:** sviluppatori Intesi, referenti applicativi, key user di magazzino

Questa pagina è la fonte unica di verità sul WMS Mecmar. È divisa in due parti:

- **[Parte 1 — Documentazione operativa](#parte-1--documentazione-operativa)**: cosa fa l'applicazione, come si usa transazione per transazione. Leggibile anche da chi non sviluppa.
- **[Parte 2 — Documentazione tecnica](#parte-2--documentazione-tecnica)**: architettura, modello dati, causali, configurazione, deploy, convenzioni di sviluppo.

---

## Indice

**Parte 1 — Operativa**
1. [Che cos'è il WMS Mecmar](#1-che-cosè-il-wms-mecmar)
2. [Glossario](#2-glossario)
3. [Dispositivo, accesso e convenzioni di interfaccia](#3-dispositivo-accesso-e-convenzioni-di-interfaccia)
4. [Menu principale](#4-menu-principale)
5. [Le transazioni in dettaglio](#5-le-transazioni-in-dettaglio)
6. [Stampa etichette](#6-stampa-etichette)
7. [Area amministrazione](#7-area-amministrazione)
8. [Errori frequenti e cosa fare](#8-errori-frequenti-e-cosa-fare)

**Parte 2 — Tecnica**
9. [Stack e architettura](#9-stack-e-architettura)
10. [Modello dati](#10-modello-dati)
11. [Causali di movimento e regole ERP](#11-causali-di-movimento-e-regole-erp)
12. [Mappa transazione → oggetti database](#12-mappa-transazione--oggetti-database)
13. [Sottosistema di stampa](#13-sottosistema-di-stampa)
14. [Nodo dispositivo, sessione e autenticazione](#14-nodo-dispositivo-sessione-e-autenticazione)
15. [Endpoint HTTP e file su rete](#15-endpoint-http-e-file-su-rete)
16. [Configurazione (appsettings)](#16-configurazione-appsettings)
17. [Script SQL e ordine di applicazione](#17-script-sql-e-ordine-di-applicazione)
18. [Build, publish e hosting IIS](#18-build-publish-e-hosting-iis)
19. [Convenzioni di sviluppo e trappole note](#19-convenzioni-di-sviluppo-e-trappole-note)
20. [Debiti tecnici e punti aperti](#20-debiti-tecnici-e-punti-aperti)

---
---

# PARTE 1 — DOCUMENTAZIONE OPERATIVA

## 1. Che cos'è il WMS Mecmar

Il WMS Mecmar è un'applicazione web **mobile-first** che porta le operazioni di magazzino sul terminale barcode dell'operatore. Non è un magazzino autonomo: è un **ponte verso il gestionale Intesi** (database `FactoryMecmar`).

Punti chiave da capire subito:

- **La giacenza vive nell'ERP, non nel WMS.** Ogni operazione confermata dall'operatore diventa immediatamente un movimento di magazzino nel gestionale. Non esiste un "magazzino WMS" da riconciliare a fine giornata.
- **Il WMS ha un proprio database di appoggio** (`Logic`) usato solo per lo *stato di lavoro*: carrello in corso, sessione inventario aperta, liste di prelievo, prelievi non ancora sincronizzati, foto di accettazione, template di stampa.
- **Nessuna scrittura diretta sulle tabelle movimenti dell'ERP.** Tutto passa dalla stored procedure standard Intesi `TRD_InsertMov`, oppure da stored procedure dedicate al WMS. Questo garantisce che trigger, progressivi e coerenza dell'ERP restino intatti.
- **Resilienza al WiFi.** Nelle transazioni lunghe (liste di prelievo, inventario) l'operatore lavora su dati salvati sul database di appoggio ad ogni singola azione. La sincronizzazione verso l'ERP è un'operazione esplicita e batch. Se il WiFi cade, non si perde il lavoro fatto.

### Modalità demo

Se la connessione al database ERP non è configurata, l'applicazione parte comunque e usa dati finti (`MockWmsService`). Serve per demo e sviluppo. In modalità demo le scritture non arrivano a nessun gestionale. Si riconosce dalla schermata di login: al posto del campo "codice operatore" compare una **tendina** con operatori precaricati.

---

## 2. Glossario

| Termine | Significato |
|---------|-------------|
| **Articolo** / `PACOD` | Codice articolo di anagrafica ERP |
| **Magazzino** / `MGCOD` | Codice magazzino. In Mecmar: `01` = magazzino generico, `MEC` = magazzino con ubicazioni |
| **Locazione** / **Ubicazione** / `LCCOD` | Codice ubicazione fisica dello scaffale |
| **Abbinamento** | La riga che lega articolo + magazzino + locazione, con la sua quantità. È l'unità su cui il WMS lavora |
| **Locazione principale** / **preferenziale** | La locazione di prelievo di default per un articolo (flag `LCPRC='Y'`) |
| **Causale** / `CMCOD` | Codice che qualifica il movimento (carico, scarico, rettifica, inventario…) |
| **Bolla** / `OLCOD` | Ordine di lavoro / bolla di produzione. È il riferimento a cui si legano i prelievi |
| **Lotto** / `CONUM` + `LOCOD` | Commessa e lotto di produzione. Una bolla appartiene a un lotto |
| **DDT** | Documento di trasporto in ingresso da fornitore, da accettare |
| **Versamento** | Registrazione della merce prodotta o ricevuta a magazzino |
| **Staged** | Prelievo registrato dall'operatore sul WMS ma non ancora scritto sul gestionale |
| **Nodo** / `CDNOD` | Identificativo del terminale fisico, registrato nell'ERP. Serve alle stored procedure di prelievo |
| **Operatore** / `OPCOD` | Utente di magazzino, anagrafato nell'ERP in `A_OPR` |

---

## 3. Dispositivo, accesso e convenzioni di interfaccia

### 3.1 Dispositivo di riferimento

- Terminale **Android** industriale con scanner barcode integrato
- Schermo **5.5" HD+**, risoluzione di riferimento **1440 × 720**
- Lo scanner è configurato in modalità **keyboard wedge**: legge il codice e invia **Invio** a fine lettura
- L'applicazione si apre nel browser del terminale. È installabile come PWA (manifest + service worker con pagina offline)

### 3.2 Accesso

1. Si apre l'URL del WMS → schermata **Login** con il logo Mecmar
2. Si digita (o si scansiona) il **codice operatore**
3. Si premono **Invio** o **ACCEDI**
4. Se l'operatore ha una password configurata nell'ERP, compare il campo **PIN**; se non ne ha, l'accesso è immediato
5. La sessione viene ricordata sul terminale: chiudendo e riaprendo il browser non serve ripetere il login

Il **logout** è nel menu in alto a destra (sotto il codice operatore), assieme alla voce **Configurazioni**.

> **Nota operativa:** la sessione è legata al browser del terminale, non all'operatore. Se il terminale è condiviso fra turni, chi smonta deve fare logout esplicito.

### 3.3 Convenzioni di interfaccia

L'interfaccia è progettata per l'uso in piedi, con guanti, spesso con una mano sola ("fat finger").

| Convenzione | Comportamento |
|-------------|---------------|
| **Campo barcode** | È sempre il campo con l'icona QR. Riceve il focus automaticamente. Accetta scansione o digitazione manuale, confermando con **Invio** o **Tab**. Il codice viene sempre normalizzato in maiuscolo |
| **Pulsante `I`** | Accanto a ogni codice articolo. Apre la scheda articolo con dati anagrafici, PDF e immagini disponibili |
| **Tasti numerici in Home** | Dalla schermata menu, i tasti **1–8** aprono direttamente la transazione corrispondente |
| **Freccia indietro in alto a sinistra** | Torna al menu principale |
| **Icona stampante in alto** | Compare solo se la schermata corrente ha un contesto di stampa attivo |
| **Frecce ↑ ↓ flottanti a destra** | Nelle liste lunghe (inventario, prelievo): salto rapido a inizio / fine lista |
| **Notifiche a fondo schermo** (snackbar) | Verde = operazione riuscita · Arancione = attenzione · Rosso = errore |

### 3.4 Codice colore delle righe

Il colore di sfondo delle righe comunica lo stato a colpo d'occhio, senza leggere i numeri:

| Colore | Significato |
|--------|-------------|
| **Blu / neutro** | Da fare |
| **Verde** | Completato, oppure quantità disponibile sopra la scorta minima |
| **Giallo / arancione** | Parziale, oppure quantità disponibile bassa |
| **Rosso** | Mancante, differenza rilevata, oppure giacenza zero |
| **Grigio con opacità ridotta** | Già evaso / non più modificabile |

---

## 4. Menu principale

Otto riquadri, due per riga. La cifra è la scorciatoia da tastiera.

| # | Riquadro | Route | A cosa serve |
|---|----------|-------|--------------|
| 1 | **Interrogazione** | `/query` | Scansiona un codice qualsiasi e ottieni la situazione completa |
| 2 | **Rettifiche** | `/adjustments` | Correggere la giacenza di articolo + locazione |
| 3 | **Spostamento Semplice** | `/move/simple` | Spostare un articolo da una locazione a un'altra |
| 4 | **Spostamento Carrello** | `/move/cart` | Raccogliere più articoli e poi assegnare le destinazioni |
| 5 | **Accettazione** | `/acceptance` | Ricevere e ubicare la merce dei DDT fornitore |
| 6 | **Inventario** | `/inventory` | Conteggio fisico di una zona con generazione delle rettifiche |
| 7 | **Lista Prelievo** | `/pick/list` | Prelevare i componenti richiesti da una o più bolle |
| 8 | **Gestione Locazioni** | `/locations/manage` | Creare locazioni e abbinarle agli articoli |

Il riquadro **Spostamento Carrello** mostra un contatore con il numero di righe in carrello.

### Pagine non presenti nel menu

Raggiungibili solo via URL diretto:

| Route | Stato |
|-------|-------|
| `/production/pick` | **Prelievo Produzione** — prelievo da singola bolla senza staging. Funzionante su ERP, superata operativamente da `/pick/list` |
| `/query/article`, `/query/location` | Vecchie interrogazioni separate, solo dati demo. Assorbite da `/query` |
| `/config` | Gestione template di stampa (raggiungibile dal menu utente) |
| `/admin/...` | Area amministrazione, riservata a gruppi autorizzati |

---

## 5. Le transazioni in dettaglio

### 5.1 Interrogazione unificata — `/query`

**Obiettivo.** Un solo campo, un solo scan: l'applicazione capisce da sola se il codice è un articolo o una locazione.

**Come funziona.** Il codice viene prima cercato come articolo. Se non esiste un articolo con quel codice, viene cercato come locazione. Se nessuno dei due esiste, compare l'avviso di codice non trovato.

**Se il codice è un ARTICOLO**, si ottiene:

- Testata con **miniatura** dell'articolo (cliccabile per ingrandirla a tutto schermo), codice, descrizione, unità di misura
- **Giacenza totale** con badge colorato rispetto alla scorta minima
- Chip riassuntivi: **scorta minima**, **quantità impegnata**, **quantità ordinata**
- Quattro schede:
  - **Locazioni** — tutte le locazioni in cui l'articolo è presente con la quantità; la locazione principale è etichettata `PRINCIPALE`; se è definita una quantità massima, una barra di riempimento
  - **Movimenti** — ultimi movimenti con causale, data/ora, operatore, magazzino/locazione, quantità con segno e colore
  - **Ordini** — ordini in corso con codice, descrizione, quantità, data prevista, e totale
  - **Impegni** — impegni in corso con gli stessi campi

**Se il codice è una LOCAZIONE**, si ottiene:

- Testata con magazzino / locazione, descrizione, descrizione magazzino, e chip di tipo (`TRANSITO`, `FISCALE`)
- Due schede:
  - **Contenuto** — tutti gli articoli presenti con quantità; ciascuno con il pulsante `I`
  - **Movimenti** — ultimi movimenti della locazione

**Contesto di stampa.** `ARTICLE` (con codice articolo precompilato) oppure `LOCATION` (con locazione e magazzino).

**Scritture sul gestionale.** Nessuna. È una transazione di sola lettura.

---

### 5.2 Rettifiche semplici — `/adjustments`

**Obiettivo.** Portare la giacenza di un abbinamento articolo/locazione al valore reale.

**Quando si usa.** Correzione puntuale, fuori dal processo di inventario: l'operatore ha davanti lo scaffale, vede che il numero non torna, lo corregge subito.

**Flusso a 4 passi** (indicatore di avanzamento in alto):

| Passo | Azione |
|-------|--------|
| **1 · Articolo** | Scansione del codice articolo. Se non esiste, avviso e si resta sul passo |
| **2 · Locazione** | Scansione della locazione, oppure tocco su una delle locazioni con giacenza già elencate. Al tocco la giacenza viene **riletta dal database** (potrebbe essere cambiata da quando è stata caricata la pagina) |
| **3 · Quantità** | Si inserisce la **nuova giacenza** (non il delta). L'applicazione mostra il riepilogo con delta calcolato e la causale che verrà usata |
| **4 · Fine** | Messaggio di conferma con il numero di movimento generato. Pulsanti per nuova rettifica o ritorno al menu |

**Cosa scrive sul gestionale.**

| Situazione | Effetto |
|------------|---------|
| Nuova quantità **maggiore** dell'attuale | Movimento con causale **`REP`** (rettifica positiva) per il delta |
| Nuova quantità **minore** dell'attuale | Movimento con causale **`REN`** (rettifica negativa) per il delta |
| Nuova quantità **uguale** all'attuale | **Nessun movimento**, ma viene registrata la **verifica dell'abbinamento** |

**La verifica dell'abbinamento.** Ogni rettifica — compresa la conferma "il numero è giusto" — timbra sull'abbinamento articolo/locazione *chi* ha verificato e *quando*. Questo alimenta il report [Verifiche locazioni](#74-verifiche-locazioni) in area admin, che permette di capire quali abbinamenti non sono mai stati controllati o non lo sono da troppo tempo.

> Se l'abbinamento articolo/locazione non esiste ancora nell'anagrafica giacenze, la verifica non può essere registrata e il messaggio lo segnala esplicitamente. La rettifica, se c'era un delta, è comunque andata a buon fine.

**Contesto di stampa.** `ADJUSTMENT` (articolo, magazzino, locazione, quantità).

---

### 5.3 Spostamento semplice — `/move/simple`

**Obiettivo.** Spostare una quantità di un articolo da una locazione a un'altra.

**Flusso a 4 passi:**

| Passo | Azione |
|-------|--------|
| **1 · Articolo** | Scansione articolo. Compare la testata con giacenza totale e il pulsante `SCEGLI ORIGINE` |
| **2 · Origine** | Elenco delle locazioni **ordinate per quantità decrescente**, colorate per disponibilità. Si tocca la riga, oppure si scansiona direttamente la locazione. Se la locazione scansionata non è in elenco, la giacenza viene letta dal database. Locazione a giacenza zero → errore bloccante |
| **3 · Destinazione** | Scansione della locazione di destinazione. Deve esistere in anagrafica locazioni: se non esiste, avviso e nessun avanzamento |
| **4 · Quantità** | Campo numerico con focus automatico e testo preselezionato (si può digitare subito sovrascrivendo). Quantità superiore alla disponibile → avviso che richiede autorizzazione supervisore. Riepilogo completo prima della conferma |

Dopo la conferma: schermata di successo con il riassunto, e pulsanti per un nuovo spostamento o il ritorno al menu.

**Cosa scrive sul gestionale.** **Due** movimenti correlati fra loro dallo stesso riferimento `WMS-MOV-<timestamp>`:

1. **`SMI`** — scarico dalla locazione di origine
2. **`CMI`** — carico sulla locazione di destinazione

> **Attenzione — non è transazionale.** I due movimenti sono chiamate separate. Se il primo riesce e il secondo fallisce (per esempio perché la causale `CMI` non è configurata), la merce risulta scaricata dall'origine e **non** caricata a destinazione. Il messaggio di errore lo indica esplicitamente. In quel caso serve un intervento manuale sul gestionale.

**Contesto di stampa.** `MOVE` (articolo, magazzino/locazione origine e destinazione, quantità).

---

### 5.4 Spostamento carrello — `/move/cart`

**Obiettivo.** Raccogliere fisicamente più articoli da locazioni diverse, portarli via con il carrello, e solo dopo decidere e registrare le destinazioni.

**Il carrello è persistente per operatore.** Viene salvato sul database di appoggio ad ogni riga aggiunta. Se il terminale si spegne o l'operatore cambia dispositivo, ritrovando il suo carrello dove l'aveva lasciato.

**Fase 1 — Accumulo**

Per ogni riga da aggiungere:
1. Scansione articolo
2. Selezione della locazione di origine (elenco ordinato per quantità, oppure scansione diretta)
3. Quantità
4. `AGGIUNGI AL CARRELLO`

Le righe già in carrello sono elencate in alto, ciascuna con il pulsante di eliminazione. In testata: contatore righe e pulsante "svuota carrello".

**Fase 2 — Evasione** (pulsante `EVADI CARRELLO`)

1. Si **selezionano** una o più righe (checkbox; disponibili "Seleziona tutti" e "Deseleziona")
2. Si **scansiona la locazione di destinazione**: viene assegnata a tutte le righe selezionate in un colpo. Le righe tornano deselezionate
3. Si ripete per gli altri gruppi di righe
4. `CONFERMA` esegue tutti gli spostamenti delle righe che hanno una destinazione assegnata

> Se non è selezionata alcuna riga quando si scansiona la destinazione, questa viene assegnata alla prima riga senza destinazione. È una comodità per il caso "una riga alla volta".

**Cosa scrive sul gestionale.** Per **ogni** riga con destinazione: la stessa coppia `SMI` + `CMI` dello spostamento semplice.

**Gestione degli errori.** L'esecuzione è riga per riga. Le righe andate a buon fine vengono rimosse dal carrello; le righe in errore **restano in carrello** con la destinazione assegnata, così si può riprovare. Il messaggio finale elenca gli articoli in errore.

**Contesto di stampa.** `CART` (codice operatore).

---

### 5.5 Accettazione merci — `/acceptance`

**Obiettivo.** Ricevere la merce dei DDT fornitore, ubicarla, documentarla, e chiudere il documento nel gestionale.

**Pannello 1 — Elenco DDT da accettare**

- Campo barcode che accetta **numero documento** oppure **codice articolo** (nel secondo caso apre il DDT che contiene quell'articolo e si posiziona sulla riga)
- Elenco dei documenti pendenti: tipo documento, numero, fornitore, data, avanzamento `righe fatte / totali` e barra di progresso
- Il bordo sinistro è verde quando tutte le righe sono complete
- Pulsante di ricarica manuale

**Pannello 2 — Righe del documento**

Ogni riga è un pannello espandibile. Nella testata:

- Codice articolo con pulsante `I`, descrizione
- **Locazione suggerita** dal gestionale, se presente (in verde con l'icona di posizione)
- Avanzamento `accettato / atteso` e il pulsante **`Versa`**
- Se la riga è completa: icona di conferma verde e icona di stampa etichetta

Espandendo la riga:

- **Elenco dei versamenti già effettuati**: locazione, data/ora, operatore, quantità. Un DDT può essere versato in **più riprese e in più locazioni**
- Pulsante **`Allega foto`**: apre la fotocamera del terminale. Utile per documentare imballi danneggiati o non conformità. Un contatore mostra quante foto sono già allegate alla riga

**Pannello 3 — Versamento di una riga**

1. Testata con atteso e **residuo**
2. Scansione della **locazione di destinazione**. Priorità dei suggerimenti: locazione indicata dal gestionale sulla riga → locazione già usata poco prima → locazione di default per l'accettazione (chip cliccabile)
3. Quantità (default: il residuo)
4. Riepilogo, poi `ACCETTA E CARICA`
5. Dopo il versamento compaiono i pulsanti `ALLEGA FOTO` e `STAMPA ETICHETTA`

Se il versamento completa la riga, si torna automaticamente all'elenco righe. Altrimenti si resta pronti per il versamento successivo, con la quantità impostata sul nuovo residuo.

**Completamento del documento**

Il pulsante `COMPLETA DOCUMENTO` si attiva solo quando **tutte** le righe sono complete oppure sono state esplicitamente **dichiarate mancanti**, e almeno un'azione è stata registrata.

Per dichiarare mancante una riga si tocca il chip corrispondente nella lista delle righe incomplete (il chip diventa rosso e riporta `MANCANTE`; si tocca di nuovo per annullare).

Il completamento chiede una **conferma esplicita**, poi:
1. Aggiorna lo stato del documento nel gestionale al valore configurato per "accettato"
2. Se ci sono anomalie, invia una **notifica** nel gestionale con l'elenco puntuale: quantità sotto l'atteso (`MANCANTE`), quantità sopra l'atteso (`ECCEDENZA`), merce versata in locazione **`NC`** (non conforme)
3. Rimuove il documento dall'elenco

**Cosa scrive sul gestionale.** Per ogni versamento:

1. Se il gestionale indica una locazione di prelievo sulla riga: movimento **`SMI`** di scarico da quella locazione
2. Movimento **`CMI`** di carico sulla locazione di destinazione

Entrambi correlati dal riferimento `WMS-ACC-<documento>-<riga>-<ora>`. Le causali sono configurabili.

**Contesto di stampa.** `ACCEPTANCE`. A livello di documento: numero documento, fornitore, tipo. A livello di etichetta articolo: articolo, locazione, documento, quantità accettata, numero copie, id riga.

---

### 5.6 Inventario — `/inventory`

**Obiettivo.** Conteggio fisico di un magazzino o di una sua zona, con generazione automatica delle rettifiche.

**Schermata 1 — Avvio o ripresa sessione**

- Se esiste già una **sessione aperta** per l'operatore, un avviso in alto mostra magazzino, zona, ora di inizio e avanzamento `contati / totali`, con i pulsanti **`RIPRENDI`** e **`Abbandona`**
- Per una nuova sessione: si scelgono il **magazzino** e, opzionalmente, un **prefisso di locazione** (per esempio `A` o `01-A`; vuoto = tutto il magazzino)
- `AVVIA INVENTARIO` carica dal gestionale tutti gli abbinamenti articolo/locazione di quel perimetro e crea la sessione sul database di appoggio

**Schermata 2 — Lista dei conteggi**

- Testata con magazzino, zona, avanzamento e barra di progresso
- **Due filtri**: `Solo da contare` e `Nascondi stock zero` (attivo di default)
- **Campo barcode** che accetta articolo **o** locazione:
  - articolo presente in **una sola** locazione della lista → apre direttamente il conteggio
  - articolo presente in **più** locazioni → apre un **selettore di locazione** dedicato, con anche il pulsante "Aggiungi locazione per questo articolo"
  - **locazione** → apre il conteggio del primo articolo non ancora contato in quella locazione
  - codice già contato → avviso informativo
  - codice **non presente in lista** → passa alla dichiarazione di articolo extra
- Elenco delle righe ordinato per locazione e articolo, colorato per stato, con etichette `DA CONTARE`, `Δ ±n`, `APPLICATO`, `EXTRA`

**Conteggio di una riga**

Testata con locazione, articolo, descrizione e **giacenza attesa**. Campo quantità con focus automatico (precompilato con la giacenza attesa, o con il valore già contato in precedenza). Se il valore differisce, avviso con il delta calcolato. `SALVA CONTEGGIO` scrive subito sul database di appoggio.

> Salvare un conteggio **non** genera ancora nessun movimento. Si può contare, tornare indietro, ricontare.

**Articolo extra** (trovato fisicamente ma non atteso in quella zona)

1. Avviso con il codice scansionato e la descrizione recuperata dall'anagrafica
2. Scansione della **locazione dove è stato trovato**
3. Quantità
4. `DICHIARA EXTRA` → viene aggiunta una riga con giacenza attesa **zero** e quantità contata quella dichiarata. Genererà quindi una rettifica positiva

**Chiusura — `APPLICA n CONTATI E CHIUDI`**

Prima della conferma, l'applicazione dichiara esattamente cosa succederà:

- quante righe genereranno una **rettifica** verso il gestionale
- quante righe sono **confermate senza differenze** (nessun movimento, solo registrazione della verifica)
- quante righe **non contate** verranno **ignorate**

**Cosa scrive sul gestionale.** Per ogni riga contata e non ancora applicata:

| Situazione | Effetto |
|------------|---------|
| Contato **maggiore** dell'atteso | Movimento con causale **`CINV`** per il delta |
| Contato **minore** dell'atteso | Movimento con causale **`SINV`** per il delta |
| Contato **uguale** all'atteso | Nessun movimento, ma **verifica dell'abbinamento registrata** |

Tutti i movimenti della sessione portano lo stesso riferimento `INV-<id sessione>`.

**Gestione degli errori.** Le righe applicate con successo vengono marcate; quelle in errore restano da applicare. La sessione viene chiusa **solo se non ci sono errori**: se ci sono, l'elenco degli errori viene mostrato e la sessione resta aperta per il ritentativo.

**Contesto di stampa.** `INVENTORY` (magazzino, zona).

---

### 5.7 Prelievo da lista — `/pick/list`

È la transazione più articolata. È il prelievo dei componenti richiesti da una o più bolle di produzione.

**Obiettivo.** Dare all'operatore un giro di prelievo ordinato, tollerante alle interruzioni, con prelievi parziali e da locazioni multiple, e sincronizzazione batch verso il gestionale.

#### Schermata 1 — Selezione della lista

- Interruttore **`Solo assegnate a me`**, **attivo di default**: ogni operatore vede le proprie liste
- **Campo barcode** che accetta:
  - il codice di una lista già esistente → la apre
  - il codice di una **bolla** → **crea una nuova lista** con tutte le righe da prelevare di quella bolla, e la apre. La lista viene auto-assegnata a chi la crea
- Per ogni lista: codice, descrizione, elenco delle bolle contenute, stato (`Aperta` / `In corso`), numero di righe dichiarate mancanti, barra di avanzamento `completati / totali`

#### Schermata 2 — Righe della lista

**Barra strumenti:**

- **Ordinamento** con quattro criteri. Le righe dichiarate mancanti finiscono **sempre in fondo**:

  | Criterio | Uso tipico |
  |----------|-----------|
  | **Tipo handling** (default) | Raggruppa per modalità di movimentazione: pezzi piccoli, pezzi grandi, carpenteria, trasmissioni… È il giro di prelievo più efficiente |
  | **Ubicazione (PAF02)** | Segue il campo di ubicazione dell'anagrafica articolo |
  | **Loc. preferenziale** | Segue la locazione principale di prelievo |
  | **Codice articolo** | Ordine alfabetico |

- Interruttore **`Nascondi completate`**, attivo di default
- Campo barcode articolo: apre direttamente il dialogo della riga corrispondente

**Ogni riga mostra:** codice articolo con pulsante `I`, descrizione, chip della locazione principale (con stella), chip dell'ubicazione da anagrafica se diversa, chip del tipo di handling, chip `Extra` se aggiunta fuori lista, un triangolo di avviso rosso se **non c'è giacenza** disponibile. A destra: `coperto / previsto` con l'unità di misura, oppure l'icona di stato se la riga è completa o mancante.

#### Dialogo di riga

Tre contatori in testata: **Da prelevare** (residuo) · **Staged** (registrato ma non sincronizzato) · **Eseguito** (già sul gestionale).

- **Elenco dei prelievi** già registrati sulla riga, con locazione, quantità e stato (`⏳ staged` / `✓ eseguito`). I prelievi *staged* si possono **eliminare**; quelli eseguiti no
- **Chip delle locazioni con giacenza**, scorrevoli orizzontalmente, ordinati per quantità. La locazione principale ha la stella. Toccando un chip la si seleziona
- **Nuovo prelievo:** scansione locazione (o tocco sul chip) + quantità + `Aggiungi prelievo`
- **`Prelievo auto (<locazione principale>)`** — un tocco: registra tutto il residuo dalla locazione principale
- **`Segnala mancante`** — dichiara la riga non prelevabile. Reversibile con `Annulla mancante`

#### Barra azioni (griglia 2 × 2)

| Pulsante | Effetto |
|----------|---------|
| **Aggiungi riga** | Aggiunge alla lista un articolo **non previsto**, con quantità e bolla di riferimento a scelta fra quelle della lista. La riga viene marcata `Extra` |
| **Preleva tutto** | Per ogni riga non mancante con residuo e con una locazione principale definita, registra automaticamente un prelievo *staged* dell'intero residuo. Non tocca il gestionale |
| **Annulla staged** | Con conferma: elimina **tutti** i prelievi non ancora sincronizzati della lista. I prelievi già eseguiti restano intatti |
| **Fai prelievi** | Con conferma: **sincronizza** tutti i prelievi *staged* sul gestionale |

#### Sincronizzazione — cosa scrive sul gestionale

I prelievi *staged* vengono raggruppati **per bolla**. Per ciascuna bolla:

```
apertura sessione di prelievo nell'ERP
  ├── per ogni prelievo della bolla:
  │      registrazione riga di prelievo
  │        → riga di prelievo bolla/articolo (aggiornata o creata)
  │        → evento di prelievo fisico
  │        → movimento di magazzino con causale SCAR
  └── chiusura della sessione
```

La sessione viene chiusa **anche in caso di errori parziali**. Se l'apertura della sessione fallisce, tutti i prelievi di quella bolla vanno in errore e si passa alla bolla successiva. I prelievi senza bolla di riferimento usano un percorso semplificato: movimento diretto con causale `SCAR`.

Ogni prelievo sincronizzato viene marcato con il numero di movimento e l'id di sessione restituiti dal gestionale.

#### Chiusura della lista — `Chiudi lista`

1. L'applicazione verifica quali bolle della lista hanno una **fase di versamento abilitata** nel gestionale
2. Si apre un dialogo che propone i **versamenti** da registrare. Il default è: proposti come selezionati se tutte le righe della lista sono completate o mancanti; non selezionati se la lista è parziale
3. Per ogni versamento selezionato: apertura sessione, registrazione della riga di versamento (causale di carico), chiusura sessione. Se il versamento è completo, la procedura del gestionale chiude anche la fase e l'ordine di lavoro
4. Se ci sono righe **dichiarate mancanti**, viene inviata una **notifica** nel gestionale con l'elenco puntuale degli articoli mancanti e le quantità previste
5. La lista viene chiusa

**Contesto di stampa.** `PICK` (codice lista, id lista). Il report Crystal collegato elenca gli articoli **ancora da prelevare** dalle bolle della lista, con il riferimento di commessa.

---

### 5.8 Gestione locazioni — `/locations/manage`

**Obiettivo.** Manutenzione dell'anagrafica: creare nuove locazioni e gestire gli abbinamenti articolo–locazione. Disponibile solo con database ERP configurato.

**Scheda 1 — Crea locazione**

Selezione del magazzino (default: il magazzino con ubicazioni, se presente) + codice locazione. Il codice viene normalizzato in maiuscolo. Se la locazione esiste già, errore esplicito senza creare nulla.

**Scheda 2 — Abbina articolo**

1. Scansione articolo → testata e **elenco delle locazioni già abbinate**, con quantità corrente. La locazione principale ha il chip `Principale` con la stella
2. Per ogni locazione abbinata, due azioni:
   - **Imposta principale** (stella) — disponibile solo se non è già principale
   - **Rimuovi abbinamento** — **disabilitato se la locazione ha giacenza diversa da zero**. Il tooltip lo spiega
3. In fondo: **aggiungi una nuova locazione** all'articolo (magazzino + codice locazione)

**Cosa scrive sul gestionale.** Scritture dirette sull'anagrafica locazioni e sull'anagrafica abbinamenti. **Non genera movimenti di magazzino.**

---

### 5.9 Prelievo produzione — `/production/pick`

Transazione precedente alla lista di prelievo, funzionante ma **non raggiungibile dal menu**. Documentata per completezza.

1. Scansione di una **singola bolla** → carica il fabbisogno componenti
2. Filtro `solo da fare`. Le righe mostrano handling, ubicazione, stato (`DA FARE` / `PARZ.` / `✓ OK`)
3. Toccando una riga si aggiunge al carrello locale con la quantità residua (modificabile)
4. Schermata di conferma con il riepilogo del carrello
5. `Esegui` → **una sola sessione di prelievo** per tutto il batch, una riga per articolo, poi chiusura sessione e ricarica della bolla

**Prerequisito.** Il **nodo dispositivo** deve essere inizializzato. Se non lo è, l'esecuzione viene bloccata con un messaggio che invita a ricaricare la pagina.

**Differenze rispetto a `/pick/list`:** nessuno staging (il carrello è solo in memoria del circuito), una bolla per volta, nessun database di appoggio, nessuna dichiarazione di mancante, nessun versamento in chiusura.

---

## 6. Stampa etichette

### 6.1 Come si stampa

L'icona **stampante** compare nella barra in alto **solo** quando la schermata corrente ha un contesto di stampa attivo. Toccandola si apre il dialogo di stampa:

1. **Report** — tendina, se per quel contesto sono configurati più report; altrimenti il nome del report in chiaro
2. **Stampante** — campo con ricerca fra le stampanti installate sul server. Il valore proposto segue questa priorità:
   1. l'ultima stampante usata **su questo terminale per questo report** (ricordata dal browser)
   2. la stampante configurata sul template
   3. la stampante di default aziendale

   Il testo di aiuto sotto il campo dice quale delle due sta usando ("Default aziendale" / "Default terminale")
3. **Parametri del report** — i parametri che l'applicazione conosce sono **precompilati e in sola lettura**, con un'icona a stellina. Gli altri sono da compilare a mano
4. **Numero etichette** — precompilato quando la schermata sa quante copie servono (per esempio nell'accettazione: una per pezzo)
5. **`STAMPA`**

Al termine, la stampante scelta viene ricordata sul terminale per quel report.

### 6.2 Contesti di stampa e parametri precompilati

| Contesto | Da dove | Parametri precompilabili |
|----------|---------|--------------------------|
| `ARTICLE` | Interrogazione, articolo trovato | `PACOD` |
| `LOCATION` | Interrogazione, locazione trovata | `LCCOD`, `MGCOD` |
| `MOVE` | Spostamento semplice, dopo la conferma | `PACOD`, `MGCOD_SRC`, `LCCOD_SRC`, `MGCOD_DST`, `LCCOD_DST`, `QTY` |
| `CART` | Spostamento carrello | `OPCOD` |
| `INVENTORY` | Inventario, sessione attiva | `MGCOD`, `ZONE` |
| `ACCEPTANCE` | Accettazione, documento selezionato | `DOCRIF`, `SUPPLIER`, `DOCTYPE` |
| `ACCEPTANCE` | Accettazione, etichetta di riga | `PACOD`, `LCCOD`, `DOCRIF`, `QTAACC`, `COPIES`, `IDRIG` |
| `ADJUSTMENT` | Rettifiche, dopo la conferma | `PACOD`, `MGCOD`, `LCCOD`, `QTY` |
| `PICK` | Prelievo da lista, lista aperta | `LISTCODE`, `LISTID` |

`COPIES` è un parametro speciale: se presente, imposta il numero di etichette proposto nel dialogo.

### 6.3 Configurazione dei template — `/config`

Raggiungibile dal menu utente in alto a destra, voce **Configurazioni**.

Per ogni template si definiscono: **contesto**, **nome** visualizzato, **nome del report**, **stampante** (opzionale), flag **attivo**, e l'elenco dei **parametri** con nome tecnico, etichetta, chiave di precompilazione, obbligatorietà, ordinamento.

Se per un contesto non esiste nessun template, il dialogo di stampa lo dice esplicitamente e indica quale contesto configurare.

---

## 7. Area amministrazione

Raggiungibile da `/admin` (redirige a `/admin/inventory`). Interfaccia desktop, non mobile: menu laterale fisso, tabelle con ricerca e paginazione.

**Autorizzazione.** L'accesso è consentito solo se il **gruppo** dell'operatore, letto dall'anagrafica ERP al login, è fra quelli abilitati in configurazione. Altrimenti la pagina mostra `Accesso non autorizzato` e nessun dato.

### 7.1 Sessioni inventario — `/admin/inventory`

Tabella di tutte le sessioni: operatore, magazzino, zona, inizio, fine, numero articoli (con quanti applicati), quantità totale, stato `Aperto` / `Chiuso`. Ricerca su operatore, magazzino, zona.

### 7.2 Liste di prelievo — `/admin/picklists`

Tabella di tutte le liste: codice/bolla, descrizione, creata da, assegnata a, data, avanzamento righe (con evidenza delle mancanti), stato. Ricerca su codice, bolla, operatore.

Due azioni:

- **`Nuova Lista`** — permette di comporre una lista **da più bolle**, con anteprima delle righe e **selezione puntuale** di quali includere. Il codice della lista è a scelta; se lasciato vuoto viene usato quello della prima bolla. È la modalità di lavoro del capo-reparto, che prepara i giri di prelievo per gli operatori
- **`Aggiungi bolla`** (sulle liste aperte o in corso) — accoda le righe di un'altra bolla a una lista esistente

### 7.3 Accettazioni — `/admin/acceptance`

Storico dei versamenti di accettazione registrati dal WMS, con documento, articolo, locazione, quantità, operatore, data e riferimento al movimento generato nel gestionale.

### 7.4 Verifiche locazioni — `/admin/verifications`

Report di controllo sulla qualità dell'anagrafica giacenze: **quando e da chi è stato verificato per l'ultima volta ogni abbinamento articolo/locazione**. Alimentato automaticamente da rettifiche e inventari.

**Filtri:** magazzino, prefisso locazione, famiglia merceologica, intervallo di date di verifica, interruttore **`Mai verif.`**.

> Tendine e interruttore applicano subito; i campi di testo e le date richiedono `APPLICA`, perché si compilano a più battute. Cambiando filtro la tabella torna alla prima pagina. Con `Mai verif.` attivo il filtro sulle date è ininfluente (una riga mai verificata non ha data) e un avviso lo segnala.

**Colonne:** articolo (con stella se è la locazione principale), descrizione, famiglia, magazzino, locazione, quantità, ultima verifica, verificata da.

L'ordinamento di default mette **per prime le righe mai verificate**. Il chip con i giorni trascorsi è colorato: verde fino a 180 giorni, arancione oltre, rosso oltre l'anno.

In fondo alla barra filtri, il conteggio totale degli abbinamenti che soddisfano i filtri.

---

## 8. Errori frequenti e cosa fare

| Messaggio | Causa | Cosa fare |
|-----------|-------|-----------|
| `Articolo <codice> non trovato` | Il codice non esiste in anagrafica, o il barcode è stato letto male | Rileggere; verificare il codice sul gestionale |
| `Locazione <codice> non trovata in anagrafica` | La locazione non esiste | Crearla da **Gestione Locazioni**, scheda "Crea locazione" |
| `Giacenza zero in questa locazione` | Origine dello spostamento vuota | Scegliere un'altra locazione di origine, o fare prima una rettifica |
| `Errore connessione DB` | Rete o database non raggiungibili | Verificare il WiFi; se persiste, segnalare all'IT |
| `Scarico origine fallito (IDMOV=-1 — causale SMI valida?)` | La causale non è configurata correttamente nel gestionale, o non è di tipo carico/scarico | **Segnalare a Intesi.** Non ritentare: verificare prima sul gestionale se il movimento è passato |
| `Carico destinazione fallito` | Come sopra, sul secondo movimento | **Situazione da sanare a mano:** la merce è stata scaricata dall'origine ma non caricata a destinazione |
| `abbinamento <mag>/<loc> non presente, verifica non registrata` | La rettifica è andata a buon fine ma l'abbinamento articolo/locazione non esisteva | Nessuna azione: informativo |
| `Nodo dispositivo non inizializzato` | Il terminale non si è registrato nel gestionale | Ricaricare la pagina |
| `Nessun template configurato per il contesto X` | Manca la configurazione di stampa | Configurare il template da **Configurazioni** |
| `Servizio stampa non configurato` | Manca l'URL del servizio di stampa | Segnalare all'IT |
| `Accesso non autorizzato` (area admin) | Il gruppo dell'operatore non è abilitato | Richiedere l'abilitazione |
| `Nessun articolo trovato per la zona selezionata` | Il prefisso di locazione non intercetta nulla | Verificare il prefisso, o lasciarlo vuoto |
| `Bolla <codice> non trovata o senza righe da prelevare` | La bolla non esiste, è chiusa, o non ha fabbisogni residui | Verificare lo stato della bolla sul gestionale |

---
---

# PARTE 2 — DOCUMENTAZIONE TECNICA

## 9. Stack e architettura

### 9.1 Stack

| Livello | Tecnologia |
|---------|-----------|
| Runtime | **.NET 10** |
| Modello applicativo | **Blazor Web App**, render mode **InteractiveServer** (Blazor Server su SignalR) |
| UI | **MudBlazor 9.x** — tema dark, palette brand Mecmar |
| Accesso dati | **Dapper 2.x** su **Microsoft.Data.SqlClient 5.x** — nessun ORM, nessuna migration automatica sull'ERP |
| Stampa | `System.Drawing.Common` per l'enumerazione delle stampanti locali |
| Database | **SQL Server** — due database: ERP di lettura/scrittura controllata, e database di appoggio proprietario |
| Hosting | **IIS** con ASP.NET Core Hosting Bundle |
| Cultura | forzata a **`it-IT`** su tutti i thread all'avvio |

### 9.2 Struttura del progetto

```
Components/
  App.razor                 shell HTML, base href, CSS/JS MudBlazor
  Routes.razor              @rendermode InteractiveServer — GLOBALE
  Layout/
    MainLayout.razor        tema, app bar, redirect login, init nodo dispositivo
    AdminLayout.razor       layout desktop area admin (drawer + tema verde scuro)
    ReconnectModal.razor    UI di riconnessione SignalR (+ .css e .js scoped)
  Pages/                    una pagina per transazione
  Pages/Admin/              area amministrazione
  Shared/                   BarcodeInput, StockBadge, WmsTile, ArticleInfoButton,
                            ArticleInfoDialog, PickRowDialog, PrintDialog,
                            CloseListDialog, AddExtraRowDialog, ConfirmDialog
Services/
  SessionService.cs         Scoped   — operatore, gruppo, nodo, carrello, contesto stampa
  ErpService.cs             Singleton — tutto l'accesso al DB ERP (~1750 righe)
  LogicService.cs           Singleton — tutto l'accesso al DB di appoggio (~1250 righe)
  PickListService.cs        Singleton — orchestrazione liste di prelievo (ERP + appoggio)
  AcceptanceService.cs      Singleton — orchestrazione accettazione (ERP + appoggio)
  PrintService.cs           HttpClient tipizzato — routing verso Printer Manager o Crystal
  MockWmsService.cs         Singleton — dataset finto per demo/sviluppo
  WmsOptions / PickListOptions / AcceptanceOptions   binding di configurazione
Models/
  WmsModels.cs              tutti i DTO, record ed enum condivisi
sql/                        script DDL per ERP e database di appoggio
docs/                       requisiti, reference ERP, PRD, questa pagina
wwwroot/                    app.css, logo, manifest PWA, service worker, Bootstrap
tools/setup-https.ps1       utility per certificato di sviluppo
```

### 9.3 Ciclo di vita dei servizi

| Servizio | Lifetime | Perché |
|----------|----------|--------|
| `SessionService` | **Scoped** | In Blazor Server lo scope coincide con il **circuito SignalR**, quindi con la sessione utente. Contiene lo stato per utente. **Non renderlo singleton** |
| `ErpService`, `LogicService`, `PickListService`, `AcceptanceService`, `MockWmsService` | **Singleton** | Stateless. Le connessioni SQL vengono aperte e chiuse per ogni chiamata (`using var db = Open()`), quindi il pooling di ADO.NET fa il suo lavoro |
| `PrintService` | **HttpClient tipizzato** | Registrato con `AddHttpClient<PrintService>` per la gestione corretta di `HttpClient` |

`MockWmsService` è singleton con **stato mutabile condiviso fra sessioni**. È intenzionale per le demo, ma va tenuto presente: in modalità demo due operatori si vedono le modifiche a vicenda.

### 9.4 Regole di render mode

- `@rendermode InteractiveServer` va **solo su `Routes.razor`** — da lì si propaga a tutto l'albero
- **Mai su `MainLayout`**: il layout riceve `Body` come `RenderFragment` e la serializzazione dei parametri fallisce a runtime
- **Mai sulle singole pagine**: è ridondante

### 9.5 Note su Program.cs

- `DetailedErrors = true` sui componenti interattivi — utile in campo, da valutare in produzione
- Snackbar configurate in basso al centro, visibili 2,5 s
- `ForwardedHeaders` abilitato per `X-Forwarded-For` e `X-Forwarded-Proto`, con `KnownProxies` e `KnownIPNetworks` **svuotati** (necessario dietro IIS)
- **`UseHttpsRedirection` deliberatamente assente**: la terminazione HTTPS è di IIS
- `LogicService.EnsureSchemaAsync()` viene invocato **all'avvio**: crea il database di appoggio e le tabelle se mancano
- `UseStatusCodePagesWithReExecute("/not-found")` per il 404
- Content type aggiuntivi registrati: `.cer`, `.webmanifest`

---

## 10. Modello dati

### 10.1 Database ERP — `FactoryMecmar`

Connection string: `ConnectionStrings:ErpDatabase`.

> **Il nome del database non è fisso.** Non va cablato nel codice né negli script condivisi. Gli script cross-database (in particolare `V007`) richiedono la sostituzione manuale del nome.

**Tabelle standard Intesi usate in lettura:**

| Tabella | Contenuto | Campi rilevanti |
|---------|-----------|-----------------|
| `A_PAR` | Anagrafica articoli | `PACOD`, `PADSC`, `PAUDM`, **`FMCOD`** (famiglia), `PAF01`, `PAF02` (ubicazione), `PAPDR` (scorta minima), `IDPAR` |
| `A_FAM` | Famiglie merceologiche | `FMCOD`, `FMDSC` |
| `A_LOC` | Anagrafica locazioni | `MGCOD` + `LCCOD` (chiave composta) |
| `A_MAG` | Anagrafica magazzini | codice, descrizione |
| `L_MLPA` | **Abbinamenti articolo/magazzino/locazione con quantità** | `PACOD`, `MGCOD`, `LCCOD`, `QTLOC`, `QTMIN`, `QTMAX`, `LCPRC` (`'Y'` = principale), `LASTUPDATE` |
| `L_PAQT` | Quantità aggregate per articolo | ordinato, impegnato |
| `S_MOV` | Movimenti di magazzino | `PACOD`, `MGCOD`, `LCCOD`, `CMCOD`, `MOQTA`/`MOQTV`, `MOSTP`, `OPCOD`, `IDTBR`, `IDRIF` |
| `A_CMM` | Causali | `CMCOD`, `CMDSC`, **`CMTYP`** (`1` carico, `-1` scarico, `0` altro) |
| `A_OPR` | Operatori | `OPCOD`, `OPDSC`, `OPPSW`, `OPPWR`, `GRCOD` |
| `A_NOD` | Nodi/terminali | `CDNOD`, `PRDCD` |
| `A_DOC` | Documenti articolo (PDF, immagini) | join su `IDPAR`; path completo in `NTDOC`; flag `ShowInPreview`, `DefaultPreview` |
| `A_DOT` / `A_DOR` | Testate e righe documenti | `IDTES`, `DTDO`, `DTSO`/`DTSTO`, `DTCOD`, `DTDTE`, `IDRIG`, `PACOD`, `DRDSC`, `DRUMI`, `DRQTI` |
| `L_DRCR` | Locazione di prelievo per riga documento | `MGCOD`, `LCCOD` |
| `A_FOR` | Fornitori | `IDFOR`, `FORAG` |
| `S_ODL` / `L_ODLA` | Ordini di lavoro e loro articolazione | `OLCOD`, `OLSTF`, `CONUM`, `LOCOD` |
| `A_LOT` | Lotti di produzione | `CONUM`, `LOCOD`, `LOCLP`, `PACOD`, `LOQTP`, `LOQDB` |
| `A_LAV` | Fasi di lavorazione | `OLCOD`, `FAABV`, `LAUFC` |
| `L_CMFE` | Componenti esterni di commessa | `IDCMFE`, `CONUM`, `LOCLP`, `PACOD`, `LOQTP` |
| `a_THP` | Tipi di handling | `IDTHP`, `THDSC` |
| `S_SES` | Sessioni di prelievo/versamento | `IDSES`, `SESTO` (`1` aperta, `2` chiusa) |
| `S_PAP` / `S_SPP` | Righe e eventi di prelievo | `PAQTP`, `TPREC` |
| `S_PAV` / `S_SPV` | Righe e eventi di versamento | |
| `S_NTF` / `A_NTF` | Notifiche e loro catalogo | `IDNTF` |

**Stored procedure standard Intesi:**

| SP | Uso |
|----|-----|
| **`TRD_InsertMov`** | **Unico punto di scrittura dei movimenti.** Vedi §11.1 |
| `GetProgressivo` | Assegnazione progressivi univoci |
| `CompGenerateNotify` | Generazione notifiche |

**Colonne custom aggiunte dal WMS** (prefisso `X_`, script `V010`):

| Colonna | Contenuto |
|---------|-----------|
| `L_MLPA.X_VerifiedUser` | `OPCOD` dell'operatore che ha verificato l'abbinamento |
| `L_MLPA.X_VerifiedDate` | Data/ora della verifica |

> **Perché non riusare `L_MLPA.LASTUPDATE`.** È gestita dal trigger `TRG_ON_UPDATE_MLPA` e cambia **solo** quando cambiano `QTLOC`, `QTMAX` o `QTMIN`. Non è "l'ultima volta che qualcuno ha guardato la riga". Lo stamp di verifica scrive quindi solo le due colonne custom, lasciando `LASTUPDATE` come timestamp dell'ultima variazione di quantità.

**Trigger ERP di cui il WMS dipende:**

| Trigger | Effetto |
|---------|---------|
| `TRG_ON_INSERT_SPP` | Aggiorna automaticamente `S_PAP.PAQTP` a fronte di un evento di prelievo |
| `TRG_ON_UPDATE_MLPA` | Gestisce `L_MLPA.LASTUPDATE` |

### 10.2 Database di appoggio — `Logic`

Connection string: `ConnectionStrings:LogicDatabase`.

Creato — database incluso — e mantenuto da `LogicService.EnsureSchemaAsync()` all'avvio dell'applicazione. Tutto in modalità `IF NOT EXISTS`, quindi **idempotente** e sicuro da rieseguire. Include anche `ALTER TABLE ADD COLUMN` condizionali per le migrazioni di schema già rilasciate.

| Tabella | Contenuto | Note |
|---------|-----------|------|
| `WMS_Cart` | Carrello persistente per operatore | Righe **senza** destinazione = in accumulo; **con** destinazione = pronte per l'evasione. La riga viene eliminata a movimento riuscito. Indice su `OperatorCode` |
| `WMS_InvSession` | Sessioni di inventario | `ClosedAt` nullo = sessione aperta |
| `WMS_InvCount` | Righe di conteggio | `ExpectedQty`, `CountedQty` (nullo = non contata), `Applied`. Indice su `SessionId` |
| `WMS_PickList` | Testate liste di prelievo | `Status` (`Open` / `InProgress` / …), `CreatedByOp`, `AssignedOperator` |
| `WMS_PickListRow` | Righe di lista | `PlannedQty`, `OlCod`, `Handling`, `IdSpec`, `IdGroup`, `Paf02`, `MainWarehouseCode`, `MainLocationCode`, `IsMissing`, `IsExtraItem`, `RowStatus`, `SortOrder`. Cascata da `WMS_PickList` |
| `WMS_PickListPick` | **Prelievi (staged ed eseguiti)** | `ExecutedAt` nullo = *staged*. A esecuzione: `ErpMovId` e `ErpSesId`. Cascata da `WMS_PickListRow` |
| `WMS_AcceptanceLine` | Versamenti di accettazione | `ErpDocId` + `ErpLineId` legano alla riga documento ERP; `ErpMovId` al movimento generato. Indici su documento e su documento+riga |
| `WMS_AcceptancePhoto` | Metadati delle foto di accettazione | Il file sta su disco; qui `FileName`, operatore, data. Indice su documento+riga |
| `WMS_PrintTemplate` | Template di stampa | `Context`, `Name`, `ReportName`, `PrinterName`, `IsActive` |
| `WMS_PrintTemplateParam` | Parametri dei template | `ParamName`, `AutoFillKey`, `Label`, `IsRequired`, `SortOrder`. Cascata dal template |

> **Nota terminologica.** Nel README e in alcuni commenti la tabella dei prelievi è chiamata `WMS_StagedPick`. Il nome reale a schema è **`WMS_PickListPick`**.

### 10.3 Regola magazzino da locazione

Regola Mecmar codificata in `WmsWarehouse`:

```
locazione "01"          → magazzino "01"  (generico, senza ubicazioni)
qualsiasi altra locazione → magazzino "MEC" (con ubicazioni)
```

Usata quando il magazzino non è noto dal contesto (per esempio quando l'operatore scansiona una locazione a mano nel dialogo di prelievo).

---

## 11. Causali di movimento e regole ERP

### 11.1 `TRD_InsertMov` — il contratto

**Mai scrivere direttamente su `S_MOV`.**

Firma (i primi tre parametri sono obbligatori, tutti gli altri opzionali):

```sql
TRD_InsertMov
  @sPACOD,          -- codice articolo
  @sCMCOD,          -- causale
  @fMOQTV,          -- quantità SEMPRE POSITIVA: il segno lo determina CMTYP
  @sMGCOD  = '',    -- '' → usa la locazione principale dell'articolo (LCPRC='Y')
  @sLCCOD  = '',
  @iIDTBR  = NULL,  -- tipo di riferimento
  @iIDRIF  = NULL,  -- id del riferimento
  @iMoveRect = 0,
  @nodeID  = 0,
  @operatorCode = NULL,
  @referenceCode = NULL,
  @movementDate  = NULL,
  @sCOCOD = NULL, @sOLCOD = NULL, @iIDCRN = NULL, @iPLCOD = NULL,
  @fPrice = 0, @PackageId = NULL, @PackageCode = NULL, @IDPAR = NULL
```

Comportamento:

- **Valore di ritorno**: `IDMOV` (intero positivo) in caso di successo, **`-1`** se `CMTYP` della causale non è `±1` — cioè causale non valida per un movimento di magazzino. In quel caso **non viene scritto nulla**
- `MGCOD`/`LCCOD` vuoti → usa la locazione principale dell'articolo
- `S_MOV.MOSTP` = `@movementDate` oppure `CURRENT_TIMESTAMP`. **`DTDOC` non viene scritta** dalla stored procedure

Nel codice il valore di ritorno viene letto con un parametro Dapper di direzione `ReturnValue` (`@ReturnVal`).

### 11.2 Tabella delle causali usate dal WMS

| Causale | Significato | Usata da | Riferimento generato |
|---------|-------------|----------|----------------------|
| **`SMI`** | Scarico per movimentazione interna | Spostamento semplice e carrello (origine); accettazione (scarico dal magazzino documento) | `WMS-MOV-<yyyyMMddHHmmss>` · `WMS-ACC-<doc>-<riga>-<HHmmss>` |
| **`CMI`** | Carico per movimentazione interna | Spostamento semplice e carrello (destinazione); accettazione (carico a destinazione) | stesso riferimento del `SMI` correlato |
| **`REP`** | Rettifica positiva | Rettifiche semplici, delta > 0 | `WMS-RTT-<yyyyMMddHHmmss>` |
| **`REN`** | Rettifica negativa | Rettifiche semplici, delta < 0 | `WMS-RTT-<yyyyMMddHHmmss>` |
| **`CINV`** | Carico da inventario | Inventario, delta > 0 | `INV-<id sessione troncato a 12 char>` |
| **`SINV`** | Scarico da inventario | Inventario, delta < 0 | `INV-<id sessione troncato a 12 char>` |
| **`SCAR`** | Scarico per produzione | Prelievo (via stored procedure, oppure diretto per i prelievi senza bolla) | `IDTBR = 5`, `IDRIF = IDSPP` |
| **`CAR`** | Carico da produzione | Versamento (via stored procedure) | |

Le causali di accettazione (`LoadCausal`, `DischargeCausal`) sono **configurabili** in appsettings.

### 11.3 Comportamento a delta zero

Sia le rettifiche semplici sia l'inventario trattano il caso "quantità confermata, nessuna differenza" in modo esplicito:

- **nessun movimento** generato (sarebbe un movimento a quantità zero, inutile e rumoroso in `S_MOV`)
- ma **la verifica dell'abbinamento viene registrata** su `L_MLPA.X_VerifiedUser` / `X_VerifiedDate`

Nel caso con delta diverso da zero, **lo stamp segue il movimento**, non lo precede: `TRD_InsertMov` può aver **creato** l'abbinamento in `L_MLPA` (per esempio nel caso di un articolo extra dichiarato in inventario). Un errore nello stamp viene loggato e segnalato nel messaggio all'operatore, ma **non annulla la rettifica**, che è già registrata nel gestionale.

Lo stamp restituisce `false` se nessuna riga è stata aggiornata, cioè se l'abbinamento non esiste. Il messaggio all'operatore lo dichiara.

### 11.4 Sequenza di prelievo produzione

```
WMS_OpenPickSession  (@OLCOD, @OPCOD, @NOCOD)  →  @IDSES     [S_SES.SESTO = 1]

  WMS_InsertPickLine (@OLCOD, @PACOD, …, @IDSES)  →  @IDMOV   ← per ogni articolo
    ├── S_PAP  upsert     riga di prelievo bolla + articolo
    ├── S_SPP  insert     evento di prelievo fisico (TPREC = 1)
    └── TRD_InsertMov     causale SCAR, IDTBR = 5, IDRIF = IDSPP

WMS_ClosePickSession (@IDSES)                              [S_SES.SESTO = 2]
```

Il trigger `TRG_ON_INSERT_SPP` aggiorna automaticamente `S_PAP.PAQTP`: il WMS non deve toccarlo.

### 11.5 Sequenza di versamento

```
WMS_OpenPickSession       (@OLCOD di versamento, @OPCOD, @NOCOD)  →  @IDSES

  WMS_InsertVersamentoLine (…, @IDSES)
    ├── S_PAV  riga di versamento
    ├── S_SPV  evento di versamento
    └── TRD_InsertMov      causale CAR
    └── se il versamento è COMPLETO: chiude anche A_LAV e S_ODL

WMS_ClosePickSession      (@IDSES)
```

Solo le bolle con `A_LAV.FAABV = 'Y'` **e** `LAUFC = 'Y'` sono candidate al versamento.

### 11.6 Notifiche verso il gestionale

`ErpService.CompGenerateNotifyAsync(idNtf, cdNtf, ntNtf, opCod)` genera una notifica in `S_NTF`.

| Evento | `IDNTF` di default | Chiave di configurazione | Contenuto |
|--------|--------------------|--------------------------|-----------|
| Anomalie di accettazione | `101` | `Acceptance:AnomalyNotificationId` | Righe con quantità sotto o sopra l'atteso, e merce versata in locazione `NC` |
| Righe mancanti alla chiusura lista | `102` | `PickList:MissingNotificationId` | Elenco degli articoli dichiarati mancanti con la quantità prevista |

Valore `0` = notifiche disabilitate. I valori validi sono nel catalogo `A_NTF` del database ERP.

---

## 12. Mappa transazione → oggetti database

| Transazione | Route | Legge da | Scrive su ERP | Scrive su appoggio |
|-------------|-------|----------|----------------|--------------------|
| Login | `/login` | `A_OPR` | `A_NOD` (aggiornamento login del nodo) | — |
| Interrogazione | `/query` | `A_PAR`, `L_MLPA`, `S_MOV`, `A_LOC`, `A_MAG`, `L_PAQT`, viste `WMS_V_ArticleOrders_Engaged` / `WMS_V_ArticleDocuments` | — | — |
| Rettifiche | `/adjustments` | `A_PAR`, `L_MLPA`, `A_LOC` | `TRD_InsertMov` `REP`/`REN` · `L_MLPA.X_Verified*` | — |
| Spostamento semplice | `/move/simple` | `A_PAR`, `L_MLPA`, `A_LOC` | `TRD_InsertMov` `SMI` + `CMI` | — |
| Spostamento carrello | `/move/cart` | come sopra | `TRD_InsertMov` `SMI` + `CMI` per riga | `WMS_Cart` |
| Accettazione | `/acceptance` | `A_DOT`, `A_DOR`, `L_DRCR`, `A_FOR`, `A_LOC`, viste `WMS_V_AcceptanceDocs` | `TRD_InsertMov` (`SMI`) + `CMI` · `A_DOT.DTSO` · `S_NTF` | `WMS_AcceptanceLine`, `WMS_AcceptancePhoto` |
| Inventario | `/inventory` | `L_MLPA`, `A_PAR`, `A_MAG` | `TRD_InsertMov` `CINV`/`SINV` · `L_MLPA.X_Verified*` | `WMS_InvSession`, `WMS_InvCount` |
| Prelievo da lista | `/pick/list` | vista `WMS_V_PickList`, `L_MLPA`, `A_PAR`, `A_LAV`, `A_LOT` | `WMS_OpenPickSession` / `WMS_InsertPickLine` / `WMS_ClosePickSession` · `WMS_InsertVersamentoLine` · `TRD_InsertMov` `SCAR` (fallback) · `S_NTF` | `WMS_PickList`, `WMS_PickListRow`, `WMS_PickListPick` |
| Prelievo produzione | `/production/pick` | vista `WMS_V_PickList` | `WMS_OpenPickSession` / `WMS_InsertPickLine` / `WMS_ClosePickSession` | — |
| Gestione locazioni | `/locations/manage` | `A_LOC`, `A_MAG`, `L_MLPA`, `A_PAR` | `A_LOC` (insert) · `L_MLPA` (insert / delete / flag `LCPRC`) | — |
| Configurazione stampa | `/config` | — | — | `WMS_PrintTemplate`, `WMS_PrintTemplateParam` |
| Admin inventari | `/admin/inventory` | — | — | `WMS_InvSession`, `WMS_InvCount` (lettura) |
| Admin liste | `/admin/picklists` | vista `WMS_V_PickList` | — | `WMS_PickList`, `WMS_PickListRow` |
| Admin accettazioni | `/admin/acceptance` | — | — | `WMS_AcceptanceLine` (lettura) |
| Admin verifiche | `/admin/verifications` | `L_MLPA`, `A_PAR`, `A_FAM`, `A_OPR` | — | — |

### Nota sulla paginazione delle verifiche

`GetLocationVerificationsAsync` esegue una **`QueryMultiple`** con due statement: prima il `COUNT(*)`, poi la pagina di righe con `OFFSET`/`FETCH NEXT`. Clausole `FROM` e `WHERE` sono condivise fra i due statement per costruzione, così non possono divergere. Il filtro `verifiedTo` è **inclusivo sul giorno** indicato (implementato come `< data + 1 giorno`).

---

## 13. Sottosistema di stampa

### 13.1 Routing

`PrintService.PrintAsync` sceglie il canale così:

```
se  PrintService:IntesiPrinterManagerUrl è valorizzato
    E il nome del report NON termina con ".rpt"
  → Intesi Printer Manager
altrimenti se PrintService:BaseUrl è valorizzato
  → servizio Crystal legacy
altrimenti
  → errore "Nessun servizio di stampa configurato"
```

Le due modalità sono **combinabili**: con l'URL del Printer Manager attivo, passando un path `.rpt` nel nome del report si forza il canale legacy per quella singola stampa.

`PrintService.ActiveChannel` restituisce `"PM"`, `"Legacy"` o `""` e serve per la diagnostica.

### 13.2 Payload Intesi Printer Manager

`POST` verso `IntesiPrinterManagerUrl` + `PrinterManagerEndpointPath` (se valorizzato), con header `x-http-method-override: PrintReport` e `Accept: application/json`:

```json
{
  "opts": {
    "printModelCode": "<ReportName del template>",
    "userCode":       "<PrintService:UserCode, default 'WMS'>",
    "printerName":    "<override richiesta | template.PrinterName | default config>",
    "description_1":  "<titolo della pagina corrente>",
    "description_2":  null,
    "copyQuantities": <numero copie>,
    "jsonParams":     "<parametri serializzati in stringa JSON>"
  }
}
```

Il payload completo viene **loggato a livello Information** ad ogni stampa. In caso di risposta non 2xx, status e corpo vengono loggati a livello Warning e restituiti all'operatore.

### 13.3 Payload legacy Crystal

`POST` verso `BaseUrl` + `/print`:

```json
{
  "report":     "<ReportName>",
  "printer":    "<override | template.PrinterName | default config>",
  "parameters": { "PACOD": "…", "LCCOD": "…" }
}
```

### 13.4 Selezione della stampante

Priorità, dalla più alta:

1. `localStorage` del browser, chiave **`wms-printer-<ReportName>`** — la scelta dell'operatore su quel terminale per quel report
2. `WMS_PrintTemplate.PrinterName` — la stampante del template
3. `PrintService:IntesiPrinterManagerPrinter` — il default aziendale

La scelta viene salvata in `localStorage` ad ogni stampa riuscita. L'elenco proposto nell'autocomplete viene da `PrinterSettings.InstalledPrinters`, cioè le **stampanti installate sul server IIS**, non sul terminale.

### 13.5 Precompilazione dei parametri

Ogni pagina, quando ha un contesto significativo, chiama `SessionService.SetPrintContext(contesto, dizionario)`. Il dialogo di stampa carica i template attivi per quel contesto e, per ogni parametro con `AutoFillKey` presente nel dizionario, lo precompila e lo rende **in sola lettura** con l'icona a stellina.

Il contesto va **azzerato** (`ClearPrintContext`) quando la pagina esce dalla condizione che lo rendeva valido — per esempio tornando dalla lista righe all'elenco documenti in accettazione. È ciò che fa comparire e sparire l'icona di stampa nella barra in alto.

---

## 14. Nodo dispositivo, sessione e autenticazione

### 14.1 Registrazione del nodo

`MainLayout.OnAfterRenderAsync` (solo al primo render, una volta per circuito):

1. Legge `wms-node-id` da `localStorage`
2. Ricava il nome del dispositivo dallo `userAgent`, troncato a 30 caratteri
3. Chiama `ErpService.RegisterOrGetNodeAsync(cdnodDaStorage, nomeDispositivo)`:
   - se il `CDNOD` esiste già, ne aggiorna il nome e lo restituisce
   - se è `0`, **crea un nuovo nodo** in `A_NOD` con `PRDCD = 10006` e restituisce il `CDNOD` assegnato
4. Se il valore restituito è diverso da quello memorizzato, lo riscrive in `localStorage`
5. Lo espone su `SessionService.NodeId`

Il nodo **non è critico**: se la registrazione fallisce viene solo loggato un warning. Le uniche transazioni che lo richiedono sono quelle che aprono sessioni di prelievo/versamento; `/production/pick` verifica esplicitamente `NodeReady` prima di eseguire.

### 14.2 Login

`ErpService.OperatorRequiresPinAsync(opcod)` determina se l'operatore ha una password configurata. Se non ne ha, il login procede senza PIN.

`ErpService.TryLoginAsync(opcod, pin)` restituisce `(Ok, Name, Role, GroupCode)`. `GroupCode` proviene da `A_OPR.GRCOD` e determina l'accesso all'area admin.

Dopo il login riuscito vengono scritte in `localStorage` quattro chiavi: `wms-op-code`, `wms-op-name`, `wms-op-role`, `wms-op-group`. `MainLayout` e `AdminLayout` le rileggono al primo render per ripristinare la sessione senza nuovo login.

Se non c'è sessione e non siamo sulla pagina di login, `MainLayout` reindirizza a `/login`.

> **Bug noto.** `MainLayout.Logout()` rimuove `wms-op-code`, `wms-op-name` e `wms-op-role` ma **non** `wms-op-group`. La chiave orfana resta nel browser. Non è sfruttabile — `TryRestoreSessionAsync` richiede tutte e tre le prime chiavi per ripristinare — ma va sistemata.

### 14.3 Autorizzazione area admin

Non c'è un sistema di ruoli strutturato. Ogni pagina admin verifica, al primo render e a ogni cambio di sessione:

```csharp
var allowed = Config.GetSection("Admin:AllowedGroups").Get<string[]>() ?? [];
_authorized = allowed.Contains(Session.GroupCode, StringComparer.OrdinalIgnoreCase);
```

`SessionService` espone anche `IsSupervisor` (`OperatorRole` in `SUP` o `ADM`), oggi non usato per gating.

> **Limite di sicurezza da tenere presente.** Il controllo è **solo lato server nel componente**, ma la pagina è raggiungibile: un operatore non autorizzato che apre `/admin/verifications` vede il messaggio di rifiuto e nessun dato, perché `LoadServerData` esce subito se `_authorized` è falso. Non c'è però un middleware di autorizzazione, e la sessione è ricostruita da `localStorage` senza firma: un utente che manipoli `localStorage` con un `wms-op-group` abilitato accede all'area admin. **Adeguato a una rete di magazzino chiusa, non a un'esposizione su internet.**

---

## 15. Endpoint HTTP e file su rete

Tre minimal API, definite in `Program.cs`.

| Endpoint | Funzione |
|----------|----------|
| `GET /api/thumbnail/{pacod}` | Miniatura dell'articolo. Cerca `<ArticleThumbnails:UncPath>\<pacod>.png`. Cache pubblica 1 h. `404` se manca il file o la configurazione |
| `GET /api/acceptance-photo/{id:guid}` | Foto di accettazione. Risolve il nome file dal database di appoggio, poi legge da `<AcceptancePhotos:StoragePath>`. Content type dedotto dall'estensione. Cache privata 1 h |
| `DELETE /api/acceptance-photo/{id:guid}` | Elimina il file e il record |

**Path traversal.** L'endpoint miniature valida `pacod` contro `Path.GetInvalidFileNameChars()` e restituisce `400` se contiene caratteri non ammessi. L'endpoint foto usa un `Guid` tipizzato nella route e il nome file viene dal database, non dalla richiesta.

> **Attenzione: nessuna autenticazione su questi endpoint.** Chiunque raggiunga il server può enumerare le miniature per codice articolo e leggere le foto di accettazione conoscendone il GUID. `DELETE` è ugualmente aperto. Accettabile in rete chiusa; da chiudere prima di qualunque esposizione.

**Accesso ai file su rete.** Miniature e documenti articolo stanno su share UNC. L'**identity dell'application pool IIS** deve avere i diritti di lettura su quelle share. Le foto di accettazione richiedono anche il diritto di **scrittura** su `AcceptancePhotos:StoragePath` (la cartella viene creata al primo upload). Limite di dimensione per file caricato: **15 MB**.

---

## 16. Configurazione (appsettings)

`appsettings.json` è in `.gitignore`. Il file da versionare è **`appsettings.template.json`**. In sviluppo si può usare `appsettings.Development.json` (anch'esso ignorato) oppure gli user secrets.

**Non committare password.**

| Sezione / chiave | Tipo | Descrizione |
|------------------|------|-------------|
| `ConnectionStrings:ErpDatabase` | string | Database ERP Intesi. Se vuota → **modalità demo** |
| `ConnectionStrings:LogicDatabase` | string | Database di appoggio WMS. Se vuota, le funzioni che richiedono persistenza degradano |
| `PrintService:IntesiPrinterManagerUrl` | string | Base URL del Printer Manager. Se valorizzato è il canale preferenziale |
| `PrintService:IntesiPrinterManagerPrinter` | string | Stampante di default aziendale |
| `PrintService:PrinterManagerEndpointPath` | string | Path relativo opzionale (es. `api/print`). Vuoto = POST sulla root |
| `PrintService:UserCode` | string | `userCode` inviato al Printer Manager. Default `"WMS"` |
| `PrintService:BaseUrl` | string | Servizio Crystal legacy. L'endpoint è `<BaseUrl>/print` |
| `Wms:AllowedWarehouses` | string[] | Magazzini abilitati (`MGCOD`). Vuoto = nessun filtro |
| `Acceptance:PendingStatuses` | int[] | Valori di stato documento che significano "da accettare". Default `[200, 201]` |
| `Acceptance:AcceptedStatus` | int | Stato da scrivere alla chiusura del documento. Default `-4` |
| `Acceptance:LocationFlagColumn` | string | Nome della colonna flag su `A_LOC` che individua la locazione di default per l'accettazione |
| `Acceptance:LoadCausal` | string | Causale di carico. Default `CMI` |
| `Acceptance:DischargeCausal` | string | Causale di scarico dal magazzino documento. Default `SMI` |
| `Acceptance:AnomalyNotificationId` | int | `IDNTF` per le notifiche di anomalia. Default `101`. `0` = disabilitate |
| `Acceptance:DefaultReportRules` | array | Regole ordinate per la selezione automatica del report etichetta. Ogni regola: `Field` (`Family` / `ArticleCode` / `*`), `Pattern` (regex o `*`), `ReportName`. **Vince la prima che fa match** |
| `PickList:MissingNotificationId` | int | `IDNTF` per la notifica dei mancanti. Default `102`. `0` = disabilitate |
| `Admin:AllowedGroups` | string[] | Gruppi (`A_OPR.GRCOD`) abilitati all'area admin. Es. `["SUPERVISOR"]` |
| `ArticleThumbnails:UncPath` | string | Cartella UNC delle miniature articolo (`<PACOD>.png`) |
| `AcceptancePhotos:StoragePath` | string | Cartella locale delle foto di accettazione |
| `VaultDoc:BaseUrl` | string | Base URL per l'apertura dei documenti articolo dal vault |
| `DetailedErrors` | bool | Dettaglio errori |
| `AllowedHosts` | string | Host ammessi |

> `Acceptance:LocationFlagColumn` è un punto aperto: il codice ha come default `LCACC`, il template propone `Acceptance`. **Il nome reale della colonna va confermato con Mecmar.**

---

## 17. Script SQL e ordine di applicazione

Gli script sono in `sql/`. Usano `CREATE OR ALTER` dove possibile e sono **idempotenti**.

> **Gli oggetti sul database ERP non vengono creati automaticamente dall'applicazione.** Vanno applicati a mano. Solo lo schema del database di appoggio è gestito da `EnsureSchemaAsync`.

### Da applicare al database ERP

| File | Contenuto |
|------|-----------|
| `V001__WMS_V_PickList.sql` | Vista `WMS_V_PickList` — fabbisogni da prelevare per bolla. **Superata da `V008`** |
| `V002__WMS_Pick_SPs.sql` | `WMS_OpenPickSession`, `WMS_InsertPickLine`, `WMS_ClosePickSession`, `WMS_InsertPick` |
| `V003__WMS_V_AcceptanceDocs.sql` | Viste per i documenti di accettazione |
| `V004__WMS_V_ArticleDocuments.sql` | Vista dei documenti articolo (PDF, immagini) |
| `V005__WMS_V_ArticleOrders_Engaged.sql` | Vista ordini e impegni per articolo |
| `V006__WMS_Versamento_SP.sql` | `WMS_InsertVersamentoLine` |
| `V008__WMS_V_PickList_PrelevatoPerLotto.sql` | **Sostituisce `V001`.** Corregge il calcolo della quantità già prelevata |
| `V009__XV_SITUAZIONE_PRELIEVI_Giacenza.sql` | Correzione della vista del report web Intesi: giacenza dalla locazione principale invece di `01`/`01` cablato |
| `V010__L_MLPA_VerifiedBy.sql` | Colonne custom `L_MLPA.X_VerifiedUser` / `X_VerifiedDate` |

```bash
sqlcmd -S <server> -d <ErpDb> -i sql/V001__WMS_V_PickList.sql
sqlcmd -S <server> -d <ErpDb> -i sql/V002__WMS_Pick_SPs.sql
sqlcmd -S <server> -d <ErpDb> -i sql/V003__WMS_V_AcceptanceDocs.sql
sqlcmd -S <server> -d <ErpDb> -i sql/V004__WMS_V_ArticleDocuments.sql
sqlcmd -S <server> -d <ErpDb> -i sql/V005__WMS_V_ArticleOrders_Engaged.sql
sqlcmd -S <server> -d <ErpDb> -i sql/V006__WMS_Versamento_SP.sql
sqlcmd -S <server> -d <ErpDb> -i sql/V008__WMS_V_PickList_PrelevatoPerLotto.sql
sqlcmd -S <server> -d <ErpDb> -i sql/V009__XV_SITUAZIONE_PRELIEVI_Giacenza.sql
sqlcmd -S <server> -d <ErpDb> -i sql/V010__L_MLPA_VerifiedBy.sql
```

### Da applicare al database di appoggio

| File | Contenuto |
|------|-----------|
| `V007__WMS_V_PickListBolleDetail.sql` | TVF `WMS_FN_PickListBolleDetail` per il report Crystal delle liste. **Cross-database: aprire il file e sostituire `[FactoryMecmar]` con il nome reale del database ERP** |
| `M001__PickList_Improvements.sql` | Migrazione tabelle liste |
| `M002__PickListRow_Paf02.sql` | Aggiunta colonna `Paf02` |
| `M003__PickListRow_Paf02_Resize.sql` | Ridimensionamento di `Paf02` |
| `D001__PrintTemplate_Acceptance.sql` | Dati: template di stampa per il contesto accettazione |

`M001`–`M003` sono coperte anche dagli `ALTER TABLE` condizionali in `EnsureSchemaAsync`: applicarle a mano è utile solo su installazioni non ancora avviate dall'applicazione.

### Dipendenze ERP richieste

Devono già esistere sul database ERP: `dbo.GetProgressivo`, `dbo.TRD_InsertMov`, e le tabelle `L_ODLA`, `A_LOT`, `L_CMFE`, `a_THP`, `A_PAR`, `L_MLPA`, `S_PAP`, `S_SPP`, `S_SES`, `S_MOV`.

### Limite noto di `V008`

`S_PAP` non ha riferimento al lotto. La vista aggrega i prelievi per commessa/lotto/articolo su **tutte** le bolle del lotto. Se una bolla copre più lotti, la quantità prelevata viene **attribuita a ciascuno di essi**. È lo stesso limite della vista standard `XV_SITUAZIONE_PRELIEVI` e non è risolvibile con lo schema attuale. Al 2026-09-03 erano 10 i casi rilevati sulle bolle aperte.

---

## 18. Build, publish e hosting IIS

### 18.1 Prerequisiti

- .NET 10 SDK (build) / ASP.NET Core 10 Hosting Bundle (server IIS)
- SQL Server raggiungibile con il database ERP e il database di appoggio
- Accesso in lettura alle share UNC di miniature e documenti articolo per l'identity dell'application pool

### 18.2 Publish

Percorso di publish: **`C:\intesi\WS\WMS`**.

Con applicazione già attiva, per evitare il lock dei file:

```powershell
# 1 — ferma l'applicazione
New-Item C:\intesi\WS\WMS\app_offline.htm -Force

# 2 — pubblica
dotnet publish WMS.csproj -c Release -o C:\intesi\WS\WMS

# 3 — riavvia
Remove-Item C:\intesi\WS\WMS\app_offline.htm
```

### 18.3 Configurazione IIS

- **Usare un sito root dedicato** (per esempio porta 8099). Montare l'applicazione come **sub-application** (tipo `localhost/wms`) **rompe SignalR** e con esso l'interattività Blazor
- La **terminazione HTTPS è di IIS**. Nel codice `UseHttpsRedirection` è deliberatamente assente: reintrodurlo causa loop di redirect dietro il proxy
- `ForwardedHeaders` è già configurato per lo scenario IIS

### 18.4 Considerazioni su Blazor Server

- Ogni utente mantiene un **circuito SignalR** aperto: lo stato vive sul server. Il consumo di memoria cresce con gli utenti concorrenti
- Un riavvio dell'application pool **chiude tutti i circuiti**. `ReconnectModal` gestisce la riconnessione lato client, ma lo stato in memoria (per esempio il carrello di `/production/pick`) va perso. Le transazioni importanti persistono su database proprio per questo
- L'applicazione è **sensibile alla latenza di rete**: ogni interazione è un round-trip. È il motivo per cui il campo barcode fa auto-submit su Invio invece di attendere un click

---

## 19. Convenzioni di sviluppo e trappole note

### 19.1 Render mode

| Regola | Motivo |
|--------|--------|
| `@rendermode InteractiveServer` **solo** su `Routes.razor` | Si propaga a tutto l'albero |
| **Mai** su `MainLayout` | Riceve `Body` come `RenderFragment` → errore di serializzazione a runtime |
| **Mai** sulle singole pagine | Ridondante |

### 19.2 MudBlazor 9

| Trappola | Soluzione |
|----------|-----------|
| `MudTabs` con `PanelClass` | Genera l'analyzer warning `MUD0002`. Non usarlo |
| `MudExpansionPanel` con `@bind-IsExpanded` | Usare `IsExpanded="x"` + `IsExpandedChanged="v => x = v"` |
| `MudSwitch` con handler custom e `@bind-Value` | Genera `RZ10010`. Usare `Value` + `ValueChanged` |
| `MudDialog` service-based | Il cascading parameter è **`IMudDialogInstance`**, non `MudDialogInstance` |
| Parametro custom chiamato `Color` | Confligge con l'enum `MudBlazor.Color`. Usare altri nomi (es. `TileColor`) |
| `MudNumericField` su colonne ERP `numeric(9,6)` | Senza `Format="0.###"` il campo mostra `8,000000`. Impostare sempre `Format` **e** `Culture="@CultureInfo.CurrentCulture"` |

### 19.3 C#

- **Le tuple sono value type**: non usare `?.` su una tupla. Controllare `== default`
- La formattazione delle quantità nei messaggi all'operatore usa sempre `"0.###"` con la cultura corrente (helper `QtyFmt` nelle pagine, `QtyText` in `ErpService`)

### 19.4 Database

| Regola | Motivo |
|--------|--------|
| **Mai scrivere direttamente su `S_MOV`** | Bypasserebbe trigger, progressivi e validazioni. Usare `TRD_InsertMov` |
| **Mai cablare `[FactoryMecmar]`** nel codice o negli script condivisi | Il nome del database non è fisso fra installazioni |
| **Non creare oggetti sul database ERP dall'applicazione** | Gli script `sql/V*` si applicano a mano, in modo controllato |
| Le quantità passate a `TRD_InsertMov` sono **sempre positive** | Il segno lo determina `CMTYP` della causale |
| Controllare sempre il valore di ritorno di `TRD_InsertMov` | `-1` significa causale non valida e **nessuna scrittura** |
| **Non usare `L_MLPA.LASTUPDATE`** come "ultima volta che la riga è stata guardata" | È gestita da trigger e cambia solo al variare delle quantità. Usare `X_VerifiedDate` |

### 19.5 Pattern applicativi

- **Doppio percorso ERP / mock.** Quasi tutte le pagine hanno la forma `if (Erp.IsConfigured) { … } else { … mock … }`. Mantenere entrambi i rami quando si aggiunge una funzione, o la modalità demo si rompe
- **Errori non bloccanti.** Le scritture sul database di appoggio sono spesso in `try { … } catch { }` deliberato: la perdita di una riga di appoggio non deve impedire un'operazione già registrata sul gestionale. Le scritture sull'ERP, al contrario, non vengono **mai** silenziate
- **Esecuzione batch riga per riga.** Nei batch (carrello, prelievi, inventario) ogni riga è indipendente: si accumula una lista di errori e si presenta il riepilogo. Le righe riuscite vengono marcate, quelle fallite restano ritentabili
- **`finally` per la chiusura delle sessioni ERP.** Le sessioni di prelievo e versamento vengono chiuse in un blocco `finally`, anche in caso di errori parziali. Una sessione lasciata aperta è uno stato sporco sul gestionale
- **Contesto di stampa.** Impostarlo quando la pagina ha dati significativi, azzerarlo quando li perde

### 19.6 Regole di progetto

- **Non modificare file di HAMMErp da questo progetto.** Sono repository separati: nessun codice e nessuna dipendenza condivisa
- **Non committare password.** Usare `appsettings.Development.json` (ignorato) o gli user secrets

---

## 20. Debiti tecnici e punti aperti

Ordinati per rilevanza.

### Sicurezza

| Punto | Dettaglio |
|-------|-----------|
| **Credenziali SQL nella storia git** | `.claude/settings.json` è versionato e contiene la password dell'utente `sa` in chiaro dentro le voci di allowlist. È già nella storia dei commit. **La password va ruotata**, e il file va rimosso dal tracking (o la storia riscritta) |
| **Endpoint file senza autenticazione** | `/api/thumbnail/{pacod}` e `/api/acceptance-photo/{id}` sono aperti, `DELETE` compreso. Vedi §15 |
| **Sessione ricostruita da `localStorage` senza firma** | Manipolando `wms-op-group` si accede all'area admin. Vedi §14.3 |
| **Nessun middleware di autorizzazione** | Il gating admin è nel codice dei componenti, pagina per pagina |

### Correttezza

| Punto | Dettaglio |
|-------|-----------|
| **Spostamento non transazionale** | `SMI` e `CMI` sono chiamate separate senza transazione che le comprenda. Se la seconda fallisce, la merce risulta scaricata e non caricata. Vale anche per l'accettazione. Sarebbe risolvibile con una `TransactionScope` o una stored procedure dedicata |
| **`Acceptance:LocationFlagColumn` non confermato** | Default nel codice `LCACC`, template `Acceptance`. Il nome reale va confermato con Mecmar |
| **`V008` attribuisce i prelievi a più lotti** | Limite dello schema `S_PAP`, condiviso con la vista standard Intesi. Vedi §17 |
| **`Logout` non pulisce `wms-op-group`** | Chiave orfana in `localStorage`. Vedi §14.2 |

### Manutenibilità

| Punto | Dettaglio |
|-------|-----------|
| **Nessun test automatico** | Nessun progetto di test nella solution. Tutta la verifica è manuale |
| **`ErpService` a 1750 righe, `LogicService` a 1250** | Classi monolitiche che coprono aree funzionali indipendenti. Candidate a essere spezzate per area (articoli, movimenti, prelievo, accettazione, anagrafiche) |
| **Pagine legacy ancora nel routing** | `/query/article` e `/query/location` sono solo mock, assorbite da `/query`. Da rimuovere |
| **`/production/pick` non raggiungibile dal menu** | Funzionante ma orfana: superata da `/pick/list`. Decidere se rimuoverla o rimetterla nel menu |
| **`MockWmsService` singleton con stato condiviso** | Intenzionale per le demo, ma le sessioni si influenzano a vicenda |
| **`JS.InvokeVoidAsync("eval", …)`** | Usato in più punti per focus, scroll e apertura del file input. Funziona, ma sarebbe più pulito un modulo JS dedicato |
| **`DetailedErrors = true` cablato** | Utile in campo, da valutare per la produzione |
| **Disallineamenti nella documentazione** | Corretti in questa revisione: la route di Gestione Locazioni è `/locations/manage` (non `/location/manage`); la tabella dei prelievi è `WMS_PickListPick` (non `WMS_StagedPick`) |

---

## Appendice A — Riferimenti interni al repository

| File | Contenuto |
|------|-----------|
| [README.md](../README.md) | Setup rapido, publish, architettura sintetica |
| [CLAUDE.md](../CLAUDE.md) | Istruzioni operative per assistenti AI sul repository |
| [docs/WMS.md](WMS.md) | Requisiti di sistema originali (System Requirements Specification) |
| [docs/FactoryMecmar_ErpReference.md](FactoryMecmar_ErpReference.md) | Schema delle tabelle ERP reali con campioni di dati |
| [docs/ErpQuery_Checklist.md](ErpQuery_Checklist.md) | Checklist delle query, viste e SP necessarie |
| [docs/ErpLogin_A_OPR.example.sql](ErpLogin_A_OPR.example.sql) | Query di esempio per l'autenticazione operatore |
| [docs/IntesiPrinterManager.md](IntesiPrinterManager.md) | Integrazione con Intesi Printer Manager |
| [docs/PRD_Acceptance.md](PRD_Acceptance.md) | PRD del modulo accettazione |
| [docs/PRD_ListePrelievoeGestioneLocazioni.md](PRD_ListePrelievoeGestioneLocazioni.md) | PRD di liste di prelievo e gestione locazioni |
| [docs/style.md](style.md) | Linee guida visive |
| [docs/Preventivo_WMS_Mecmar_MVP.md](Preventivo_WMS_Mecmar_MVP.md) | Preventivo MVP |
| [sql/README.md](../sql/README.md) | Indice degli script SQL |

## Appendice B — Storico versioni

| Versione | Contenuti principali |
|----------|----------------------|
| **1.2.0** | Verifica abbinamento articolo/locazione (`L_MLPA.X_Verified*`) e report admin dedicato · correzione della quantità prelevata aggregata per lotto · liste di prelievo filtrate per operatore · stampa contesto `PICK` · correzione dei decimali nelle rettifiche |
| **1.1.x** | Accettazione su ERP · liste di prelievo con sessioni ERP e staging · gestione locazioni · annullamento degli staged e chiusura lista · stampa per terminale |
| **1.0-MVP** | Interrogazione unificata · spostamento semplice e carrello · rettifiche · inventario · logo e tema Mecmar · ripristino sessione da `localStorage` |
