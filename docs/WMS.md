# System Requirements Specification: WMS Mobile Bridge

## 1. Caratteristiche generali

- **Hosting:** applicazione hostabile su **IIS** (Internet Information Services).
- **Database:** **SQL Server** (istanza già disponibile). Il WMS userà:
  - dati e procedure del **database MRP/ERP** esistente (lettura/scrittura dove necessario);
  - **strutture proprietarie** dove serve: tabelle e viste di appoggio, log, abbinamenti report–stampante–utente, ecc.
- **Stampa etichette:** da **tutti i menù** deve essere possibile stampare etichette (chiamata a un’**API esterna**). Payload tipico:
  - nome report **Crystal Report XI**;
  - nome **stampante**;
  - **parametri Crystal** (es. ID, PACOD, altri campi richiesti dal report).
- **Dispositivo principale:** terminale **Android** con uso intensivo di **barcode**:
  - schermo **5.5" HD+ IPS**, risoluzione di riferimento **1440×720**;
  - **touch capacitivo multitouch**;
  - scanner barcode integrato (gestione invio fine lettura, es. tasto Invio).

### 1.1 Interfaccia touch e “fat finger”

L’uso in magazzino è prevalentemente **in piedi**, spesso con **guanti** o **dita grandi** e poca precisione: l’interfaccia deve **favorire tocchi sicuri** e ridurre errori involontari.

- **Target di tocco generosi:** pulsanti, righe elenco, card e controlli selezionabili con **altezza/larghezza minima ampia** (riferimento: linee guida touch tipo **44–48 px** equivalenti a schermo, meglio **ancora più grandi** su terminale industriale 5.5"); evitare controlli affiancati troppo piccoli o fitti.
- **Spaziatura** tra elementi interattivi adiacenti (margini/padding) per separare chiaramente un’azione dall’altra.
- **Testo e numeri leggibili** (quantità, codici articolo, locazioni): dimensione corpo adeguata; evitare testo troppo piccolo solo per “far stare tutto” in una schermata.
- **Contrasto e stato visivo** chiari (es. evadibile / non evadibile, selezionato, confermato, disabilitato) così che l’operatore capisca al volo senza leggere dettagli minuti.
- **Azioni critiche** (conferma movimento, dichiarazione mancante, chiusura lista, rettifiche): dove opportuno **conferma esplicita** o **due passi** per evitare tap accidentali.
- **Flussi frequenti** con **pochi tap** possibile: priorità allo **scan** e a campi unici ben evidenti; ridurre dipendenza da menu annidati o gesture delicate.

---

## 2. Stack tecnico (implementazione)

- **Backend:** .NET 10 / C# (Web API + Minimal API), deployabile su IIS.
- **Frontend:** Mud Blazor (o React).
- **SQL Server:** connessione all’istanza esistente; integrazione con tabelle/procedure ERP e oggetti proprietari WMS.

---

## 3. Dati: ERP/MRP e strutture proprietarie

- **Lato ERP/MRP:** uso di **procedure e tabelle** esposte dal gestionale (liste prelievo, giacenze, anagrafiche, utenti per login, ecc. secondo modello reale).
- **Lato WMS (proprietario):** dove necessario:
  - **log** operazioni e tracciabilità;
  - **configurazione** (es. abbinamenti utente / report / stampante);
  - **appoggio liste di prelievo** (testata, righe, prelievi parziali multipli per riga, stati);
  - **viste bridge** per uniformare letture in tempo reale verso l’ERP.

---

## 4. Moduli funzionali

### 4.1 Login

- **Utente e password** validati contro il **database ERP** (non credenziali solo locali, salvo ambienti di sviluppo).

### 4.2 Interrogazione universale

- Si inquadra / si immette **un solo codice** (barcode o digitato).
- Il codice può identificare un **articolo** oppure una **locazione** (discriminazione tramite regole ERP e/o formato, es. regex o query).
- **Se articolo:** dettaglio articolo, **elenco locazioni** in cui è presente con **quantità** per locazione.
- **Se locazione:** dettaglio locazione, **elenco articoli** presenti con **quantità** (comportamento allineato alle regole ERP per sottolocazioni o aggregazioni, se previste).

### 4.3 Spostamento semplice

Flusso operativo:

1. Scansione **codice articolo**.
2. Scansione o selezione **locazione di partenza** tra quelle proposte (**ordinate per giacenza**).
3. Inserimento **quantità** da spostare.
4. Scansione **locazione di destinazione**.

### 4.4 Lista di prelievo

- La lista arriva dal **database ERP** (articoli, quantità richieste, **riferimento** e **tipo riferimento**, altri campi utili in UI).
- **UI:** presentazione a **card** (più campi organizzati in card), ottimizzata per terminale 5.5" e per **touch / fat finger** (vedi §1.1): card e azioni facilmente selezionabili.
- **Tempo reale:** evidenziazione righe **evadibili** vs **non evadibili** in base alle giacenze.
- **Ordinamento** delle righe; **flag** per mostrare/nascondere le righe **già evase**.
- **Tabelle di appoggio** (modello concettuale):
  - identificativo lista, riferimento, tipo riferimento;
  - per ogni riga: articolo, quantità richiesta;
  - per ogni riga: **uno o più prelievi** (quantità **parziali** consentite nel tempo).
- **Esecuzione:** una volta avviata, la lista resta consultabile; le quantità **già prelevate/confermate** non sono modificabili; restano evadibili le **quantità residue**.
- **Mancante:** dichiarazione articolo/riga come mancante.
- **Fuori lista:** aggiunta di un articolo **non presente** in lista obbligando un **riferimento** (es. ordine/documento).

### 4.5 Rettifiche semplici

- Impostazione rapida della giacenza per terna **Articolo / Locazione / Quantità** (secondo regole ERP e autorizzazioni).

### 4.6 Inventario

- Il gestionale invia una **lista inventario**; l’operatore **conferma le quantità** rilevate sul campo; all’esecuzione si generano le **rettifiche** verso l’ERP secondo il flusso definito.

### 4.7 Carrello (batch picking)

- **Crea carrello:** scansione sequenziale di **N articoli** con **quantità** e **locazione di prelievo** per ciascuna riga.
- **Evadi carrello:** per ogni riga si indica il **magazzino/locazione di destinazione**; prevedere anche **versamento massivo** di più righe verso **una sola locazione** dove applicabile.

### 4.8 Scheda articolo

- Richiamabile in ogni punto in cui compare un articolo tramite pulsante **Info** (etichetta **“I”** sufficiente).
- Contenuti: **dettagli anagrafici/tecnici**, **PDF** e **anteprima immagine** (JPG/PNG, ecc.) se disponibili.
- **Origine file (Mecmar):** percorsi su **rete** accessibili all’**account IIS** (share/UNC); collegamento tramite tabella **`A_DOC`** con **`A_DOC.IDPAR = A_PAR.IDPAR`** (il path del file è sui record documento; estensione `.pdf` / immagine per distinguere uso in UI).

### 4.9 Liste di prelievo e versamento (dettaglio dati ERP)

- Definizione tabelle/query lato gestionale: **da concordare in seguito** (non bloccante per le altre aree).

---

## 5. Stampa (servizio centralizzato)

Ogni modulo che prevede etichette passa da un **unico punto** nell’app (es. `POST /api/print`). L’implementazione sceglie automaticamente:

1. **Intesi Printer Manager** — se il parametro di database **`IntesiPrinterManagerUrl`** è valorizzato **e** il nome report **non** è un file Crystal (non termina con **`.rpt`**, quindi è un *print model code*). Payload API: oggetto con `opts.printModelCode`, `opts.userCode`, `opts.printerName` (da parametro **`IntesiPrinterManagerPrinter`** se non passato nella richiesta), `opts.description_1` (nome pagina), `opts.description_2`, `opts.copyQuantities`, `opts.jsonParams` (parametri report serializzati in JSON). Diagnostica: log in **debug** e UI del Printer Manager.
2. **Metodo legacy (Crystal / servizio HTTP precedente)** — se l’URL Printer Manager **non** è impostato, oppure il report è un **percorso/file `.rpt`**, si usa il flusso storico (stesso endpoint unico, payload “report + stampante + parametri” come prima).

È possibile **combinare** le due modalità: URL Printer Manager attivo e, per singole stampe, continuare a passare il **path del file `.rpt`** nel parametro report per forzare il metodo legacy.

Parametri da configurazione/DB (oltre a `ConnectionStrings`): almeno **`IntesiPrinterManagerUrl`**, **`IntesiPrinterManagerPrinter`**; in app: sezione **`PrintService`** (allineamento ai nomi parametro gestionale).

---

## 6. Note per lo sviluppo

1. **DB:** schema SQL per tabelle di appoggio (prelievi, log, carrello, configurazioni stampa) allineato ai flussi sopra.
2. **API:** endpoint per liste, movimenti, inventario, carrello e **stampa**.
3. **UI/UX:** layout per 1440×720; **priorità a touch sicuro e “fat finger”** (§1.1): componenti Mud (o equivalenti) con dimensioni minime, padding, tipografia; navigazione da menù con voci ben separate; scan-first; pulsante **I** per scheda articolo ove previsto.
4. **Logica scan:** distinzione articolo vs locazione tramite **query ERP** e/o **regex** configurabili.
