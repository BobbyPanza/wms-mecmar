# WMS Mecmar — Guida di Stile

Fonte: *Brand Guidelines PLC Software, Mecmar S.p.a. — 06.2024 (NetStrategy)*

---

## Colori

### Primari

| Nome | Hex | RGB | Uso |
|------|-----|-----|-----|
| Eerie black | `#1e1f1f` | 30, 31, 31 | Background navigazione, superfici scure |
| Mikado yellow | `#ffc200` | 255, 194, 0 | Accento primario, bottoni attivi, titoli di sezione, icone |

### Secondari

| Nome | Hex | RGB | Uso |
|------|-----|-----|-----|
| Pictor blue | `#23A6F0` | 35, 166, 240 | Bottoni secondari: Aggiorna, Modifica, Configura, Torna indietro |
| Red | `#FF1919` | 255, 25, 25 | Bottoni distruttivi: Elimina, Allarme, Errore |
| Dark pastel green | `#00C853` | 0, 200, 83 | Bottoni di conferma: Attiva, Successo |

### Background / superfici

| Hex | Uso |
|-----|-----|
| `#a1a1a1` | Cadet gray — testo secondario, icone muted |
| `#e2e2e2` | Superfici chiare (griglia light) |
| `#fafafa` | Background contenuto light |
| `#ffe79c` | Giallo chiaro — selezioni attive, highlight |

---

## Tipografia

**Font principale: Yantramanav** (Google Fonts)
Pesi disponibili: 400 Regular · 500 Medium · 700 Bold · 900 Black

Import Google Fonts:
```html
<link href="https://fonts.googleapis.com/css2?family=Yantramanav:wght@400;500;700;900&display=swap" rel="stylesheet" />
```

### Scale tipografica

| Livello | Font | Dimensione | Line-height |
|---------|------|-----------|-------------|
| H1 | Yantramanav Medium | 40px | 1.25 |
| H2 | Yantramanav Medium | 20px | 1.25 |
| H3 | Yantramanav Medium | 18px | 1.25 |
| H4 | Yantramanav Medium | 16px | 1.25 |
| H5 | Yantramanav Bold | 14px | 1.25 |
| Body 1 | Yantramanav Medium | 16px | 1.25 |

> Nota: barcode input e chip codici articolo usano `Roboto Mono` (font monospace) per leggibilità.

---

## Icone

Le icone Mecmar seguono le regole:
- Area minima rispettata: **25×25 px**
- Colori consentiti: `#ffc200` (giallo brand), `#1e1f1f` (nero), `#ffffff` (bianco)
- Lo stile è **outline** con tratto uniforme

Per il WMS usiamo **Material Icons** (MudBlazor), colorati con `--wms-primary` (`#ffc200`) sulle azioni principali.

---

## Componenti UI

### Bottoni primari (Attivazione/Selezione)

- **Attivo**: background `#ffc200`, testo `#1e1f1f`, icona check cerchio scuro
- **Non attivo**: background trasparente, bordo `#ffc200`, testo chiaro

### Bottoni secondari

| Tipo | Colore | Uso |
|------|--------|-----|
| Crea/Nuovo | `#1e1f1f` + icona + | Aggiunge una nuova entità |
| Elimina | `#FF1919` | Rimuove / cancella |
| Attiva | `#00C853` | Conferma / attiva |
| Aggiorna/Modifica | `#23A6F0` outline | Modifica o aggiorna |

### Modal

- Header: background `#1e1f1f`, testo bianco, icona, pulsante chiusura ×
- Contenuto: lista di voci con checkbox stile Mecmar
- Footer: bottone primario `Start` / `Conferma`
- Overlay: `#191b1c` con opacità **10%**

---

## Layout (riferimento PLC, adattato per WMS mobile)

Il brand guide definisce un canvas 1280×800 px con:
- **Navigazione 1** (bottom): 65 px — adattato a AppBar in cima
- **Navigazione 2** (top): 65 px
- **Content**: 670 px
- Margini interni: **35 px**

Per il WMS (mobile-first su tablet/terminale industriale):
- AppBar fisso in cima (`height: 56px`)
- Contenuto con padding `px-2 py-2`
- `MaxWidth.Small` per contenuto centrato

---

## Implementazione attuale (WMS)

| Variabile CSS | Valore | Corrisponde a |
|--------------|--------|---------------|
| `--wms-primary` | `#ffc200` | Mikado yellow |
| `--wms-secondary` | `#23A6F0` | Pictor blue |
| `--wms-success` | `#00C853` | Dark pastel green |
| `--wms-warning` | `#FF8C00` | Orange (distinto dal giallo) |
| `--wms-error` | `#FF1919` | Red |
| `--wms-bg` | `#1e1f1f` | Eerie black |
| `--wms-surface` | `#252525` | Superficie card |
| `--wms-text` | `#f0f0f0` | Testo principale |
| `--wms-text-muted` | `#a1a1a1` | Cadet gray |

MudBlazor `PrimaryContrastText = "#1e1f1f"` — testo scuro sui bottoni gialli per leggibilità WCAG.
