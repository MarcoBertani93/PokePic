# Guida a PokePic

[English](guide.md) | **Italiano**

PokePic crea **card PNG** di Pokémon a partire da un **template**: un disegno con parti fisse (cornici, sfondi, scritte) e parti che cambiano in base al Pokémon (nome, tipi, statistiche, sprite). I dati di un Pokémon arrivano da un **pacchetto dati**, un file JSON.

Questa guida spiega come usare l'editor e come scrivere un template.

- [Concetti](#concetti)
- [L'editor](#leditor)
- [Il template](#il-template)
- [Elementi](#elementi)
- [Valori, binding e formati](#valori-binding-e-formati)
- [Valori](#valori)
- [Stili](#stili)
- [Testi](#testi)
- [Immagini](#immagini)
- [Lingue](#lingue)
- [Export](#export)
- [Il pacchetto dati](#il-pacchetto-dati)
- [Problemi frequenti](#problemi-frequenti)

## Concetti

| Termine | Cos'è |
|---|---|
| **Template** | Una cartella con `template.json` e i suoi file (font, immagini). Descrive la card. |
| **Elemento** (o livello) | Un pezzo della card: forma, testo, immagine, linea o gruppo. Ogni elemento è disegnato sopra i precedenti. |
| **Pacchetto dati** | Un file JSON con i valori di un Pokémon (`level`, `species.name`…) e le sue immagini (`species.sprite`…). |
| **Binding** | Un riferimento a un valore del pacchetto dati, scritto tra graffe: `{level}`. |
| **Card** | Il PNG che si ottiene applicando un template a un pacchetto dati. |

Il testo JSON del template è sempre la fonte di verità: tutto ciò che fai in modo visuale viene riscritto nel JSON, e puoi sempre correggerlo a mano.

## L'editor

### Pannelli

I pannelli si spostano trascinandone il titolo, si mettono in schede insieme ad altri o si staccano in una finestra. Chiusi, si riaprono dal menu **Visualizza**. Il layout viene ricordato; **Visualizza → Ripristina layout dei pannelli** torna a quello iniziale.

| Pannello | A cosa serve |
|---|---|
| **Livelli** | Gli elementi in ordine di disegno: in alto quelli dietro, in basso quelli davanti. |
| **JSON** | Il testo del template. Errori e modifiche si vedono subito nell'anteprima. |
| **Proprietà** | I campi dell'elemento selezionato, di uno stile o del template. |
| **Testi**, **Numeri**, **Vero/falso**, **Colori**, **Immagini**, **Font** | I valori di ogni tipo (**Visualizza → Valori**): a sinistra quelli del pacchetto dati, in sola lettura (doppio clic inserisce `{chiave}` nel JSON); a destra quelli del template, modificabili. |
| **Stili** | Gli stili del template. |
| **Problemi** | Gli errori del template, con la riga; doppio clic per andare alla riga nel JSON. |

### Selezionare e spostare

- **Clic** su un elemento del canvas: lo seleziona (il più in alto sotto il mouse).
- **Trascinare** un elemento già selezionato: lo sposta. Il primo clic seleziona soltanto, così non si sposta niente per sbaglio.
- **Ctrl+clic** (sul canvas o nei Livelli): aggiunge o toglie un elemento dalla selezione.
- **Clic sullo sfondo grigio**: seleziona il template.
- **Maniglie** (con un solo elemento): ridimensionano, o spostano gli estremi di una linea.
- **Frecce**: spostano di 1 pixel, con Maiusc di 10.
- Le coordinate scritte come binding (`"rect": ["{x}", 10, 20, 20]`) non si spostano col mouse.

### Menu contestuale e appunti

Il clic destro, nei Livelli o sul canvas, apre il menu dei livelli selezionati: **Taglia**, **Copia**, **Incolla**, **Duplica**, **Raggruppa**, **Separa**, **Elimina**.

| Tasti | Azione |
|---|---|
| Ctrl+C / Ctrl+X / Ctrl+V | Copia, taglia, incolla (con il fuoco su Livelli o canvas) |
| Ctrl+D | Duplica |
| Ctrl+G / Ctrl+Maiusc+G | Raggruppa / separa |
| Canc | Elimina |
| F2 o doppio clic | Rinomina (nei Livelli) |
| Ctrl+Z / Ctrl+Y | Annulla / ripeti |

Gli elementi copiati sono testo JSON negli appunti: si possono incollare anche nel pannello JSON, o in un altro template. Restano nella stessa posizione sul canvas anche se li incolli dentro un gruppo diverso.

### Zoom

La barra sotto il canvas mostra le dimensioni del canvas, la posizione del cursore e lo zoom. Il pulsante accanto alla percentuale passa da *adatta alla finestra* a *100%*. Scorciatoie: Ctrl+rotellina, Ctrl++ e Ctrl+−, Ctrl+B (adatta), Ctrl+0 (100%).

### Proprietà

Ogni campo si applica con **Invio** o cliccando altrove. Un campo vuoto usa il valore dello stile o quello predefinito, mostrati in grigio. Il menu a tendina propone i valori possibili, comprese le chiavi del pacchetto dati e i valori del template. Con più elementi selezionati compaiono solo le proprietà in comune: dove i valori sono diversi si legge *{valori multipli}*, e ciò che scrivi vale per tutti.

## Il template

```jsonc
{
  "id": "spotlight",                      // identificativo, senza spazi
  "version": 1,
  "name": "Spotlight",                    // nome mostrato (e usato nel nome del PNG esportato)
  "language": "it",                       // facoltativo: forza la lingua di tutta la card
  "canvas": { "width": 192, "height": 128, "mode": "pixelPerfect", "scale": 4 },
  "values": {                             // valori con un nome, per tipo
    "font":  { "pixel": "fonts/PocketPixelRegular.ttf" },
    "image": { "frame": "images/frame.png", "types": { "pattern": "types/{key}.png" } }
  },
  "styles": { "label": { "type": "text", "font": "{pixel}", "size": 8 } },
  "elements": [ ]
}
```

Il JSON ammette commenti `//` e virgole finali.

Un template scaricato da internet si scompatta mantenendo la sua cartella: con PKHeX in `plugins\PokePic\templates\<nome>\` accanto a `PKHeX.exe`, dove il plugin lo trova; con il solo editor dove preferisci, aprendolo con **File → Apri template…** e scegliendo il suo `template.json`. Non spostare `template.json` fuori dalla cartella: i percorsi dei suoi file sono relativi.

### Canvas

`canvas` stabilisce le dimensioni (`width` e `height` in pixel: ogni coordinata del template è in questi pixel), lo sfondo (`background`) e come si disegna la card:

- **`highResolution`** (predefinito): bordi sfumati, testi e immagini nitidi a ogni `scale`. Per card con artwork. Le immagini in pixel art, come gli sprite di gioco, ingrandite risultano sfocate: dai loro `"smooth": false`.
- **`pixelPerfect`**: si disegna alla dimensione del canvas senza sfumature, poi si ingrandisce di `scale` (un intero) mantenendo i pixel netti. Per card in stile retrò: disegna in piccolo (ad esempio 256×192) con coordinate e misure intere, mostra gli sprite alla loro dimensione (`"fit": "none"`) o ingranditi di un fattore intero (`"fit": "scale"`), e usa i font pixel alle dimensioni per cui sono disegnati (quelli degli esempi a 8 e 16). I gradienti mostrano delle bande e le ellissi hanno bordi seghettati.

`scale` moltiplica il PNG: un canvas 192×128 con scala 4 dà un PNG 768×512. Tutte le proprietà del canvas: [riferimento del template](template-reference.it.md#canvas).

## Elementi

Proprietà di tutti gli elementi: `type`, `name` (il nome nei Livelli), `style`, `visible` (`true`, `false` o un binding), `opacity` (da 0 a 1).

| `type` | Cosa disegna |
|---|---|
| `group` | niente di suo: sposta, mostra e nasconde insieme i suoi `children`; le loro coordinate sono relative alla sua `position` |
| `shape` | un rettangolo (anche con angoli arrotondati) o un'ellisse, riempiti e/o con un bordo |
| `line` | una linea da `from` a `to` |
| `image` | un'immagine, vedi [Immagini](#immagini) |
| `text` | un testo, vedi [Testi](#testi) |

I numeri di `rect`, `position`, `from` e `to` possono essere binding (`"{level}"`); gli altri numeri (`size`, `radius`, `opacity`…) sono fissi. Tutte le proprietà di ogni elemento, con valori e valore predefinito, sono nel [riferimento del template](template-reference.it.md).

**Riempimento** (`fill`): un colore, oppure un gradiente lineare:

```json
"fill": { "linear": { "from": [0, 0], "to": [192, 0], "stops": [[0, "{type1.color}"], [1, "{type2.color}"]] } }
```

**Colori**: `"#RGB"`, `"#RGBA"`, `"#RRGGBB"`, `"#RRGGBBAA"` (l'ultima coppia è la trasparenza: `00` invisibile, `FF` pieno), oppure un binding a un colore (`"{type1.color}"`).

## Valori, binding e formati

Un binding prende un valore dal pacchetto dati (o da un [valore del template](#valori)):

| Sintassi | Risultato |
|---|---|
| `"{level}"` | il valore di `level` |
| `"Lv. {level}"` | un testo con dentro dei valori |
| `"{!hasType2}"` | il contrario di un valore vero/falso |
| `"{level:D3}"` | il valore con un formato (dopo i due punti) |
| `"{typeColor[move1.type.key]}"` | l'elemento di un [set](#set) scelto dal valore di `move1.type.key` |
| `"{{"` e `"}}"` | le graffe come caratteri |

### Formati dei testi

| Formato | Esempio | Risultato |
|---|---|---|
| (nessuno) | `{species.name}` | `Charizard` |
| `upper` | `{species.name:upper}` | `CHARIZARD` |
| `lower` | `{species.name:lower}` | `charizard` |

Un testo localizzato usa la lingua dell'elemento (vedi [Lingue](#lingue)).

### Formati dei numeri

I formati sono quelli di .NET, con le convenzioni **inglesi** (punto per i decimali, virgola per le migliaia), uguali su ogni computer. I formati `D` e `X` valgono solo per i numeri interi.

| Formato | Valore | Risultato | Note |
|---|---|---|---|
| (nessuno) | 42 | `42` | |
| `D3` | 6 | `006` | interi: almeno 3 cifre, con zeri davanti |
| `D4` | 97 | `0097` | utile per il numero del Pokédex |
| `N0` | 1234 | `1,234` | separatore delle migliaia |
| `N2` | 1234.5 | `1,234.50` | migliaia e 2 decimali |
| `F1` | 75.65 | `75.7` | 1 decimale, arrotondato |
| `F0` | 75.6 | `76` | nessun decimale |
| `P0` | 0.85 | `85 %` | percentuale (moltiplica per 100, con uno spazio) |
| `0%` | 0.85 | `85%` | percentuale senza spazio |
| `X2` | 255 | `FF` | esadecimale (interi) |
| `0.0` | 7 | `7.0` | formato personalizzato: `0` è una cifra sempre presente |
| `#,##0` | 1234 | `1,234` | `#` è una cifra presente solo se serve |
| `+0;-0;0` | 5 | `+5` | segno anche per i positivi (positivi;negativi;zero) |
| `00 kg` | 7 | `07 kg` | il testo fisso si può scrivere nel formato |

Un formato non valido, o un formato numerico su un testo, è un errore nel pannello Problemi.

### Valori vero/falso

`visible` accetta `true`, `false`, `"{hasType2}"` o `"{!hasType2}"`. Le condizioni più complesse, e le somme, non si scrivono nel template: le prepara chi produce il pacchetto dati (`hasType2`, `hasMove4`, `isShiny`, `stat.total`…).

## Valori

I valori del template danno un nome a testi, numeri, colori, immagini e font, e si usano come le chiavi del pacchetto dati: `"{title}"`, `"{frame}"`. Sono raggruppati per tipo, come nel pacchetto dati:

```json
"values": {
  "text":  { "BASE": "https://example.org/cards", "title": "{species.name:upper} Lv. {level}", "lv": { "en": "Lv.", "it": "Liv." } },
  "int":   { "pad": 6 },
  "float": { "ratio": 1.5 },
  "bool":  { "single": "{!hasType2}" },
  "color": { "accent": "{type1.color}", "panel": "#FFFFFFC0" },
  "image": { "frame": "images/frame.png", "sprite": "{BASE}/sprites/{species:D4}.png" },
  "font":  { "pixel": "fonts/PocketPixelRegular.ttf", "body": { "en": "fonts/Kanit.ttf", "ja": "fonts/NotoSansJP.ttf" } }
}
```

I nomi sono unici fra i tipi e non possono essere una chiave del pacchetto dati. Cosa può contenere un valore dipende dal tipo:

| Tipo | Si scrive |
|---|---|
| `color`, `int`, `float`, `bool` | il valore (`"#30303080"`, `6`, `1.5`, `true`) o un riferimento a un valore dello stesso tipo (`"{type1.color}"`, `"{level}"`, `"{!hasType2}"`); un intero va bene dove serve un decimale |
| `text` | un testo con riferimenti e formati: `"{species.name} Lv. {level}"`, `"#{species:D4}"` |
| `image`, `font` | un percorso, anche composto: `"images/frame.png"`, `"{BASE}/{species}.png"`; oppure un riferimento a un'immagine o a un font (`"{species.sprite}"`). Un font può anche essere `"system:Arial"`. |

`{language}` si può usare sempre: è la lingua dell'elemento che si sta disegnando (`"badges/{language}/fire.png"`).

Ogni tipo si può scrivere in quattro forme: singolo, per lingua, set, pattern.

### Valori per lingua

`{ "en": …, "it": … }` dà un valore per lingua, come i testi tradotti del pacchetto dati: `"en"` è obbligatorio, una lingua mancante usa la lingua base, poi l'inglese (vedi [Lingue](#lingue)). Vale per ogni tipo: un'etichetta, un colore, un'immagine, un font per il giapponese.

### Set

Un set sceglie un elemento con una chiave che arriva dai dati: `{set[chiave]}` prende il valore di `chiave` (es. `move1.type.key` = `fire`) e lo cerca nel set.

```json
"values": {
  "color": { "typeColor": { "items": { "fire": "#F08030", "water": "#6890F0" }, "fallback": "#888888" } },
  "text":  { "typeShort": { "items": { "fire": { "en": "FIR", "it": "FUO" } }, "fallback": "{move1.type.name}" } },
  "image": {
    "typeIcon": { "items": { "fire": { "en": "{BASE}/0001.png", "it": "{BASE}/0002.png" } }, "fallback": "{BASE}/0000.png" },
    "artwork":  { "pattern": "artwork/{key}.png", "fallback": "artwork/unknown.png" }
  }
}
```

```json
{ "type": "shape", "rect": [0, 0, 46, 46], "fill": "{typeColor[move1.type.key]}" },
{ "type": "text", "rect": [50, 0, 80, 20], "text": "{typeShort[move2.type.key]:upper}", "font": "{pixel}", "size": 8 },
{ "type": "image", "rect": [0, 0, 64, 64], "image": { "set": "artwork", "key": ["{species}-{form}", "{species}"] } }
```

- **`items`** elenca un valore per chiave; ognuno può essere per lingua.
- **`pattern`** (solo testi, immagini e font) costruisce l'elemento dalla chiave, `{key}` nel pattern: i file non sono elencati, quindi un set può contenere migliaia di artwork.
- **`fallback`** si usa per una chiave che il set non ha (per un pattern di file, il cui file non esiste). Senza, quella chiave è un errore quando si crea la card.
- `"{set[chiave]}"` accetta una chiave; `{ "set": …, "key": … }` accetta anche una chiave con formato (`"{species:D4}"`) o candidati provati in ordine. Si usano ovunque vada un valore del tipo del set: colori, numeri, `visible`, testi con formati, immagini, altri valori (`"accent": "{typeColor[type1.key]}"`).
- Una chiave contiene lettere, cifre, `-` e `_`, in parti separate da `/` (`shiny/female/3`); anche un numero è una chiave (`{dexColor[species]}` con `"6": …`).

### Nell'editor

Ogni tipo ha il suo pannello. A sinistra ci sono i valori del pacchetto dati nella lingua dell'anteprima; a destra quelli del template, ognuno come tabella: una riga per un valore singolo, oppure una riga per elemento (più il pattern e il fallback), e una colonna per lingua.
- **Forma** passa fra singolo, set e pattern.
- **+ Lingua** aggiunge una colonna (il JSON cambia quando riempi una cella); ✕ su una colonna toglie quella lingua dal valore.
- Una cella vuota mostra in grigio il valore usato al suo posto. Sotto ogni riga c'è quello che dà con i dati attuali: il testo, il colore, l'immagine, un campione del font.
- **{ }** inserisce il riferimento al valore nel JSON. Il template d'esempio `variables` mostra le forme.

## Stili

Uno stile raccoglie proprietà comuni a più elementi. Quelle scritte nell'elemento hanno la precedenza.

```json
"styles": { "label": { "type": "text", "font": "{pixel}", "size": 8, "color": "#000000" } }
```

- Con `"type"` lo stile è per un tipo di elemento e può impostarne tutte le proprietà; nelle proprietà degli elementi vengono proposti solo gli stili adatti.
- Senza `"type"` può impostare solo proprietà che l'elemento ha (ad esempio `opacity`): se uno stile con `font` viene dato a un'immagine, il pannello Problemi lo segnala.
- Uno stile non contiene la posizione né il contenuto: `rect`, `text` e `image` sono di ogni elemento.

Gli stili si gestiscono dal pannello **Stili**: **+** ne crea uno, il menu ne sceglie il tipo, **✎** lo apre nel pannello Proprietà per modificarne i valori. Rinominare uno stile aggiorna gli elementi che lo usano.

## Testi

Un testo si dispone nel suo `rect` secondo `align` e `valign`, e non va a capo da solo: `\n` va a capo. Un testo più lungo del rettangolo viene disegnato oltre il bordo, a meno che `overflow` sia `crop` (tagliato al bordo) o `ellipsis` (ogni riga troppo lunga viene accorciata e finisce con `ellipsis`, `"."` se non indicato). Usa `ellipsis` per nomi, soprannomi e mosse: la loro lunghezza cambia con il Pokémon e con la lingua. Tutte le proprietà dei testi: [riferimento del template](template-reference.it.md#text).

Un font di sistema rende il template dipendente dal computer: se manca, la card non si genera. Per template da condividere conviene mettere il file del font nella cartella.

Un font contiene solo i caratteri disegnati dal suo autore; quelli mancanti appaiono come quadratini. I font pixel dei template di esempio (Pocket Pixel, Pokemon Pixel Font) non hanno lettere accentate (é, ö, ü…) né simboli come ♂ ♀ ★; Kanit ha le lettere accentate ma non quei simboli; giapponese, cinese e coreano richiedono un font che li contenga, come Noto Sans JP. Disegna i simboli con forme o immagini invece che come testo.

## Immagini

`image` può essere:

| Valore | Da dove arriva |
|---|---|
| `"{frame}"` | un valore di `values.image` |
| `"{species.sprite}"` | un'immagine del pacchetto dati |
| `"{artwork[species]}"`, `{ "set": "artwork", "key": "{species}" }` | un elemento di un set (vedi [Set](#set)) |
| `"images/frame.png"`, `"badges/{type1.key}.png"` | un file, anche composto con riferimenti |

`fit` decide la dimensione (`none` originale, `contain` intera dentro il rettangolo, `cover` a riempirlo, `stretch` deformata, `scale` moltiplicata per `scale`), `anchor` la posizione nel rettangolo, `overflow: "crop"` taglia ciò che esce dal rettangolo, `flip` la rispecchia. Tutte le proprietà delle immagini: [riferimento del template](template-reference.it.md#image).

### Dove possono stare i file

Immagini e font sono percorsi:

| Scritto come | Significato |
|---|---|
| `images/frame.png` | relativo alla cartella del template (non può uscirne) |
| `file:///C:/Carte/frame.png` | un file in qualsiasi cartella |
| `https://…/frame.png` | un file sul web, scaricato una volta per sessione |

I percorsi scritti senza riferimenti si verificano all'apertura del template; quelli che dipendono dai dati (`"{BASE}/{species}.png"`, gli elementi di un pattern) si leggono quando si crea la card, una volta sola. Un percorso con `{language}` prova la lingua, la lingua base e l'inglese, e prende il primo file che esiste: per una card in italiano `badges/{language}/fire.png` prova `badges/it/fire.png`, poi `badges/en/fire.png`. Il template `artwork` usa così i badge dei tipi.

Le immagini di [PokeAPI](https://github.com/PokeAPI/sprites) hanno le chiavi già pronte nei dati: `pokeapi.path` per i render di HOME e `pokeapi.artwork` per gli artwork ufficiali contengono già la forma, lo shiny e (solo HOME) la variante femmina, come `10034` (Mega Charizard X) o `shiny/female/3`:

```json
"values": {
  "text": { "HOME": "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home" },
  "image": { "home": { "pattern": "{HOME}/{key}.png", "fallback": "images/unknown.png" } }
},
"elements": [ { "type": "image", "rect": [0, 0, 200, 200], "image": { "set": "home", "key": ["{pokeapi.path}", "{pokeapi.id}", "{species}"] }, "fit": "contain" } ]
```

Un file che non è sul server (404) vale come mancante; gli altri errori di download finiscono nel pannello Problemi, e **Aggiorna** (F5) riprova.

## Lingue

Un testo dei dati può essere uguale in tutte le lingue (`"nickname": "Drago"`) o tradotto (`"species.name": { "en": "Charizard", "de": "Glurak" }`; l'inglese è obbligatorio). La lingua usata è, in ordine:

1. **Forza lingua** dell'elemento (`language` del testo o dell'immagine);
2. nell'editor, la **Lingua anteprima** se non è *auto*;
3. **Forza lingua** del template;
4. la lingua del pacchetto dati, cioè di chi usa la card;
5. l'inglese.

Se una traduzione manca si prova la lingua base (`pt-BR` → `pt`), poi l'inglese. Di solito non si forza niente: la lingua arriva dal pacchetto dati.

Nella barra in alto, **Lingua anteprima** permette di provare la card in un'altra lingua: vince anche sulla lingua del template, senza cambiare template né pacchetto dati. Con *auto* valgono le regole sopra. L'export dall'editor usa la stessa lingua dell'anteprima.

## Export

**Esporta PNG** (Ctrl+E) salva la card con la modalità e la scala del canvas. Il nome proposto è `{nome del template}_{nome del Pokémon}.png`, ad esempio `Spotlight_Drago.png`: il nome del Pokémon è il soprannome, o la specie nella lingua del pacchetto dati.

## Il pacchetto dati

```jsonc
{
  "formatVersion": 1,
  "description": "Charizard Lv. 50",
  "name": "Drago",                      // nome per i file: soprannome, o la specie
  "language": "it",                     // lingua di chi usa la card
  "values": {                           // gli stessi tipi dei valori del template (senza font)
    "text":  { "nickname": "Drago", "species.name": { "en": "Charizard", "it": "Charizard" } },
    "int":   { "level": 50, "species": 6 },
    "float": { },
    "bool":  { "hasType2": true },
    "color": { "type1.color": "#EE8130" },
    "image": { "species.sprite": "sprites/charizard.png" }
  }
}
```

Ogni valore può essere per lingua (`{ "en": …, "it": … }`). Le immagini sono file relativi al pacchetto, oppure PNG scritti in Base64: `{ "png": "iVBORw0…" }`.

I pannelli dei valori mostrano tutte le chiavi, tipo per tipo; doppio clic su una chiave la inserisce nel JSON. Le chiavi di un Pokémon esportato dal plugin, con significato e valori possibili, sono nelle [chiavi dei dati](data-keys.it.md); i pacchetti di esempio in `resources/data` hanno le stesse chiavi.

## Problemi frequenti

| Messaggio | Cosa fare |
|---|---|
| *The card data has no value "x"* | La chiave non esiste nel pacchetto dati: controlla il nome nei pannelli dei valori. |
| *Value "x" is text, expected color* | Il binding punta a un valore del tipo sbagliato. |
| *format "D3" is only valid for numbers* | I testi accettano solo `upper` e `lower`. |
| *Style "label" is for text elements* | Lo stile è per un altro tipo di elemento: scegline uno adatto. |
| *File not found* | Il percorso è relativo alla cartella del template: controlla maiuscole ed estensione. |
| *Download of … failed* | Problema di rete o indirizzo sbagliato; F5 riprova. |
| *The font "X" is not installed* | Il template usa un font di sistema che manca: installalo o metti il file nel template. |
