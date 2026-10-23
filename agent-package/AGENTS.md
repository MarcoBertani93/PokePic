# Making PokePic templates

You turn a user's description, sketch or draft of a Pokémon card into a **PokePic template**: a folder with a `template.json` and the files it uses (fonts, images). PokePic draws a card from the template and the data of one Pokémon (a *data pack*). The PokePic plugin for PKHeX applies the template to every Pokémon the user selects, so the template must work for any Pokémon, not only the one in the request.

A template is finished only when `validate.ps1` prints `VALID`. The documents describe the format; only the CLI decides.

## What is in this folder

| Path | What it is |
|---|---|
| `docs/guide.md` | How templates work: canvas, elements, bindings and formats, values (by kind, by language, sets), styles, texts, images, file paths, languages, data packs. **Read it first.** (Its parts about the editor's panels do not concern you.) |
| `docs/template-reference.md` | Every property of every element: values, limits, defaults. Generated from the PokePic code. |
| `docs/data-keys.md` | Every value and image of a Pokémon you can bind, its meaning and its possible values. Generated from the PokePic code. |
| `schema/template.schema.json` | JSON Schema of `template.json`. Passing it is necessary, not sufficient. `schema/data-keys.json`: the keys and their kinds. |
| `examples/` | Worked examples, see *Examples*. |
| `data/` | Test data packs (see below). |
| `bin/pokepic.exe` | The PokePic CLI (Windows x64). |
| `validate.ps1` | Runs `check` and `render` on a template with every data pack. |
| `CREDITS.md` | Licenses of the fonts, disclaimer on Pokémon images. |

The data packs in `data/` are chosen to reach every optional part of a card:

| Pack | Language | What it tests |
|---|---|---|
| `charizard` | Italian | two types, four moves, held item, Tera Type |
| `charizard-mega-x` | English | a Mega form (form 1, Dragon type, PokeAPI image `10034`) |
| `hypno` | English | one type, three moves, no item, female (HOME has a female image: `female/97`), nicknamed "Psych Bro" |
| `edge-cases` | German | shiny (`shiny/6`), Stellar Tera Type, genderless, nickname "Drachenkönig" |
| `vulpix-alola` | Spanish | a regional form (Ice type, PokeAPI image `10103`), made by PokePic.Adapter from a PKHeX Pokémon |

## The loop

1. Read the request. Settle what is ambiguous (see *Ambiguous requests*).
2. Create a folder, e.g. `work/my-card/`, with `template.json` and the files it uses: copy fonts and images from `examples/`.
3. Run:
   ```
   powershell -ExecutionPolicy Bypass -File validate.ps1 -Template work/my-card/template.json
   ```
4. If it prints `INVALID`, read the errors (see *Reading errors*), fix **all** of them, run again, until `VALID`.
5. Look at every PNG in `work/my-card/out/` (one per data pack). Check what the CLI cannot: overlaps, text outside its box or shown as boxes (missing characters), bars longer than their track, poor contrast. Fix and run again.
6. To try a language for every pack: `-Language de` (long words), `-Language ja` (Japanese characters). To try your own data: `-Data <pack.json>`.
7. Deliver the folder (without `out/`) and a short note: what you built, the choices you made, what the user must know (network for web files, licenses of the fonts).

Running the CLI directly, one data pack at a time:

```
bin\pokepic.exe check  --template work/my-card/template.json --data data/hypno.json
bin\pokepic.exe render --template work/my-card/template.json --data data/hypno.json --out card.png
```

Exit code 0: success (`Template is valid.` or `Written card.png (WxH).`). 1: errors on stderr, one per line, after `Template errors:`, `Data pack errors:` or `Render errors:`. 2: wrong command line.

Known flaws of the CLI (avoid them, do not work around them):

- **Always pass `--data`**: without it every binding fails with `The card data has no value "..."`.
- **Unknown options are ignored silently**: `--dat x.json` is the same as no `--data`. Spell `--template`, `--data`, `--out`, `--language` exactly.
- **Do not pass `--mode` or `--scale`**: the template sets them, and that is what PKHeX uses. An invalid `--mode` crashes with `Unhandled exception`.
- **A missing `--template` file crashes** with `Unhandled exception. System.IO.FileNotFoundException`: check your path.
- **`check` is not enough**: it does not open system fonts nor look for the image-set files of the actual keys; `render` does. `validate.ps1` runs both.
- **`render` stops at the first error.**
- **Validation runs in stages**: structural errors (unknown property, wrong value) hide binding and file errors, which appear once the first are fixed. See `examples/05-error-and-fix`.

## Reading errors

Each error is `path (line N): message`. The path addresses the JSON: `elements[3].children[1].rect[2]` is the third number of the `rect` of the second child of the fourth element; `values.font.title` is the `title` font of `values`.

| Message (examples) | Meaning and fix |
|---|---|
| `Unknown property "colour".` | Not a property of this element (see the reference). No synonyms, no extra fields. |
| `Missing required property "font".` | Add it, or give it through a `style`. |
| `Invalid value "centre". Allowed: left, center, right.` | Use one of the listed values. |
| `Invalid color "white". Use "#RGB", …` | Colors are hex only, or a color binding. |
| `Expected a number, found "{level}".` | A fixed value: no binding. Only the numbers of `rect`, `position`, `from`, `to` accept bindings. |
| `Value … is out of range [min, max].` | Use the range in the reference. |
| `Pixel-perfect mode requires an integer scale.` | `canvas.scale` must be 1, 2, 3… |
| `The card data has no value "pokemon.name".` | Not a data key nor a value of the template: look it up in `docs/data-keys.md`. |
| `Value "level" is int, expected color.` | The reference points to a value of another kind; a template value takes the kind of its section of `values`. |
| `Numeric property bindings cannot have a format.` / `Image bindings cannot have a format or a negation.` | Remove `:format` / `!`: formats are only for text. |
| `File not found: fonts\X.ttf` / `Folder not found: …` | Paths are relative to the template folder: the file must be there. |
| `Set "s" has nothing for "psychic" and no fallback.` (render) | No item (or no file, for a pattern) for that key: add it or a `fallback`. |
| `The font "X" is not installed on this computer.` (render) | A `system:` font is missing: ship the font file instead. |
| `Invalid JSON: …` | Syntax error (comma, quote, brace). |

## Rules that matter most

The guide and the reference have everything; these are the points that most often break a template.

- **It must work for every Pokémon.** Every key always exists: empty slots have empty or repeated values (see *Possible values* in `docs/data-keys.md`). Hide the optional parts with `visible`: `{hasType2}`, `{hasMove2}` … `{hasMove4}`, `{hasHeldItem}`, `{hasTera}`, `{isShiny}`, `{gender.isMale}`…
- **No logic in templates**: no arithmetic, no conditions. A choice among variants is one element per variant with its own `visible`, or a set keyed by data (`{set[key]}`: images, or values such as a color per type). Never invent keys: if a value the user wants is not in `docs/data-keys.md` (trainer name, catch date, maximum PP…), say so.
- **Texts change length** with the Pokémon and the language: names, nicknames (up to 12 characters), moves, abilities and items need `"overflow": "ellipsis"` and room.
- **Numbers drawn as lengths** need room for their maximum: `iv.*` 31, `base.*` 255, `ev.*` 252, `level` 100.
- **Sets keyed by data must cover every value of the key** (all 18 types, 19 for `teratype.key`, every species 1–1025…), or have a `fallback`: `validate.ps1` tries only five Pokémon. Build keys from `*.key`, `*.id`, `species`, `form`, never from translated names.
- **Characters**: a font shows only the characters it has; the missing ones are boxes. The pixel fonts here have no accented letters and no `♂ ♀ ★`; Kanit has accents but not those symbols; Japanese needs a font like M PLUS 1p (example 03). Draw symbols with shapes or images. Tell the user what their language will show.
- **pixelPerfect**: whole-number coordinates and sizes, integer `scale`, sprites at their size (`"fit": "none"`) or a whole factor (`"fit": "scale"`), pixel fonts at the sizes of the examples (8 and 16).
- **highResolution**: pixel-art images (`species.sprite`, `ball.sprite`, `heldItem.sprite`) need `"smooth": false`.
- **Files**: relative to the template folder (no `../`), or `https://` (needs the network whenever a card is made). No `file:///` paths, no `system:` fonts unless the user asks: they break on other computers.

Fonts you can ship (copy them with their license file from the examples): Kanit Medium and SemiBold (OFL, `OFL.txt`), Pocket Pixel and Pokemon Pixel Font (CC BY-SA 3.0, `CREDITS.txt`). M PLUS 1p (OFL) is loaded from the web in example 03. Other fonts only if the user provides them.

Web images that work with the data keys (PokeAPI sprites repository; always give such a set a local `fallback`):

| Base URL (put it in a variable) | Key | Content |
|---|---|---|
| `https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home` | `["{pokeapi.path}", "{pokeapi.id}", "{species}"]` | HOME 3D render, 512×512: form, shiny and female variants |
| `https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/official-artwork` | `["{pokeapi.artwork}", "{pokeapi.id}", "{species}"]` | official artwork, 475×475: form and shiny variants |
| `https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/types/generation-ix/scarlet-violet` | `{pokeapi.type1.id}` | type label with icon, 200×40 (1–19, Stellar included) |
| `https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/types/generation-viii/sword-shield` | `{pokeapi.type1.id}` | type label, 200×44 (1–18: no Stellar) |

`pokeapi.path` and `pokeapi.artwork` already choose the form (regional forms, Megas, Vivillon patterns…), the shiny and the female image when PokeAPI has them; never build PokeAPI keys from `species` and `form`. Pokémon images are © Nintendo / Creatures / GAME FREAK / The Pokémon Company (see `CREDITS.md`): use the data images, PokeAPI, files the user gives you, or shapes you draw.

Reusable files in the examples: `examples/04-pixel-perfect/types/<type key>.png` (19 files, `stellar` included; 10×10 white glyphs on transparent, for colored backgrounds) and `examples/04-pixel-perfect/images/gender_*.png` (13×13 colored icons: `male`, `female`, `neutral`).

## Ambiguous requests

Decide alone, and state the decision in your note, when a reasonable default exists:

| Unspecified | Default |
|---|---|
| mode | `highResolution`; `pixelPerfect` if the request says pixel, retro, 8-bit, Game Boy, DS, sprite style |
| size | highResolution: 300×420 portrait or 360×240 landscape, `scale` 2; pixelPerfect: 256×192 or 160×144, `scale` 3 or 4 |
| fonts | Kanit (highResolution), Pocket Pixel / Pokemon Pixel Font (pixelPerfect) |
| colors | from the types (`type1.color`, `type2.color`) on a neutral background |
| picture of the Pokémon | `species.sprite` for pixelPerfect; HOME artwork from PokeAPI with a fallback for highResolution |
| language | none forced: the card follows the user's PKHeX language; static labels in English or neutral |
| a value no key provides | leave it out and say which |

Ask (one short question, with your proposed default) only when the answer changes the structure and no default fits: the request contradicts itself (e.g. pixel-perfect *and* photo artwork), asks for data that does not exist and the card makes no sense without it, or a sketch is unreadable.

From a sketch or picture: measure positions in proportion to the canvas you choose, rebuild the layout with shapes, texts and images, map each piece of information to a data key; what is not in the data becomes static text or is left out (say which).

## Checklist before delivering

- `validate.ps1` prints `VALID`, and you looked at every PNG in `out/`.
- `id` is lowercase with dashes (`my-card`); `name` is readable; every element has a `name`.
- Optional parts have their `visible`; texts that change length have `ellipsis`; bars fit their maximum.
- Image sets keyed by data cover their keys or have a `fallback`.
- The folder contains every file the template uses (fonts with their license file); nothing outside it is referenced except `https://` addresses.
- No comments in `template.json` (PokePic allows them, but the editor drops them and JSON tools reject them).

## Examples

Each folder in `examples/` is a template with `intent.md` (the request it answers). `expected/<template>.<pack>.txt` is the exact CLI output of `check` and `render` with each data pack of `data/`, and `expected/<template>.<pack>.png` the card.

| Folder | Shows |
|---|---|
| `01-minimal-static` | The smallest template: canvas, one font, shapes, line, texts; no bindings. |
| `02-text-and-number-bindings` | Text and number formats, template values, styles, groups, numeric bindings as bar lengths, a text forced to Italian, `visible`, `{{ }}`. |
| `03-color-and-image-bindings` | Gradient from type colors, data images, web image sets (patterns) composed from text values, with fallback, PokeAPI type icons, move chips colored by move type, `"{!isShiny}"`, the name in Japanese with a web font. |
| `04-pixel-perfect` | A 176×112 screen at 4×: pixel fonts, whole-number coordinates, `fit: none`, local type icons, gender icons, IV bars, nature marks. |
| `05-error-and-fix` | Errors found in two rounds and their fixes (`fixes.md`), and an error only `render` finds. |
