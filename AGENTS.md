# PokePic — linee guida per Claude Code

## Obiettivo

**PokePic** è un sistema per creare **card PNG** di Pokémon a partire da **template** grafici:

- un **editor standalone multipiattaforma** (Avalonia) per creare i template e generare immagini;
- un **plugin per PKHeX** (C#/.NET WinForms) che esporta il Pokémon selezionato come card PNG contenente i suoi dati in modo lossless, e che permette di **reimportare** quella card.

Una card è il rendering di un template applicato a un `CardData`. Il template definisce elementi statici e dinamici; quelli dinamici cambiano colore, testo o immagine in base ai dati.

Quando il plugin apre l'editor, gli passa un **pacchetto dati** con valori e immagini del Pokémon selezionato (in sola lettura), così il template si costruisce su valori veri.

## Stack

- .NET 10 (allineato a PKHeX.Core 26.10.18), C# `latest`, nullable abilitato, warning come errori
- SkiaSharp 3.119.4 per il rendering — **fissato alla versione da cui dipende Avalonia**: editor e renderer girano nello stesso processo
- Avalonia 12 + AvaloniaEdit + CommunityToolkit.Mvvm + Dock.Avalonia (docking dei pannelli) per l'editor
- PKHeX.Core (NuGet) per i dati Pokémon; sprite dal `PKHeX.Drawing.PokeSprite` già caricato in PKHeX
- xUnit v3 su Microsoft.Testing.Platform (`global.json`)
- Versioni dei pacchetti centralizzate in `Directory.Packages.props`; `nuget.config` usa solo nuget.org

## Struttura

```
src/
  PokePic.Core/        modello template, binding, validazione, CardData, pacchetti dati; NESSUNA dipendenza da PKHeX né da SkiaSharp
  PokePic.Rendering/   motore di rendering SkiaSharp (ICardRenderer)
  PokePic.Editor/      editor Avalonia: JSON + anteprima live + dati + export
  PokePic.Cli/         `pokepic render|check`, per batch e debug
  PokePic.Png/         payload nel PNG (chunk iTXt "pokepic"), IPngPayloadCodec
  PokePic.Adapter/     PKM (PKHeX.Core) -> DataPack (PkmAdapter), byte del PKM (PkmFile)
  PokePic.Plugin/      plugin PKHeX (WinForms): menu Strumenti → PokePic, finestra template + anteprima, export, import, avvio editor
build/package-plugin.ps1     pacchetto per PKHeX: artifacts/PokePic-PKHeX (+ zip)
build/package-agent.ps1      pacchetto per un agente AI che scrive template: artifacts/PokePic-AgentPackage (+ zip)
build/generate-reference.cs  genera docs/template-reference*.md e docs/data-keys*.md dal codice (e lo schema JSON per l'agente)
build/generate-pokeapi.cs    genera src/PokePic.Adapter/PokeApi.json (immagini PokeAPI di ogni forma); correzioni in build/pokeapi-overrides.json
resources/templates/   template distribuiti con editor e plugin (cartella con template.json + asset)
resources/data/        pacchetti dati di esempio (editor, test, validazione dell'agente)
resources/CREDITS.md   licenze dei font e disclaimer sulle immagini, copiato nei pacchetti
agent-package/         sorgente del pacchetto per l'agente: AGENTS.md, README.md, validate.ps1, tools/, examples/ (template d'esempio, intent.md)
docs/                  guida utente e riferimenti generati: nome.md (inglese) + nome.it.md (italiano)
tests/                 unit test, golden image test (tests/golden/)
```

Le dipendenze vanno in una sola direzione: Plugin/Editor/Cli → Adapter/Rendering/Png → Core. Core non deve mai referenziare PKHeX o SkiaSharp.

Le cartelle `PKHeX/` e `XmlPictureCreation/` sono i vecchi progetti, solo come riferimento: non fanno parte della soluzione. Il fork di PKHeX è abbandonato: il plugin si prova con l'ultima release ufficiale di PKHeX.

## Principi

- **Dati separati dal PKM.** Il rendering lavora solo su `CardData`: valori tipizzati per chiave (`ValueKind`: text, int, float, bool, color, image; le immagini sono `ImageValue` PNG), ognuno singolo o per lingua (`LocalizedValue`, "en" obbligatorio). L'Adapter è l'unico punto che conosce `PKM`.
- **Pacchetto dati.** `CardData` serializzato in JSON (`DataPackSerializer`, `formatVersion` 1): `values` con le sezioni `text`, `int`, `float`, `bool`, `color`, `image`, come i valori del template (senza `font`); ogni valore singolo o per lingua; un'immagine è un percorso o `{ "png": Base64 }`. È il ponte fra PKHeX e l'editor standalone, e il formato degli esempi.
- **Template dichiarativi.** Canvas con dimensioni logiche e lista ordinata di elementi `group`, `image`, `shape`, `line`, `text`. **Ogni elemento è un livello**, disegnato sopra il precedente. Un `group` ha `position` e `children`: le coordinate dei figli (compresi i gradienti) sono relative al gruppo. Ogni proprietà è un valore statico oppure un binding (`"{level}"`, `"{type1.color}"`, `"{!hasType2}"`, `"Lv. {level:D3}"`). Nessuna logica di template nel codice C#; le condizioni composte le calcola l'Adapter (`hasType2`, `hasMove4`).
- **Lingue.** Ogni valore (dei dati e del template) può essere per lingua. Lingua di un elemento: `language` dell'elemento → `RenderOptions.LanguageOverride` (la "Lingua anteprima" dell'editor, se non è auto) → `language` del template → `RenderOptions.Language` (passata da PKHeX, nel pacchetto dati come `language`) → "en"; lingua mancante → lingua base → "en" (`Languages.Pick`). `{language}` è sempre un testo: la lingua dell'elemento.
- **Valori del template.** Sezione `values` con le sezioni per tipo (`text`, `int`, `float`, `bool`, `color`, `image`, `font`): il tipo lo dà la sezione (`TemplateValue`), i nomi sono unici fra le sezioni e diversi dalle chiavi dei dati; `language` e `key` sono riservati. Quattro forme per ogni tipo: singolo, per lingua (`{ "en": …, "it": … }`), set (`{ "items": {…}, "fallback": … }`), pattern (`{ "pattern": "art/{key}.png", "fallback": … }`, solo testi, immagini, font). Colori, numeri e bool: valore o alias dello stesso tipo (un int va dove serve un float); testi, immagini e font: composti di testi e numeri (`"{BASE}/{species:D4}.png"`), o un riferimento dello stesso tipo (`"{frame}"`, `"{species.sprite}"`). Niente calcoli: somme e condizioni le prepara l'Adapter (`stat.total`, `iv.total`, `ev.total`). Li risolve `BindingResolver`, li controlla `TemplateValidator` (tipi, cicli, chiavi mancanti).
- **Immagini e font.** `image` e `font` degli elementi sono valori di tipo immagine o font: `"{frame}"`, `"{species.sprite}"`, `"{set[key]}"`, `{ "set": …, "key": [candidati] }`, oppure un percorso anche composto; un font può essere `"system:Famiglia"` (il renderer lo cerca con `SKFontManager` e fallisce se non è installato). Il renderer (`FileCache`) carica i file fissi alla preparazione e gli altri al primo uso.
- **Set.** `{set[chiave]}` (`Binding.Set`) prende il valore della chiave come testo invariante e cerca l'elemento; `{ "set": …, "key": … }` (`SetLookup`, `LookedUp<T>` nelle proprietà) accetta chiavi con formato o candidati. Vince la prima chiave che il set ha (per un pattern di immagini o font, la prima il cui file esiste), poi il fallback; senza, errore al rendering. Si usa ovunque vada un valore del tipo del set.
- **Stili.** Proprietà condivise fra elementi; quelle dell'elemento hanno la precedenza. Con `"type"` uno stile vale per un tipo di elemento ed è verificato dove è definito; senza, può impostare solo proprietà che l'elemento ha (`ElementProperties`). Nell'editor si modificano dal pannello Stili (✎ apre lo stile nel pannello Proprietà).
- **Percorsi dei file.** Relativi alla cartella del template (non possono uscirne), `file://` assoluto o `http(s)://`; possono dipendere dai dati. Un percorso con `{language}` prova lingua, lingua base ed "en" e prende il primo file esistente. I percorsi fissi si verificano al caricamento, gli altri al rendering. I download passano da `WebAssetCache` (in memoria, una volta per sessione; 404 = file mancante; gli altri errori sono `WebAssetException`, cioè `IOException`, e diventano problemi); `FileSystemAssetProvider` riceve la cache dall'editor o dalla CLI.
- **Validazione al caricamento.** Proprietà sconosciute, binding non risolti, tipi errati, asset mancanti o non decodificabili fanno fallire il caricamento con percorso JSON e riga. Il renderer fallisce solo se i dati cambiano rispetto allo schema validato (es. chiave di un set assente) e lo dice esplicitamente.
- **Editor: il testo JSON è la fonte di verità.** Le modifiche visuali passano da `TemplateDocument` (Core/Editing, JSON mutabile indirizzato con i percorsi del parser, es. `elements[2].children[0]`), vengono riscritte con `TemplateJsonFormatter` e applicate all'editor di testo come una sola modifica annullabile. I commenti non sopravvivono a una modifica visuale. Le coordinate legate ai dati non vengono mai toccate dalle operazioni visuali.
- **Rendering deterministico.** Stesso template + stessi dati + stessa modalità = stessi pixel. Niente dipendenze da tempo, cultura (formattazione invariante) o ordine di dizionari non ordinati (si usano `ImmutableSortedDictionary` con `StringComparer.Ordinal`).

## Modalità di rendering

- **Alta risoluzione:** si disegna alla scala finale, `IsAntialias = true`, immagini con campionamento lineare + mipmap (disattivabile con `"smooth": false`).
- **Pixel-perfect:** si disegna alla risoluzione base con AA disattivato, testo con `SKFontEdging.Alias` e hinting `None`, coordinate arrotondate; poi si ingrandisce con `SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)`. Scala intera.

Regole:
- La modalità è un parametro del renderer (`RenderOptions`); il template ne dà i valori (`canvas.mode`, `canvas.scale`), che l'editor usa per anteprima ed export (non ci sono override nell'editor). Il plugin potrà sovrascriverli.
- In pixel-perfect nessuna primitiva produce pixel semitrasparenti dovuti ad AA (c'è un test). Le semitrasparenze volute (colori con alpha, `opacity`) restano.
- Non scalare mai un testo rasterizzato in alta risoluzione.

## Formato PNG

Il payload va in un chunk `iTXt` con chiave `pokepic`, contenente JSON con:
- `formatVersion`: versione del formato, intero
- `templateId` e `templateVersion`
- `display`: campi leggibili (nome, specie, livello…), solo per la UI
- `pkmFormat`: estensione del file PKHeX (`pk9`…), serve per rileggere i byte
- `pkm`: i byte del PKM in Base64 (dati party decifrati, come un file .pkX di PKHeX: `PkmFile.Bytes`). È la fonte di verità per l'import.
- `checksum`: SHA-256 (hex) dei byte del PKM, per rilevare modifiche manuali

Import: leggere il chunk, verificare `formatVersion` e checksum, ricostruire il PKM dai byte. I campi `display` non vengono mai usati per ricostruire il Pokémon. Se il chunk manca o il formato non è riconosciuto, l'import fallisce con errore esplicito, senza fallback. Nessuna retrocompatibilità con il vecchio chunk `pkHx`.

## Convenzioni di codice

- Nomi in inglese nel codice e nei commenti; testi visibili all'utente localizzabili (`Localization/Strings.resx` + `Strings.it.resx` nell'editor; l'utente lavora in italiano).
- Preferire tipi immutabili (`record`, `init`) per modello, template e CardData.
- Nessun `static` mutabile. Dipendenze iniettate tramite interfacce (`IAssetProvider`, `ICardRenderer`, `IPngPayloadCodec`).
- Niente workaround silenziosi: un errore va propagato o mostrato, mai inghiottito.
- Soluzioni semplici e manutenibili prima di quelle generali; niente astrazioni anticipate.
- Documentazione per l'utente in due lingue: `nome.md` (inglese) e `nome.it.md` (italiano), aggiornate insieme. `docs/template-reference*.md` e `docs/data-keys*.md` si rigenerano con `build/generate-reference.cs`, non si modificano a mano. Il pacchetto dell'agente contiene solo i documenti in inglese.

## Testing

- Unit test su Core (binding, parser, validazione, pacchetti dati).
- Golden image test per il rendering: confronto pixel-by-pixel con i PNG in `tests/golden/`. In pixel-perfect la differenza deve essere zero. Se un golden manca o differisce, l'output reale finisce in `tests/golden-actual/` (ignorata da git).
- Roundtrip test (da fare): `PKM → card PNG → PKM` deve restituire byte identici.
- Nessun test richiede PKHeX in esecuzione; l'Adapter avrà test separati con PKM di esempio.

## Cosa non fare

- Non modificare file in `PKHeX/` né duplicarne il codice: usare i riferimenti NuGet.
- Non usare `System.Drawing` per il rendering. È ammesso solo nel plugin, al confine, per convertire gli sprite `Bitmap` di PKHeX in PNG.
- Non inserire dati del Pokémon nel PNG fuori dal chunk `pokepic`.
- Non rigenerare i golden image senza spiegare nel commit perché il rendering è cambiato.
- Non aggiornare SkiaSharp indipendentemente da Avalonia.

## Stato e prossimi passi

Fatto (round 1): Core (modello, binding, stili, set di immagini, validazione con righe), Rendering nelle due modalità, pacchetti dati, editor Avalonia (anteprima live, problemi, dati, esempi, export PNG, ricarica automatica di asset e dati), CLI, test e golden.

Fatto (round 2): elemento `group`; editor visuale con pannelli richiudibili (livelli ad albero con drag & drop, JSON, proprietà per tipo di elemento), selezione/spostamento/ridimensionamento sul canvas, undo/redo tramite l'editor di testo, gestione di font e immagini del template.

Fatto (round 3): docking con Dock.Avalonia (pannelli trascinabili, tab, finestre flottanti; layout salvato da `LayoutStore`, pannelli nascosti che tornano nel dock originale o nel lato predefinito); icone dei tipi di livello; albero in ordine di disegno; Canc nell'albero; evidenziazione nel JSON del livello selezionato; campi delle proprietà applicati in modo esplicito (`CommitPending`/`FlushPendingEdits`) invece che su perdita di focus.

Fatto (round 4): variabili, font di sistema, totali nei pacchetti dati; multi-selezione (Ctrl+clic su canvas e albero, `MainViewModel._selection`): raggruppa/elimina/duplica/riordina/sposta più elementi, pannello proprietà con i campi comuni (`NodeAccessor` con più slot, `{valori multipli}`, nomi in sola lettura); un clic seleziona l'elemento più in alto anche davanti alla selezione, solo il trascinamento di un elemento già selezionato lo sposta; clic sullo sfondo grigio = template; icone template e selezione multipla nell'intestazione delle proprietà.

Fatto (round 5): barra sotto il canvas stile Paint.NET (zoom libero in percentuale, adatta alla finestra, preset, slider); in pixel-perfect l'anteprima si disegna a scala 1 e la vista la ingrandisce con nearest neighbor; pannello Risorse (variabili con tipo e valore, font con anteprima, immagini, set di immagini modificabili); variabili e `http(s)://`/`file://` nei percorsi; template `variables` dimostrativo; template `artwork` con gli artwork da PokeAPI (nessun file scaricato nel repository).

Fatto (round 6): modalità e scala solo dal template (tolte dalla barra); pacchetto dati e lingua di prova nel pannello Dati; "Forza lingua" su template ed elementi; set di immagini localizzati con `{language}` (badge dei tipi in `templates/artwork/badges`); niente riferimenti a PKHeX nei testi dell'editor.

Fatto (round 7): badge dei tipi (Sword/Shield, solo per test locale) in `resources/templates/artwork/badges/{type}_{lang}.png`; "Lingua anteprima" come override nella barra in alto (il pannello Dati mostra la lingua del pacchetto in sola lettura); stili con `type`; `overflow` (`none`/`crop`/`ellipsis`) ed `ellipsis` nei testi; copia/taglia/incolla e menu contestuale su albero e canvas (elementi negli appunti come JSON con coordinate del canvas); nome dell'export `{template}_{nome}.png` con `"name"` nel pacchetto dati (`DataPack.EffectiveName`); guida utente in `docs/guida.md`.

Fatto (round 8): immagini con `fit: "scale"` + `scale` (float) e `overflow` (`none`/`crop`); pannello Risorse in verticale quando è più alto che largo; documentazione in due lingue: `README.md` (inglese, mostrato da GitHub) e `README.it.md`, `docs/guide.md` e `docs/guida.md`, con i link per cambiare lingua in cima; disclaimer su marchi e immagini Pokémon in fondo ai README. Ogni modifica alla documentazione va fatta in entrambe le lingue.

Editor: il `TextDocument` del JSON vive nel view model (`MainViewModel.JsonDocument`), non nella vista, perché Dock ricrea le viste quando i pannelli vengono spostati. Per lo stesso motivo il TreeView (selezione multipla) non scrive nel view model gli azzeramenti di selezione: la selezione vive nel view model, e la vista la sincronizza con l'evento `SelectionChanged`.

Fatto (round 9): `PokePic.Png` (codec iTXt, roundtrip PKM → card → PKM a byte identici), `PokePic.Adapter` (PKM → DataPack con le chiavi degli esempi, testi in tutte le lingue di PKHeX, colori dei tipi Scarlatto/Violetto, statistiche calcolate su una copia), `PokePic.Plugin` e `build/package-plugin.ps1`. Plugin:
- PKHeX 26.10.18 carica ogni .dll sotto `plugins` in un `PluginLoadContext` proprio (PKHeX.Core condiviso dal contesto di default: nel plugin `ExcludeAssets="runtime"`). Il plugin è una sola DLL: le dipendenze .NET sono risorse `deps/*.dll` caricate da `EmbeddedDependencies` (module initializer + `Resolving` del contesto del plugin), libSkiaSharp per win-x64/arm64 è la risorsa `native/<rid>/libSkiaSharp.dll` estratta da `NativeSkia` in `%LOCALAPPDATA%\PokePic\native\<mvid>`. `PokePic.Plugin.Tests` riproduce quel caricamento con la sola DLL e renderizza una card nel contesto isolato.
- `plugins/PokePic/PokePic.Editor.exe` (pubblicato single-file perché PKHeX non carichi le sue DLL) e `plugins/PokePic/templates/<cartella>/template.json`.
- Il `name` del pacchetto dati è il nome del file PKHeX del Pokémon (`PkmFile.Name`: copia con checksum aggiornato). Scrivere i byte o calcolare il nome avviene sempre su una copia: `WriteDecryptedDataParty` aggiorna il checksum del PKM.
- Trascinare una card su PKHeX passa da `IPlugin.TryLoadFile` (PKHeX chiede prima ai plugin): un PNG che non è una card resta a PKHeX (`CardPayloadReadResult.IsCard`), una card non importabile mostra lo stesso messaggio di "Importa card".
- Icone del menu e della finestra (dal vecchio plugin) in `src/PokePic.Plugin/Resources`, incorporate nella DLL.
- Dati all'editor: pacchetto dati in un file temporaneo passato con `--data` (le immagini sono troppo grandi per la riga di comando; l'editor ricarica il file quando cambia).
- Sprite da `PKHeX.Drawing.PokeSprite` via reflection (non è su NuGet), convertiti in PNG in `PkhexSprites`.
- Testi del plugin in en/it nel codice (`Text`), non in resx: le assembly satellite finirebbero sotto `plugins`.

Fatto (round 10): template `lightspot` (prima `showcase`, poi `spotright`; alta risoluzione, 600×600, ispirato alle spotlight di Smogon: font Kanit OFL in `fonts/`, badge dei tipi, artwork 3D di HOME da PokeAPI); `spotlight` rinominato `pixelcard` e `summary` (pixel-perfect, 256×192, schermata di riepilogo: barre degli IV larghe quanto il valore 0–31). Nuove chiavi nei dati: `base.*`, `base.total`, `nature.up.*`, `nature.down.*` (Adapter e campioni).

Fatto (round 11): ordine delle lingue con `RenderOptions.LanguageOverride` (Lingua anteprima, "auto" = nessun override); chiavi `*.id` e `*.id.pokeapi` per tipi, tipi delle mosse e tera (id = `MoveType`; il Stellar che PKHeX salva come 99 diventa 18, PokeAPI = id + 1), `teratype.*`, `hasTera` (solo PK9 in contesto Gen9), `hasMega` (`FormInfo.HasMegaForm` e `EntityContext.IsMegaContext`).

Fatto (round 12): pacchetto per un agente AI che crea template. Corretto l'Adapter: `NatureAmp.GetAmps` è in ordine atk, def, spa, spd, spe (prima `nature.up/down` segnavano la statistica sbagliata per spa/spd/spe).

Fatto (round 13): `CLAUDE.md` → `AGENTS.md` (Claude Code ≥ 2.1.277 lo legge se non c'è un CLAUDE.md). `templates/` e `samples/data/` spostati in `resources/` (con `edge-cases.json`: shiny, teratipo Stellar, senza sesso, soprannome accentato, tedesco; lingue dei campioni miste: charizard it, mega-x e hypno en). Documentazione senza ripetizioni:
- README (en/it): compilazione, avvio, plugin; per il formato rimanda a `docs/`.
- `docs/guide.md` + `guide.it.md` (ex `guida.md`): concetti, editor, binding, formati; per le proprietà e le chiavi rimanda ai riferimenti.
- `docs/template-reference*.md` e `docs/data-keys*.md`: generati da `build/generate-reference.cs` (app file-based che importa Core e Adapter): proprietà da `ElementProperties`, enum per reflection, ogni regola verificata con `TemplateParser`, chiavi dal `PkmAdapter` con una descrizione per chiave in `Spec.Describe` (fallisce se una chiave non ha descrizione o se una regola non corrisponde al parser). Dopo aver cambiato proprietà, valori o chiavi: `dotnet run build/generate-reference.cs -- docs` (lo fa anche `package-agent.ps1`).
- `agent-package/AGENTS.md`: solo il lavoro dell'agente (ciclo crea → valida → correggi, difetti della CLI, errori, casi ambigui, checklist, esempi); il formato lo legge in guida e riferimenti.

Pacchetto dell'agente: `build/package-agent.ps1` lo assembla in `artifacts/PokePic-AgentPackage` (cartella conservata, ricreata a ogni build) da `agent-package/`, `resources/` e dai documenti inglesi di `docs/`; pubblica la CLI self-contained in `bin/`, genera `schema/` ed `examples/*/expected/<template>.<pacchetto>.txt|png` con tutti i pacchetti. Si fida del renderer (lo verificano i test): fallisce solo se un esempio valido non funziona con un pacchetto o è rifiutato dallo schema, o se un esempio di errore smette di fallire (`$mustFail`). Gli esempi sono separati dai template del plugin; l'esempio 03 carica M PLUS 1p dal web per il nome in giapponese.

Fatto (round 14): forme e shiny nelle immagini dal web. Le Mega esistono solo in battaglia (`FormInfo.IsBattleOnlyForm`): le forme salvate che contano sono regionali, Rotom, Vivillon… `species.sprite` arriva già giusto da PKHeX (`b_<specie>-<forma>[s]`, lo shiny solo se PKHeX ha attivo `SpriteName.AllowShinySprite`). Chiavi `pokeapi.id` (nome dell'immagine: `6`, `10034`, `666-tundra`), `pokeapi.path` (percorso relativo a una raccolta di immagini di PokeAPI, con le varianti: `shiny/female/3`; varianti verificate nella raccolta HOME, la più completa), `pokeapi.artwork` (artwork ufficiale, solo `shiny/`) da `PokeApi` (Adapter) con la tabella `PokeApi.json` incorporata, generata da `build/generate-pokeapi.cs` (CSV di PokeAPI + albero dei file del repo degli sprite clonato senza blob, cache in `artifacts/pokeapi-cache`): abbina per nome le forme PKHeX di ogni contesto (chiavi `specie-forma`, `@Contesto` per Arceus Gen4 e Pikachu Gen6, `specie-forma-argomento` per Alcremie), le altre in `build/pokeapi-overrides.json`; una forma senza immagine propria (`noImage`/`noArtwork`) usa quella della specie. Le chiavi PokeAPI dei tipi sono `pokeapi.type1.id`, `pokeapi.move1.type.id`… Le chiavi dei set ammettono `/` fra parti (`ImageSet.IsValidKey`, mai `.` né parti vuote). Campioni: sprite di PKHeX per Mega X e shiny, nuovo `vulpix-alola.json` (spagnolo) generato dall'Adapter.

Fatto (round 15): variabili localizzate (`LocalizedTextVariable`, oggetto lingua → testo in `variables`); il pannello Risorse è diviso in sei pannelli (Variabili, Testi localizzati, Font, Immagini, Set di immagini, Stili; sottomenu Visualizza → Risorse), tutti sullo stesso `ResourcesPanel`. Testi localizzati come tabella (`LocalizedTextsTable`: righe = variabili, colonne = lingue; una lingua aggiunta resta colonna vuota finché non si riempie una cella; cella vuota = lingua tolta, col testo di ripiego in grigio); 🌐 trasforma una variabile di testo in localizzata. Gli elenchi usano `ColumnsPanel` (colonne quante ne stanno). Layout salvato alla versione 2: quelli vecchi sono sostituiti dal predefinito.

Fatto (round 16): `imageSets` → `sets`, con set di valori (`ValueSet`) accanto ai set di immagini e sintassi `{set[chiave]}` (anche `"image": "{artwork[species]}"`); pannello Set di valori nell'editor (una tabella chiave → valore per set, i valori per lingua si modificano nel JSON); layout salvato alla versione 3; template `variables` con un testo localizzato e un set di colori per tipo.

Fatto (round 17): valori unificati fra dati e template. Pacchetto dati e template hanno `values` con le sezioni per tipo; immagini dei dati come valori (`ImageValue`), `LocalizedValue` per ogni tipo al posto di `LocalizedTextValue`; nel template `TemplateValue` con quattro forme (singolo, per lingua, `items`, `pattern`), espressioni `LiteralExpr`/`AliasExpr`/`ComposedExpr`/`LookupExpr`; spariti `variables`, `fonts`, `images`, `sets`. Elementi e stili usano `"{nome}"` anche per font e immagini. Renderer con `FileCache` (file fissi alla preparazione, gli altri al primo uso). Editor: un pannello per tipo (Testi, Numeri, Vero/falso, Colori, Immagini, Font; `ValueTabs`/`KindTab`/`ValueItem`), con i valori del pacchetto a sinistra e quelli del template a destra come tabelle chiavi × lingue; il pacchetto dati è nella barra in alto; pannello Stili a parte (`ResourcesPanel`); layout alla versione 4. Template, esempi dell'agente, campioni, guide e riferimenti migrati.

Da fare:
1. Provare il plugin nella release di PKHeX (sprite reali, menu, import da trascinamento) e sistemare quello che emerge
2. Plugin: aggiornare i dati dell'editor quando in PKHeX si cambia Pokémon (riscrivere il file `--data`)
3. Editor: menu dei template della cartella del plugin quando è avviato da lì; export dall'editor con il Pokémon nel PNG quando i dati vengono da PKHeX
4. Catalogo delle chiavi con i domini degli enum (per validare che i set, di immagini e di valori, coprano tutti i valori)

## Comandi

```
dotnet build
dotnet test
dotnet run --project src/PokePic.Editor -- [--template <file>] [--data <pacchetto.json>] [--lang it|en]
dotnet run --project src/PokePic.Cli -- render --template resources/templates/pixelcard/template.json --data resources/data/hypno.json --out card.png
dotnet run build/generate-reference.cs -- docs
pwsh build/package-plugin.ps1
pwsh build/package-agent.ps1
```

Rigenerare i golden (spiegando il motivo nel commit): `POKEPIC_UPDATE_GOLDEN=1 dotnet test`.
