# WMS Mecmar — Offerta Attivazione MVP

**Cliente:** Mecmar  
**Oggetto:** Sistema di gestione magazzino mobile (WMS) — Fase 1

---

## Descrizione del progetto

Sviluppo e attivazione di un sistema WMS mobile su misura, accessibile da terminale Android con lettore barcode integrato. L'interfaccia è ottimizzata per l'uso in magazzino: touch generoso, testo leggibile, flussi rapidi con pochi passaggi.

Il sistema si integra direttamente con il gestionale ERP esistente (FactoryMecmar) senza duplicazione dei dati.

**Dispositivo di riferimento:** terminale Android industriale con schermo 5,5" HD+, scanner barcode integrato e touch capacitivo. L'interfaccia è progettata per l'uso con guanti e in condizioni di scarsa precisione: elementi touch generosi, testo e quantità leggibili a colpo d'occhio, flussi barcode-first che minimizzano la digitazione manuale.

---

## Funzionalità incluse nell'MVP

### 1. Interrogazione Articolo / Locazione

Unico punto di accesso per la consultazione del magazzino in tempo reale.

- Scansione o digitazione di un codice (articolo o locazione)
- Riconoscimento automatico del tipo di codice
- **Se articolo:** descrizione, giacenza totale, elenco locazioni con quantità
- **Se locazione:** dettaglio locazione, elenco articoli presenti con quantità
- Storico movimenti recenti
- Accesso rapido alla scheda articolo da ogni riga

---

### 2. Scheda Articolo

Pannello informativo richiamabile in qualsiasi punto dell'applicazione in cui compare un articolo.

- Dati anagrafici completi (codice, descrizione, unità di misura, famiglia, ecc.)
- Visualizzazione allegati **PDF** con anteprima in-app
- Visualizzazione **immagini** (JPG, PNG) con anteprima in-app
- File recuperati dalla rete aziendale tramite i percorsi configurati nel gestionale

---

### 3. Rettifica Semplice

Correzione diretta della giacenza per un articolo in una locazione specifica.

- Scansione articolo e selezione/scansione locazione
- Visualizzazione della giacenza attuale
- Inserimento della nuova quantità con calcolo automatico del delta
- Conferma esplicita prima dell'esecuzione (azione irreversibile)
- Scrittura nel gestionale tramite le causali di rettifica previste

---

### 4. Inventario

Gestione completa del ciclo di inventario fisico con sessione riprendibile.

- Selezione magazzino e zona da inventariare
- Lista articoli/locazioni da contare, con stato avanzamento
- Conteggio articolo per articolo con scansione barcode
- Gestione articoli extra (trovati fisicamente ma non in lista)
- Calcolo automatico dei delta rispetto alle giacenze attese
- Applicazione delle rettifiche al gestionale a fine sessione
- Sessione persistente: riprende da dove era rimasta in caso di interruzione

---

### 5. Spostamento Semplice

Trasferimento guidato di un articolo da una locazione a un'altra.

- Scansione articolo
- Selezione locazione di partenza (proposte ordinate per giacenza)
- Scansione locazione di destinazione
- Inserimento quantità con controllo disponibilità
- Conferma riepilogativa prima dell'esecuzione

---

### 6. Spostamento Carrello (batch picking)

Prelievo sequenziale di più articoli con versamento massivo verso una o più destinazioni.

- **Fase accumulo:** scansione sequenziale di articoli, locazioni di prelievo e quantità
- Carrello persistente per operatore (riprende da dove lasciato)
- **Fase versamento:** assegnazione destinazione a una o più righe contemporaneamente
- Versamento massivo: più righe verso un'unica locazione con una sola scansione
- Esecuzione di tutti i movimenti in blocco con conferma finale
- Possibilità di rimuovere singole righe prima dell'evasione

---

### 7. Prelievo Produzione

Dichiarazione del consumo materiali a supporto degli ordini di produzione.

- Selezione ordine/bolla di produzione tramite barcode o ricerca
- Elenco componenti raggruppati per tipologia di handling
- Scansione articolo e locazione di prelievo con inserimento quantità
- Gestione prelievi parziali con aggiornamento del residuo
- Indicazione visiva dello stato di avanzamento per riga e per gruppo
- Esecuzione in blocco con riepilogo e gestione errori parziali

---

### 8. Accettazione Materiale

Registrazione dell'entrata merci da fornitore o da trasferimento.

- Selezione del documento/ordine in arrivo tramite barcode o ricerca
- Elenco articoli attesi con quantità e stato di accettazione
- Scansione articolo e locazione di destinazione con inserimento quantità
- Gestione accettazioni parziali con tracciamento del residuo atteso
- Conferma con riepilogo delle quantità accettate

---

### 9. Stampa Etichette

Sistema di stampa centralizzato accessibile da tutti i moduli, con configurazione tramite interfaccia amministrativa integrata.

- Pulsante stampa disponibile in tutti i contesti operativi (articolo, locazione, movimento, carrello, inventario, accettazione, rettifica)
- Configurazione di **3 layout report/etichette** concordati con Mecmar
- Parametri auto-compilati dal contesto corrente (codice articolo, locazione, quantità, ecc.)
- Parametri aggiuntivi richiedibili all'operatore prima della stampa
- Compatibile con **Intesi Printer Manager** e con il servizio Crystal Reports esistente
- Interfaccia di configurazione template integrata nell'applicazione (nessun intervento manuale su file)

---

## Suddivisione giornate

### 8 Giornate da remoto

| # | Contenuto |
|---|-----------|
| 1 | Analisi tecnica, mappatura flussi operativi, allineamento con il gestionale |
| 2 | Sviluppo: login, interrogazione unificata, scheda articolo |
| 3 | Sviluppo: spostamento semplice, rettifiche |
| 4 | Sviluppo: inventario, spostamento carrello |
| 5 | Sviluppo: prelievo produzione, accettazione materiale |
| 6 | Sviluppo: sistema di stampa etichette, configurazione 3 layout |
| 7 | Test interni, correzioni, ottimizzazioni interfaccia mobile |
| 8 | Affinamento post-collaudo (bug fix e aggiustamenti emersi dal go-live) |

### 3 Giornate on-site (presso Mecmar)

| # | Contenuto |
|---|-----------|
| 1 | Condivisione requisiti dettagliati, allineamento flussi operativi con il personale di magazzino |
| 2 | Installazione, configurazione terminali Android, test scansione barcode in ambiente reale |
| 3 | Correzione report ed etichette, affiancamento operativo al go-live |

---

## Esclusioni — Fase 2

Le seguenti funzionalità **non sono incluse** nell'offerta e richiedono un'analisi dedicata prima di qualsiasi stima. Verranno valutate a fine Fase 1, a progetto o a consuntivo.

| Funzionalità | Note |
|---|---|
| **Packing** | Logiche di aggregazione colli da definire |
| **Spedizioni** | Dipende dall'integrazione con DDT, vettori e flussi documentali |
| **Conto Lavoro** | Flussi specifici con terzisti, tracciabilità componenti inviati/rientrati |
| **Rotazione Magazzino** | Logica di promozione Stock → Locazione Principale |

---

*Documento interno — Intesi S.r.l.*
