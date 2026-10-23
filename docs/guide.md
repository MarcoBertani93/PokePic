# PokePic guide

**English** | [Italiano](guide.it.md)

PokePic makes Pokémon **card PNGs** from a **template**: a design with fixed parts (frames, backgrounds, labels) and parts that change with the Pokémon (name, types, stats, sprite). A Pokémon's data comes from a **data pack**, a JSON file.

This guide explains how to use the editor and how to write a template.

- [Concepts](#concepts)
- [The editor](#the-editor)
- [The template](#the-template)
- [Elements](#elements)
- [Values, bindings and formats](#values-bindings-and-formats)
- [Values](#values)
- [Styles](#styles)
- [Texts](#texts)
- [Images](#images)
- [Languages](#languages)
- [Export](#export)
- [The data pack](#the-data-pack)
- [Common problems](#common-problems)

## Concepts

| Term | What it is |
|---|---|
| **Template** | A folder with `template.json` and its files (fonts, images). It describes the card. |
| **Element** (or layer) | A piece of the card: shape, text, image, line or group. Each element is drawn over the previous ones. |
| **Data pack** | A JSON file with a Pokémon's values (`level`, `species.name`…) and images (`species.sprite`…). |
| **Binding** | A reference to a value of the data pack, written in braces: `{level}`. |
| **Card** | The PNG you get by applying a template to a data pack. |

The template's JSON text is always the source of truth: whatever you do visually is written back to the JSON, and you can always fix it by hand.

## The editor

### Panels

Panels move by dragging their title, can be put in tabs with other panels or detached into a window. Once closed, they reopen from the **View** menu. The layout is remembered; **View → Reset panel layout** goes back to the initial one.

| Panel | What it is for |
|---|---|
| **Layers** | The elements in drawing order: those at the top are behind, those at the bottom in front. |
| **JSON** | The template text. Errors and edits show up in the preview right away. |
| **Properties** | The fields of the selected element, of a style or of the template. |
| **Texts**, **Numbers**, **True/false**, **Colors**, **Images**, **Fonts** | The values of each kind (**View → Values**): on the left the data pack's, read-only (double-click inserts `{key}` in the JSON); on the right the template's, editable. |
| **Styles** | The template's styles. |
| **Problems** | The template's errors, with their line; double-click to go to the line in the JSON. |

### Selecting and moving

- **Click** an element on the canvas: selects it (the topmost one under the mouse).
- **Drag** an element that is already selected: moves it. The first click only selects, so nothing moves by accident.
- **Ctrl+click** (on the canvas or in Layers): adds an element to the selection, or removes it.
- **Click the grey background**: selects the template.
- **Handles** (single element): resize, or move the ends of a line.
- **Arrows**: move by 1 pixel, with Shift by 10.
- Coordinates written as bindings (`"rect": ["{x}", 10, 20, 20]`) do not move with the mouse.

### Context menu and clipboard

Right-click, in Layers or on the canvas, opens the menu of the selected layers: **Cut**, **Copy**, **Paste**, **Duplicate**, **Group**, **Ungroup**, **Delete**.

| Keys | Action |
|---|---|
| Ctrl+C / Ctrl+X / Ctrl+V | Copy, cut, paste (with the focus on Layers or on the canvas) |
| Ctrl+D | Duplicate |
| Ctrl+G / Ctrl+Shift+G | Group / ungroup |
| Del | Delete |
| F2 or double-click | Rename (in Layers) |
| Ctrl+Z / Ctrl+Y | Undo / redo |

Copied elements are JSON text in the clipboard: you can also paste them into the JSON panel, or into another template. They stay in the same place on the canvas even when pasted into a different group.

### Zoom

The bar below the canvas shows the canvas size, the cursor position and the zoom. The button next to the percentage switches between *zoom to window* and *100%*. Shortcuts: Ctrl+wheel, Ctrl++ and Ctrl+−, Ctrl+B (zoom to window), Ctrl+0 (100%).

### Properties

Each field is applied with **Enter** or by clicking elsewhere. An empty field uses the style's value or the default one, shown in grey. The drop-down suggests the possible values, including the data pack's keys and the template's values. With several elements selected only their common properties appear: where values differ you read *{multiple values}*, and what you type applies to all of them.

## The template

```jsonc
{
  "id": "spotlight",                      // identifier, no spaces
  "version": 1,
  "name": "Spotlight",                    // display name (also used in the exported PNG's name)
  "language": "it",                       // optional: forces the language of the whole card
  "canvas": { "width": 192, "height": 128, "mode": "pixelPerfect", "scale": 4 },
  "values": {                             // named values, by kind
    "font":  { "pixel": "fonts/PocketPixelRegular.ttf" },
    "image": { "frame": "images/frame.png", "types": { "pattern": "types/{key}.png" } }
  },
  "styles": { "label": { "type": "text", "font": "{pixel}", "size": 8 } },
  "elements": [ ]
}
```

The JSON allows `//` comments and trailing commas.

A template downloaded from the internet is unzipped keeping its folder: with PKHeX in `plugins\PokePic\templates\<name>\` next to `PKHeX.exe`, where the plugin finds it; with the editor alone wherever you like, opening it with **File → Open template…** and choosing its `template.json`. Do not move `template.json` out of its folder: the paths of its files are relative.

### Canvas

`canvas` sets the size (`width` and `height` in pixels: every coordinate of the template is in these pixels), the `background` and how the card is drawn:

- **`highResolution`** (default): smoothed edges, sharp texts and images at any `scale`. For cards with artwork. Pixel-art images, such as game sprites, look blurred when enlarged: give them `"smooth": false`.
- **`pixelPerfect`**: drawn at the canvas size without smoothing, then enlarged by `scale` (a whole number) keeping the pixels sharp. For retro cards: design small (e.g. 256×192) with whole-number coordinates and sizes, show sprites at their size (`"fit": "none"`) or enlarged by a whole factor (`"fit": "scale"`), and use pixel fonts at the sizes they are drawn for (the sample ones at 8 and 16). Gradients show bands and ellipses have jagged edges.

`scale` multiplies the PNG: a 192×128 canvas with scale 4 gives a 768×512 PNG. Every property of the canvas: [template reference](template-reference.md#canvas).

## Elements

Properties of every element: `type`, `name` (the name in Layers), `style`, `visible` (`true`, `false` or a binding), `opacity` (0 to 1).

| `type` | What it draws |
|---|---|
| `group` | nothing by itself: it moves, shows and hides its `children` together; their coordinates are relative to its `position` |
| `shape` | a rectangle (also with rounded corners) or an ellipse, filled and/or with a border |
| `line` | a line from `from` to `to` |
| `image` | an image, see [Images](#images) |
| `text` | a text, see [Texts](#texts) |

The numbers of `rect`, `position`, `from` and `to` can be bindings (`"{level}"`); the other numbers (`size`, `radius`, `opacity`…) are fixed. Every property of every element, with its values and default, is in the [template reference](template-reference.md).

**Fill** (`fill`): a color, or a linear gradient:

```json
"fill": { "linear": { "from": [0, 0], "to": [192, 0], "stops": [[0, "{type1.color}"], [1, "{type2.color}"]] } }
```

**Colors**: `"#RGB"`, `"#RGBA"`, `"#RRGGBB"`, `"#RRGGBBAA"` (the last pair is the transparency: `00` invisible, `FF` opaque), or a binding to a color (`"{type1.color}"`).

## Values, bindings and formats

A binding takes a value from the data pack (or from a [value of the template](#values)):

| Syntax | Result |
|---|---|
| `"{level}"` | the value of `level` |
| `"Lv. {level}"` | a text with values in it |
| `"{!hasType2}"` | the opposite of a true/false value |
| `"{level:D3}"` | the value with a format (after the colon) |
| `"{typeColor[move1.type.key]}"` | the item of a [set](#sets) picked by the value of `move1.type.key` |
| `"{{"` and `"}}"` | braces as characters |

### Text formats

| Format | Example | Result |
|---|---|---|
| (none) | `{species.name}` | `Charizard` |
| `upper` | `{species.name:upper}` | `CHARIZARD` |
| `lower` | `{species.name:lower}` | `charizard` |

A localized text uses the element's language (see [Languages](#languages)).

### Number formats

Formats are .NET's, with **English** conventions (dot for decimals, comma for thousands), the same on every computer. `D` and `X` work only on whole numbers.

| Format | Value | Result | Notes |
|---|---|---|---|
| (none) | 42 | `42` | |
| `D3` | 6 | `006` | whole numbers: at least 3 digits, with leading zeros |
| `D4` | 97 | `0097` | handy for the Pokédex number |
| `N0` | 1234 | `1,234` | thousands separator |
| `N2` | 1234.5 | `1,234.50` | thousands and 2 decimals |
| `F1` | 75.65 | `75.7` | 1 decimal, rounded |
| `F0` | 75.6 | `76` | no decimals |
| `P0` | 0.85 | `85 %` | percentage (multiplies by 100, with a space) |
| `0%` | 0.85 | `85%` | percentage without the space |
| `X2` | 255 | `FF` | hexadecimal (whole numbers) |
| `0.0` | 7 | `7.0` | custom format: `0` is a digit that is always shown |
| `#,##0` | 1234 | `1,234` | `#` is a digit shown only when needed |
| `+0;-0;0` | 5 | `+5` | sign for positive numbers too (positive;negative;zero) |
| `00 kg` | 7 | `07 kg` | fixed text can be written in the format |

An invalid format, or a numeric format on a text, is an error in the Problems panel.

### True/false values

`visible` takes `true`, `false`, `"{hasType2}"` or `"{!hasType2}"`. More complex conditions, and sums, are not written in the template: whoever produces the data pack prepares them (`hasType2`, `hasMove4`, `isShiny`, `stat.total`…).

## Values

The template's values give names to texts, numbers, colors, images and fonts, and are used like the keys of the data pack: `"{title}"`, `"{frame}"`. They are grouped by kind, like in the data pack:

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

Names are unique across the kinds and cannot be a key of the data pack. What a value can contain depends on its kind:

| Kind | Written as |
|---|---|
| `color`, `int`, `float`, `bool` | the value (`"#30303080"`, `6`, `1.5`, `true`) or a reference to a value of the same kind (`"{type1.color}"`, `"{level}"`, `"{!hasType2}"`); a whole number goes where a decimal goes |
| `text` | a text with references and formats: `"{species.name} Lv. {level}"`, `"#{species:D4}"` |
| `image`, `font` | a path, also composed: `"images/frame.png"`, `"{BASE}/{species}.png"`; or a reference to an image or font (`"{species.sprite}"`). A font can also be `"system:Arial"`. |

`{language}` can always be used: it is the language of the element being drawn (`"badges/{language}/fire.png"`).

Every kind can be written in four forms: single, by language, set, pattern.

### Values by language

`{ "en": …, "it": … }` gives a value per language, like the translated texts of the data pack: `"en"` is required, a missing language uses the base language, then English (see [Languages](#languages)). It works for every kind: a label, a color, an image, a font for Japanese.

### Sets

A set picks an item with a key that comes from the data: `{set[key]}` takes the value of `key` (e.g. `move1.type.key` = `fire`) and looks it up in the set.

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

- **`items`** lists a value per key; each can be by language.
- **`pattern`** (texts, images and fonts only) builds the item from the key, `{key}` in the pattern: files are not listed, so a set can hold thousands of artworks.
- **`fallback`** is used for a key the set does not have (for a pattern of files, whose file does not exist). Without it, such a key is an error when the card is made.
- `"{set[key]}"` takes one key; `{ "set": …, "key": … }` also takes a formatted key (`"{species:D4}"`) or candidates tried in order. Both can be used wherever a value of the set's kind goes: colors, numbers, `visible`, texts with formats, images, other values (`"accent": "{typeColor[type1.key]}"`).
- A key contains letters, digits, `-` and `_`, in parts separated by `/` (`shiny/female/3`); a number is a key too (`{dexColor[species]}` with `"6": …`).

### In the editor

Each kind has its panel. On the left are the data pack's values in the preview language; on the right the template's, each as a table: a row for a single value, or a row per item (plus the pattern and the fallback), and a column per language.
- **Form** switches between single, set and pattern.
- **+ Language** adds a column (the JSON changes when you fill a cell); ✕ on a column removes that language from the value.
- An empty cell shows in grey the value used instead. Under each row is what it gives with the current data: the text, the color, the image, a sample of the font.
- **{ }** inserts the value's reference in the JSON. The `variables` sample template shows the forms.

## Styles

A style gathers properties shared by several elements. Those written in the element win.

```json
"styles": { "label": { "type": "text", "font": "{pixel}", "size": 8, "color": "#000000" } }
```

- With `"type"` the style is for one type of element and can set all its properties; the properties of an element only suggest the styles that fit it.
- Without `"type"` it can only set properties the element has (e.g. `opacity`): if a style with `font` is given to an image, the Problems panel says so.
- A style contains neither the position nor the content: `rect`, `text` and `image` belong to each element.

Styles are managed in the **Styles** panel: **+** creates one, the menu chooses its type, **✎** opens it in the Properties panel to edit its values. Renaming a style updates the elements that use it.

## Texts

A text is laid out in its `rect` according to `align` and `valign`, and does not wrap by itself: `\n` breaks the line. A text longer than its rectangle is drawn past it, unless `overflow` is `crop` (cut at the edge) or `ellipsis` (each line too long is shortened and ends with `ellipsis`, `"."` by default). Use `ellipsis` for names, nicknames and moves: their length changes with the Pokémon and the language. Every property of texts: [template reference](template-reference.md#text).

A system font makes the template depend on the computer: if it is missing, the card is not made. For templates to share, put the font file in the folder.

A font has only the characters its author drew; the missing ones show as boxes. The pixel fonts of the sample templates (Pocket Pixel, Pokemon Pixel Font) have no accented letters (é, ö, ü…) and no symbols such as ♂ ♀ ★; Kanit has the accented letters but not those symbols; Japanese, Chinese and Korean need a font that contains them, such as Noto Sans JP. Draw symbols with shapes or images rather than as text.

## Images

`image` can be:

| Value | Where it comes from |
|---|---|
| `"{frame}"` | a value of `values.image` |
| `"{species.sprite}"` | an image of the data pack |
| `"{artwork[species]}"`, `{ "set": "artwork", "key": "{species}" }` | an item of a set (see [Sets](#sets)) |
| `"images/frame.png"`, `"badges/{type1.key}.png"` | a file, also composed with references |

`fit` decides the size (`none` original, `contain` whole inside the rectangle, `cover` filling it, `stretch` distorted, `scale` multiplied by `scale`), `anchor` the position inside the rectangle, `overflow: "crop"` cuts what goes outside it, `flip` mirrors it. Every property of images: [template reference](template-reference.md#image).

### Where files can be

Images and fonts are paths:

| Written as | Meaning |
|---|---|
| `images/frame.png` | relative to the template folder (it cannot leave it) |
| `file:///C:/Cards/frame.png` | a file in any folder |
| `https://…/frame.png` | a file on the web, downloaded once per session |

Paths written without references are checked when the template opens; those that depend on the data (`"{BASE}/{species}.png"`, the items of a pattern) are read when the card is made, once. A path with `{language}` tries the language, its base language and English, and takes the first file that exists: for a card in Italian `badges/{language}/fire.png` tries `badges/it/fire.png`, then `badges/en/fire.png`. The `artwork` template uses the type badges this way.

The images of [PokeAPI](https://github.com/PokeAPI/sprites) have keys ready in the data: `pokeapi.path` for the HOME renders and `pokeapi.artwork` for the official artwork already contain the form, the shiny and (HOME only) the female variant, such as `10034` (Mega Charizard X) or `shiny/female/3`:

```json
"values": {
  "text": { "HOME": "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home" },
  "image": { "home": { "pattern": "{HOME}/{key}.png", "fallback": "images/unknown.png" } }
},
"elements": [ { "type": "image", "rect": [0, 0, 200, 200], "image": { "set": "home", "key": ["{pokeapi.path}", "{pokeapi.id}", "{species}"] }, "fit": "contain" } ]
```

A file that is not on the server (404) counts as missing; other download errors go to the Problems panel, and **Refresh** (F5) tries again.

## Languages

A data text can be the same in every language (`"nickname": "Drago"`) or translated (`"species.name": { "en": "Charizard", "de": "Glurak" }`; English is required). The language used is, in order:

1. the element's **Force language** (`language` of the text or image);
2. in the editor, the **Preview language** when it is not *auto*;
3. the template's **Force language**;
4. the data pack's language, i.e. the language of whoever uses the card;
5. English.

When a translation is missing, the base language is tried (`pt-BR` → `pt`), then English. Usually nothing is forced: the language comes from the data pack.

In the top bar, **Preview language** lets you try the card in another language: it wins over the template's language too, without changing the template or the data pack. With *auto* the rules above apply. Exporting from the editor uses the same language as the preview.

## Export

**Export PNG** (Ctrl+E) saves the card with the canvas's mode and scale. The suggested name is `{template name}_{Pokémon name}.png`, e.g. `Spotlight_Drago.png`: the Pokémon's name is its nickname, or its species in the data pack's language.

## The data pack

```jsonc
{
  "formatVersion": 1,
  "description": "Charizard Lv. 50",
  "name": "Drago",                      // name for files: nickname, or the species
  "language": "it",                     // language of whoever uses the card
  "values": {                           // the same kinds as the template's values (no fonts)
    "text":  { "nickname": "Drago", "species.name": { "en": "Charizard", "it": "Charizard" } },
    "int":   { "level": 50, "species": 6 },
    "float": { },
    "bool":  { "hasType2": true },
    "color": { "type1.color": "#EE8130" },
    "image": { "species.sprite": "sprites/charizard.png" }
  }
}
```

Every value can be by language (`{ "en": …, "it": … }`). Images are files relative to the pack, or PNGs written in Base64: `{ "png": "iVBORw0…" }`.

The value panels show all the keys, kind by kind; double-click a key to insert it into the JSON. The keys of a Pokémon exported by the plugin, with their meaning and possible values, are in [data keys](data-keys.md); the sample packs in `resources/data` have the same keys.

## Common problems

| Message | What to do |
|---|---|
| *The card data has no value "x"* | The key does not exist in the data pack: check its name in the value panels. |
| *Value "x" is text, expected color* | The binding points to a value of the wrong kind. |
| *format "D3" is only valid for numbers* | Texts accept only `upper` and `lower`. |
| *Style "label" is for text elements* | The style is for another type of element: choose one that fits. |
| *File not found* | The path is relative to the template folder: check upper/lower case and the extension. |
| *Download of … failed* | Network problem or wrong address; F5 tries again. |
| *The font "X" is not installed* | The template uses a system font that is missing: install it, or put the file in the template. |
