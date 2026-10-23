# Riferimento del template

[English](template-reference.md) | **Italiano**

Tutte le proprietà di `template.json`, con valori, limiti e valore predefinito. Come funziona un template lo spiega la [guida](guide.it.md); i dati che un template può usare sono nelle [chiavi dei dati](data-keys.it.md).

*Generato dal codice di PokePic da `build/generate-reference.cs`, che verifica ogni regola con il parser di PokePic: non modificarlo a mano.*

Una proprietà sconosciuta è un errore. Una proprietà a `null` vale come assente. I valori di un elenco (`left`, `center`…) non distinguono maiuscole e minuscole. Un *valore fisso* non può essere un binding.

## Template (`template.json`)

| Proprietà | Obbligatoria | Valori | Predefinito | Note |
|---|---|---|---|---|
| `$schema` |  | testo |  | Ignorata da PokePic (utile agli editor). |
| `id` | sì | testo |  | Identificativo, senza spazi; salvato nelle card esportate. |
| `version` |  | intero 1–…, valore fisso | `1` | La versione del template. |
| `name` |  | testo |  | Nome visualizzato; è anche la prima parte del nome dei file esportati. |
| `language` |  | codice di lingua (`en`, `it`, `ja`, `zh-Hans`…) |  | Forza la lingua di tutta la card. |
| `canvas` | sì | oggetto, vedi `canvas` |  |  |
| `values` |  | oggetto |  | Valori con un nome, per tipo (`text`, `int`, `float`, `bool`, `color`, `image`, `font`): vedi *Valori*. |
| `styles` |  | oggetto |  | Vedi *Stili*. |
| `elements` | sì | array di elementi |  | Livelli, disegnati in ordine: ognuno sopra i precedenti. |

## `canvas`

| Proprietà | Obbligatoria | Valori | Predefinito | Note |
|---|---|---|---|---|
| `width` | sì | intero 1–4096, valore fisso |  | Larghezza del template in pixel: ogni coordinata è in questi pixel. |
| `height` | sì | intero 1–4096, valore fisso |  | Altezza del template in pixel. |
| `mode` |  | `highResolution` \| `pixelPerfect` | `highResolution` | `pixelPerfect`: pixel netti, senza bordi sfumati (pixel art). `highResolution`: bordi sfumati, testi e immagini nitidi a ogni scala. |
| `scale` |  | numero 0.1–16, valore fisso | `1` | Moltiplicatore del PNG: un canvas 192×128 con scala 4 dà un PNG 768×512. Intero in pixelPerfect. |
| `background` |  | colore o gradiente |  | Vedi *Riempimento*. Assente: trasparente. |

## Proprietà di ogni elemento

| Proprietà | Obbligatoria | Valori | Predefinito | Note |
|---|---|---|---|---|
| `type` | sì | `group` \| `image` \| `shape` \| `line` \| `text` |  | Tipo di elemento. |
| `name` |  | testo |  | Nome mostrato nel pannello Livelli dell'editor. |
| `style` |  | testo |  | Nome di uno stile di `styles`: le proprietà dell'elemento hanno la precedenza. |
| `visible` |  | `true` \| `false` \| `"{bool}"` \| `"{!bool}"` | `true` | `"{!chiave}"` è il contrario del valore. |
| `opacity` |  | numero 0–1, valore fisso | `1` | 0 invisibile, 1 opaco. |

## `group`

| Proprietà | Obbligatoria | Valori | Predefinito | Note |
|---|---|---|---|---|
| `position` |  | `[x, y]`: numeri o binding numerici | `[0, 0]` | Spostamento dei figli: le loro coordinate (gradienti compresi) sono relative a questo punto. |
| `children` |  | array di elementi | `[]` | Elementi del gruppo, disegnati in ordine. |

## `image`

| Proprietà | Obbligatoria | Valori | Predefinito | Note |
|---|---|---|---|---|
| `rect` | sì | `[x, y, larghezza, altezza]`: numeri o binding numerici |  | Il rettangolo in cui si colloca l'immagine. |
| `image` | sì | `"{nome}"`, un percorso, oppure `{ "set", "key" }` |  | Quale immagine: vedi *Immagini e font*. |
| `fit` |  | `none` \| `stretch` \| `contain` \| `cover` \| `scale` | `none` | `none` dimensione originale; `stretch` deformata per riempire il rettangolo; `contain` intera, proporzioni mantenute; `cover` riempie il rettangolo, ritagliata; `scale` dimensione originale per `scale`. |
| `scale` |  | numero 0.01–100, valore fisso | `1` | Solo con `fit: scale` (altrimenti è un errore): `2` doppia, `0.5` metà. |
| `anchor` |  | `topLeft` \| `top` \| `topRight` \| `left` \| `center` \| `right` \| `bottomLeft` \| `bottom` \| `bottomRight` | `center` | Dove si colloca l'immagine nel rettangolo. |
| `flip` |  | `none` \| `horizontal` \| `vertical` \| `both` | `none` | Rispecchia l'immagine. |
| `smooth` |  | `true` \| `false`, valore fisso |  | Assente: sfumata in highResolution. `false`: ingrandisce la pixel art senza sfumare. In pixelPerfect le immagini non sono mai sfumate. |
| `overflow` |  | `none` \| `crop` | `none` | `none`: disegnata intera, anche fuori dal rettangolo; `crop`: solo la parte dentro. Con `fit: cover` è sempre ritagliata. |
| `language` |  | codice di lingua (`en`, `it`, `ja`, `zh-Hans`…) |  | Forza la lingua di un set di immagini localizzato (`{language}` nel pattern). |

## `shape`

| Proprietà | Obbligatoria | Valori | Predefinito | Note |
|---|---|---|---|---|
| `rect` | sì | `[x, y, larghezza, altezza]`: numeri o binding numerici |  | Il rettangolo della forma. |
| `shape` |  | `rect` \| `ellipse` | `rect` | `ellipse` è l'ellisse inscritta nel rettangolo. |
| `radius` |  | numero 0–…, valore fisso | `0` | Angoli arrotondati (solo rettangoli). |
| `fill` |  | colore o gradiente |  | Vedi *Riempimento*. Una forma ha bisogno di `fill`, `stroke` o entrambi. |
| `stroke` |  | colore, oppure `{ "color", "width" }` |  | Il bordo, disegnato dentro il rettangolo: vedi *Bordo*. |

## `line`

| Proprietà | Obbligatoria | Valori | Predefinito | Note |
|---|---|---|---|---|
| `from` | sì | `[x, y]`: numeri o binding numerici |  | Inizio della linea. |
| `to` | sì | `[x, y]`: numeri o binding numerici |  | Fine della linea. |
| `color` | sì | colore o binding a un colore |  |  |
| `width` |  | numero 0.01–…, valore fisso | `1` | Spessore in pixel. Con uno spessore dispari le coordinate sono pixel, estremi compresi. |

## `text`

| Proprietà | Obbligatoria | Valori | Predefinito | Note |
|---|---|---|---|---|
| `rect` | sì | `[x, y, larghezza, altezza]`: numeri o binding numerici |  | Il rettangolo in cui si dispone il testo. I testi non vanno a capo da soli: solo con `\n`. |
| `text` | sì | testo con binding |  | Il testo, con i binding; `\n` va a capo. |
| `font` | sì | testo |  | Un font: `"{nome}"` di `values.font`, il percorso di un file, oppure `"system:Nome famiglia"` per un font installato; vedi *Immagini e font*. |
| `size` | sì | numero 0.5–1000, valore fisso |  | Dimensione in pixel. |
| `color` |  | colore o binding a un colore | `#000000` |  |
| `align` |  | `left` \| `center` \| `right` | `left` |  |
| `valign` |  | `top` \| `middle` \| `bottom` | `top` |  |
| `lineHeight` |  | numero 0.5–1000, valore fisso |  | Distanza fra le righe, in pixel. Assente: quella del font. |
| `language` |  | codice di lingua (`en`, `it`, `ja`, `zh-Hans`…) |  | Forza la lingua dei valori tradotti di questo testo. |
| `overflow` |  | `none` \| `crop` \| `ellipsis` | `none` | `none`: disegnato intero, anche fuori dal rettangolo; `crop`: solo la parte dentro; `ellipsis`: ogni riga troppo lunga viene accorciata e finisce con `ellipsis`. |
| `ellipsis` |  | testo | `.` | Aggiunto alle righe accorciate, ad esempio `...` o `…`. |

## Valori

- **Colore**: `"#RGB"`, `"#RGBA"`, `"#RRGGBB"`, `"#RRGGBBAA"` (l'ultima coppia è la trasparenza: `00` invisibile, `FF` opaco), oppure un binding a un colore (`"{type1.color}"`, senza formato né `!`). I nomi dei colori non sono accettati.
- **Riempimento** (`fill`): un colore, oppure un gradiente lineare `{ "linear": { "from": [x, y], "to": [x, y], "stops": [[0, colore], [1, colore]] } }`: almeno due tappe, posizioni da 0 a 1 in ordine crescente. Le coordinate del gradiente sono quelle del canvas (del gruppo, dentro un gruppo).
- **Bordo** (`stroke`): un colore (spessore 1), oppure `{ "color": colore, "width": numero ≥ 0 }`. È disegnato dentro il rettangolo.
- **Valori** (`values.<tipo>.<nome>`, tipi `text`, `int`, `float`, `bool`, `color`, `image`, `font`): si usano come le chiavi dei dati, `"{nome}"`; i nomi possono contenere lettere, cifre, `.`, `_` e `-`, e sono unici fra i tipi. Ogni valore si scrive in una di quattro forme: singolo (`"#F08030"`, `6`, `"fonts/a.ttf"`); per lingua (`{ "en": "Lv.", "it": "Liv." }`, `en` obbligatorio, scelta come per i dati); set (`{ "items": { "fire": …, "water": … }, "fallback": … }`, ogni elemento singolo o per lingua); pattern, solo per testi, immagini e font (`{ "pattern": "artwork/{key}.png", "fallback": … }`). Colori, numeri e vero/falso accettano un valore o un riferimento del loro tipo (`"{type1.color}"`, `"{!hasType2}"`, `"{typeColor[type1.key]}"`; un intero va bene dove serve un decimale); testi, immagini e font si compongono di testi e numeri (`"{species.name} Lv. {level}"`, `"{BASE}/{species:D4}.png"`), oppure sono un riferimento del loro tipo (`"{frame}"`, `"{species.sprite}"`). `{language}` è sempre la lingua dell'elemento. Un ciclo, una chiave sconosciuta, un tipo sbagliato o un nome uguale a una chiave dei dati sono errori.
- **Set**: `"{set[chiave]}"` prende il valore di `chiave` e sceglie quell'elemento (`"{typeColor[move1.type.key]}"`); `{ "set": "artwork", "key": ["{species}-{form}", "{species}"] }` accetta anche una chiave con formato o candidati provati in ordine. Vince la prima chiave che il set ha (per un pattern di immagini o font, la prima il cui file esiste), poi il `fallback`; senza, una chiave senza elemento è un errore quando si crea la card. Una chiave contiene lettere, cifre, `-` e `_`, in parti separate da `/` (`shiny/female/3`): non può uscire dalla cartella di un pattern. I file di un pattern non sono elencati (un set può contenere migliaia di artwork).
- **Immagini e font**: `"{frame}"` = un valore di `values.image` o un'immagine del pacchetto dati (`"{species.sprite}"`); `"{artwork[pokeapi.id]}"` o `{ "set": …, "key": … }` = l'elemento di un set; ogni altro testo è il percorso di un file, anche composto (`"badges/{type1.key}.png"`). Un font è `"{nome}"` di `values.font`, un percorso, oppure `"system:Famiglia"` (un font installato: la card dipende allora dal computer).
- **Percorsi dei file**: relativi alla cartella del template (non possono uscirne), `file:///C:/cartella/file.png`, oppure `https://…` (scaricato una volta per sessione). Un percorso con `{language}` prova la lingua, la lingua base e l'inglese, e prende il primo file che esiste. I percorsi fissi si verificano all'apertura del template; quelli che dipendono dai dati quando si crea la card.
- **Stili** (`styles.<nome>`): proprietà condivise fra elementi, con un `"type"` facoltativo. Con `"type"` uno stile può impostare ogni proprietà di quel tipo di elemento tranne `name`, `style` e `children`; senza, solo proprietà che l'elemento che lo usa possiede. Le proprietà dell'elemento hanno la precedenza; quelle obbligatorie possono arrivare dallo stile.
