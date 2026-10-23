# PokePic

[English](README.md) | **Italiano**

Card PNG di Pokémon generate da template JSON, con un editor standalone (Avalonia). I dati di un Pokémon arrivano da un *pacchetto dati* JSON, che il plugin per PKHeX produce per il Pokémon selezionato (e che chiunque può scrivere).

La **[guida d'uso](docs/guide.it.md)** spiega l'editor e il formato dei template a chi crea le card; il **[riferimento del template](docs/template-reference.it.md)** elenca tutte le proprietà e le **[chiavi dei dati](docs/data-keys.it.md)** i dati di un Pokémon. Questo README spiega come compilare, avviare e usare il plugin per PKHeX.

## Avvio rapido

```
dotnet build
dotnet test
dotnet run --project src/PokePic.Editor -- --lang it
```

L'editor si apre con il primo template e il primo pacchetto dati di esempio. Il menu **Esempi** carica gli altri template e pacchetti dati.

### Avviare l'editor

Serve l'[SDK di .NET 10](https://dotnet.microsoft.com/download). Dalla cartella del repository, `dotnet run --project src/PokePic.Editor` compila e avvia l'editor. Dopo `dotnet build` si può avviare anche direttamente l'eseguibile: `src/PokePic.Editor/bin/Debug/net10.0/PokePic.Editor.exe` su Windows (`PokePic.Editor` su Linux e macOS). Opzioni: `--lang it|en` (lingua dell'interfaccia), `--template <template.json>`, `--data <pacchetto.json>`.

### Template scaricati da internet

Un template è una **cartella**: `template.json` insieme ai suoi file (font, immagini), a cui si riferisce con percorsi relativi. Per usare un template scaricato:

1. scompattalo mantenendo la sua cartella così com'è, dove preferisci (ad esempio `Documenti\PokePic\Templates\<nome del template>\`);
2. nell'editor, **File → Apri template…** e scegli il suo `template.json` (oppure avvia l'editor con `--template <percorso>\template.json`).

Non spostare `template.json` fuori dalla sua cartella, o i percorsi relativi non funzionano più. I file che il template prende dal web (`http://`, `https://`) vengono scaricati quando il template si apre: un template può contenere solo immagini, font e testi, nessun codice. Il menu **Esempi** elenca solo i template forniti con l'editor.

### Plugin per PKHeX

Serve [PKHeX](https://projectpokemon.org/home/files/file/1-pkhex/) 26.10.18 (il plugin è compilato con il suo PKHeX.Core: altre versioni potrebbero non caricarlo).

1. Crea il pacchetto: `pwsh build/package-plugin.ps1` produce `artifacts/PokePic-PKHeX/plugins` (e uno zip).
2. Copia quella cartella `plugins` accanto a `PKHeX.exe` (unendola a una già presente). Se hai scaricato i file, sbloccali (proprietà del file → *Annulla blocco*).
3. Avvia PKHeX: **Strumenti → PokePic** ha *Esporta card…*, *Esporta box come card…* e *Importa card…*. Anche una card trascinata su PKHeX viene importata, con gli stessi messaggi quando non si può (file modificato, Pokémon non convertibile nel salvataggio aperto).

```
PKHeX.exe
plugins/
  PokePic.Plugin.dll        il plugin: una sola DLL con dentro tutte le sue dipendenze
  PokePic/
    PokePic.Editor.exe      l'editor, in un solo file
    templates/
      pixelcard/            una cartella per template (template.json e i suoi file)
      lightspot/
      artwork/
```

**I template vanno in `plugins/PokePic/templates`**, una cartella ciascuno: un template scaricato da internet si scompatta lì, mantenendo la sua cartella. La finestra di export li elenca con l'anteprima di quello scelto per il Pokémon in modifica; *Modifica template* lo apre nell'editor con i dati di quel Pokémon, *Nuovo template…* crea una cartella con un template minimo e lo apre. Dopo aver salvato nell'editor, l'anteprima si aggiorna quando torni alla finestra.

Il plugin passa i dati all'editor da riga di comando come file (`--data`, un pacchetto dati nella cartella temporanea): i dati contengono gli sprite, troppo grandi per la riga di comando. Il `name` del pacchetto dati è il nome che PKHeX dà al file del Pokémon (`0025 - Pikachu - AFC21B886487`), quindi una card si salva come `Spotlight_0025 - Pikachu - AFC21B886487.png`. Il PNG esportato contiene il Pokémon nel chunk `pokepic`, quindi *Importa card…* lo restituisce identico byte per byte.

PKHeX carica ogni `.dll` che trova sotto `plugins` (sottocartelle comprese): per questo il plugin è una sola DLL, l'editor un unico `.exe`, e in `plugins/PokePic` non va messo nient'altro che contenga `.dll`. La libreria nativa del plugin (SkiaSharp) viene estratta al primo uso in `%LOCALAPPDATA%\PokePic\native`.

I pannelli (Livelli, JSON, Proprietà, Problemi, i pannelli dei valori, Stili) si agganciano come in Visual Studio: trascinali per spostarli, metterli in tab con altri pannelli o staccarli in una finestra; chiudili con la ✕ e riaprili dal menu **Visualizza**, che li rimette dove erano. Il layout viene salvato all'uscita (`%APPDATA%\PokePic\editor-layout.json`); **Visualizza → Ripristina layout dei pannelli** torna a quello predefinito.

- **Livelli**: albero in ordine di disegno (in alto i livelli più in basso; un nuovo livello va in fondo alla lista e sopra all'immagine). Barra per aggiungere (＋: gruppo, forma, testo, immagine, linea), duplicare, spostare su/giù; il clic destro (anche sul canvas) apre il menu con taglia, copia, incolla, duplica, raggruppa, separa, elimina. Ctrl+C/X/V copiano gli elementi come JSON con coordinate del canvas, quindi restano al loro posto anche incollati in un altro gruppo. Trascina un livello per riordinarlo o per metterlo in un gruppo (fascia centrale della riga del gruppo): mantiene la sua posizione sul canvas. Ctrl+clic seleziona più livelli (per raggrupparli, spostarli o modificarli insieme). F2 o doppio clic rinomina, Canc elimina (Ctrl+Z annulla); la casella attiva/disattiva la visibilità (disabilitata se la visibilità è legata ai dati). Selezionando un livello, il suo testo viene evidenziato nel JSON.
- **JSON**: il testo del template. È la fonte di verità: ogni modifica visuale lo riscrive (i commenti si perdono) ed è annullabile con Ctrl+Z.
- **Proprietà**: i campi dell'elemento selezionato, o del template se non c'è selezione (nome, lingua, canvas). Con più elementi selezionati compaiono le proprietà in comune: un valore uguale per tutti viene mostrato, valori diversi appaiono come *{valori multipli}*; ciò che scrivi vale per tutti (nei rettangoli solo le parti modificate). I nomi sono in sola lettura. Ogni campo accetta un valore o un binding `{chiave}`; il menu a tendina propone i binding compatibili. Un campo vuoto usa lo stile o il default, mostrati in grigio. Il valore si applica con Invio o cliccando altrove.

Sul canvas: il primo clic seleziona il livello più in alto sotto il mouse, anche se davanti alla selezione; trascinando un elemento già selezionato si sposta tutta la selezione. Ctrl+clic aggiunge o toglie un elemento; un clic sullo sfondo grigio seleziona il template. Le maniglie (con un solo elemento) ridimensionano o spostano gli estremi di una linea; frecce per spostare di 1 px (Maiusc: 10 px), Canc per eliminare. Un gruppo selezionato dall'albero si sposta trascinandolo dentro il suo riquadro. Le coordinate legate ai dati non si spostano col mouse.

- **Problemi**: errori con riga; doppio clic apre il JSON alla riga.
- **Pacchetto dati** (barra in alto): il pacchetto in uso (menu **Esempi** e **Apri pacchetto dati…**) e la sua lingua.
- **Lingua anteprima** (barra in alto): di norma la lingua del pacchetto dati; un'altra lingua serve a provare le grafiche (vedi *Lingue*).
- **Testi, Numeri, Vero/falso, Colori, Immagini, Font**: un pannello per tipo di valore. A sinistra i valori del pacchetto dati (sola lettura; doppio clic inserisce `{chiave}` nel JSON), a destra quelli del template, ognuno come tabella chiavi × lingue (singolo, set o pattern), con quello che ogni riga dà con i dati attuali: testo, colore, miniatura, campione del font. "+ file…" copia un font o un'immagine nella cartella del template.
- **Stili**: gli stili del template (✎ apre uno stile nel pannello Proprietà).

Sotto il canvas, come in Paint.NET: dimensioni del canvas, posizione del cursore, zoom (menu con i valori predefiniti), pulsante *adatta alla finestra ⇄ 100%*, cursore dello zoom con − e +. Lo zoom può essere qualsiasi percentuale: in pixel-perfect l'anteprima viene ingrandita senza sfumature (con zoom non interi i pixel hanno larghezze leggermente diverse, come in ogni editor di immagini), sotto il 100% viene ridotta in modo morbido; in alta risoluzione si ridisegna alla scala dello zoom. Ctrl+rotellina, Ctrl++ e Ctrl+− cambiano zoom, Ctrl+B adatta alla finestra, Ctrl+0 torna al 100%. Lo zoom vale solo per l'anteprima.
- **Esporta PNG** (Ctrl+E): con la modalità (`canvas.mode`, pixel-perfect o alta risoluzione) e la scala (`canvas.scale`) del template, che si cambiano nelle proprietà del canvas. Anche l'anteprima usa la modalità del template. Il nome proposto è `{nome del template}_{nome del Pokémon}.png` (`"name"` del pacchetto dati; se manca, il soprannome o la specie nella lingua del pacchetto).

Se modifichi un'immagine o un font del template con un altro programma, o se il pacchetto dati cambia su disco, l'anteprima si aggiorna.

Da riga di comando:

```
PokePic.Editor --template <template.json> --data <pacchetto.json> [--lang it|en]
pokepic render --template <template.json> --data <pacchetto.json> [--mode pixelPerfect|highResolution] [--scale N] --out card.png
pokepic check  --template <template.json> --data <pacchetto.json>
pokepic format --template <template.json>     # riscrive il file nel formato dell'editor
```

## Template e pacchetti dati

Un template è una cartella con `template.json` e i suoi file (font, immagini); un pacchetto dati è un file JSON con i valori e le immagini di un Pokémon. Come scriverli:

- [Guida d'uso](docs/guide.it.md): l'editor, il formato del template, binding e formati, valori e set, stili, immagini, lingue, pacchetti dati.
- [Riferimento del template](docs/template-reference.it.md): tutte le proprietà, con valori, limiti e valore predefinito.
- [Chiavi dei dati](docs/data-keys.it.md): i valori e le immagini di un Pokémon disponibili ai template, con i valori possibili.

I template di esempio sono in `resources/templates` (quelli distribuiti con l'editor e il plugin), i pacchetti dati di esempio in `resources/data`, i crediti di font e immagini in `resources/CREDITS.md`. Il riferimento e le chiavi dei dati sono generati dal codice, con cui vengono verificati: dopo aver cambiato proprietà, valori o chiavi dei dati, esegui `dotnet run build/generate-reference.cs -- docs`.

## Template creati da un agente AI

Un agente AI di programmazione (che sappia leggere e scrivere file ed eseguire PowerShell) può trasformare una descrizione o uno schizzo di una card in un template e verificarlo con PokePic stesso. `pwsh build/package-agent.ps1` crea il pacchetto da dargli, in `artifacts/PokePic-AgentPackage` (e uno zip): le istruzioni per l'agente (`AGENTS.md`), la guida e i riferimenti in inglese, template d'esempio con l'output atteso per ogni pacchetto dati di esempio, i pacchetti dati e la CLI di PokePic come singolo programma Windows (senza bisogno di .NET). I suoi sorgenti sono in [`agent-package/`](agent-package/README.md).

## Disclaimer

PokePic è un progetto amatoriale, non ufficiale e senza scopo di lucro. **Non è affiliato, approvato, sponsorizzato o autorizzato** da Nintendo, Game Freak, Creatures Inc. o The Pokémon Company.

Pokémon e i nomi dei personaggi Pokémon sono marchi di Nintendo, Creatures Inc., Game Freak e The Pokémon Company. Tutte le immagini di Pokémon, strumenti, tipi e badge usate negli esempi, presenti nel repository o scaricate dai template di esempio (sprite, artwork, icone e badge dei tipi) sono © Nintendo / Creatures Inc. / GAME FREAK Inc. / The Pokémon Company. **Non appartengono all'autore di PokePic**: sono incluse o richiamate solo a scopo esemplificativo, per mostrare cosa fa il programma, e non se ne rivendica alcuna proprietà.

La licenza del codice sorgente di PokePic, qualunque sia, non si estende a queste immagini. Se sei titolare dei diritti e vuoi che qualcosa venga rimosso, apri una issue.
