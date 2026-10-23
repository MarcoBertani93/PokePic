# PokePic

**English** | [Italiano](README.it.md)

Pokémon card PNGs generated from JSON templates, with a standalone editor (Avalonia). A Pokémon's data comes from a JSON *data pack*, which the PKHeX plugin produces for the selected Pokémon (and anyone can write).

The **[user guide](docs/guide.md)** explains the editor and the template format for card authors; the **[template reference](docs/template-reference.md)** lists every property and **[data keys](docs/data-keys.md)** the data of a Pokémon. This README covers building, running and the PKHeX plugin.

## Quick start

```
dotnet build
dotnet test
dotnet run --project src/PokePic.Editor -- --lang en
```

The editor opens with the first sample template and data pack. The **Samples** menu loads the other templates and data packs.

### Starting the editor

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). From the repository folder, `dotnet run --project src/PokePic.Editor` builds and starts the editor. After `dotnet build` you can also start the executable directly: `src/PokePic.Editor/bin/Debug/net10.0/PokePic.Editor.exe` on Windows (`PokePic.Editor` on Linux and macOS). Options: `--lang it|en` (interface language), `--template <template.json>`, `--data <datapack.json>`.

### Templates downloaded from the internet

A template is a **folder**: `template.json` together with its files (fonts, images), which it refers to with relative paths. To use a downloaded template:

1. unzip it keeping its folder as it is, wherever you like (e.g. `Documents\PokePic\Templates\<template name>\`);
2. in the editor, **File → Open template…** and choose its `template.json` (or start the editor with `--template <path>\template.json`).

Do not move `template.json` out of its folder, or the relative paths break. Files the template takes from the web (`http://`, `https://`) are downloaded when the template opens: a template can only contain images, fonts and texts, no code. The **Samples** menu lists only the templates that come with the editor.

### PKHeX plugin

Requires [PKHeX](https://projectpokemon.org/home/files/file/1-pkhex/) 26.10.18 (the plugin is built against its PKHeX.Core: other versions may not load it).

1. Build the package: `pwsh build/package-plugin.ps1` creates `artifacts/PokePic-PKHeX/plugins` (and a zip of it).
2. Copy that `plugins` folder next to `PKHeX.exe` (merge it with an existing one). If you downloaded the files, unblock them (file properties → *Unblock*).
3. Start PKHeX: **Tools → PokePic** has *Export card…*, *Export box as cards…* and *Import card…*. A card dropped onto PKHeX is imported too, with the same messages when it cannot be (changed file, Pokémon not convertible to the open save).

```
PKHeX.exe
plugins/
  PokePic.Plugin.dll        the plugin: a single DLL with all its dependencies inside
  PokePic/
    PokePic.Editor.exe      the editor, as a single file
    templates/
      pixelcard/            one folder per template (template.json and its files)
      lightspot/
      artwork/
```

**Templates go in `plugins/PokePic/templates`**, one folder each: a template downloaded from the internet is unzipped there, keeping its folder. The export window lists them with the preview of the chosen one for the Pokémon being edited; *Edit template* opens it in the editor with that Pokémon's data, *New template…* creates a folder with a minimal template and opens it. After saving in the editor, the preview updates when you go back to the window.

The plugin passes the data to the editor on the command line as a file (`--data`, a data pack in the temporary folder): the data contains the sprites, too large for the command line itself. The data pack's `name` is the name PKHeX gives the Pokémon's file (`0025 - Pikachu - AFC21B886487`), so a card is saved as `Spotlight_0025 - Pikachu - AFC21B886487.png`. The exported PNG contains the Pokémon in its `pokepic` chunk, so *Import card…* gives it back byte for byte.

PKHeX loads every `.dll` it finds under `plugins` (subfolders included): that is why the plugin is a single DLL, the editor a single `.exe`, and nothing else with a `.dll` should be put in `plugins/PokePic`. The plugin's native library (SkiaSharp) is extracted on first use to `%LOCALAPPDATA%\PokePic\native`.

The panels (Layers, JSON, Properties, Problems, the value panels, Styles) dock like in Visual Studio: drag them to move them, put them in tabs with other panels or detach them into a window; close them with ✕ and reopen them from the **View** menu, which puts them back where they were. The layout is saved on exit (`%APPDATA%\PokePic\editor-layout.json`); **View → Reset panel layout** goes back to the default one.

- **Layers**: tree in drawing order (layers at the top are behind; a new layer goes at the end of the list, in front). Toolbar to add (＋: group, shape, text, image, line), duplicate, move up/down; right-click (also on the canvas) opens the menu with cut, copy, paste, duplicate, group, ungroup, delete. Ctrl+C/X/V copy the elements as JSON with canvas coordinates, so they stay in place even when pasted into another group. Drag a layer to reorder it or to put it into a group (middle band of the group's row): it keeps its position on the canvas. Ctrl+click selects several layers (to group, move or edit them together). F2 or double-click renames, Del deletes (Ctrl+Z undoes); the check box toggles visibility (disabled when visibility is bound to data). Selecting a layer highlights its text in the JSON.
- **JSON**: the template text. It is the source of truth: every visual edit rewrites it (comments are lost) and can be undone with Ctrl+Z.
- **Properties**: the fields of the selected element, or of the template when nothing is selected (name, language, canvas). With several elements selected, the properties they have in common: a value shared by all is shown, different values appear as *{multiple values}*; what you type applies to all (in rectangles only the edited parts). Names are read-only. Every field takes a value or a `{key}` binding; the drop-down suggests compatible bindings. An empty field uses the style or the default, shown in grey. A value is applied with Enter or by clicking elsewhere.

On the canvas: the first click selects the topmost layer under the mouse, even in front of the selection; dragging an already selected element moves the whole selection. Ctrl+click adds or removes an element; a click on the grey background selects the template. Handles (single element) resize or move the ends of a line; arrows move by 1 px (Shift: 10 px), Del deletes. A group selected in the tree moves by dragging inside its box. Coordinates bound to data do not move with the mouse.

- **Problems**: errors with their line; double-click opens the JSON at that line.
- **Data pack** (top bar): the pack in use (**Samples** menu and **Open data pack…**) and its language.
- **Preview language** (top bar): normally the data pack's language; another language lets you try the graphics (see *Languages*).
- **Texts, Numbers, True/false, Colors, Images, Fonts**: a panel per kind of value. On the left the data pack's values (read-only; double-click inserts `{key}` into the JSON), on the right the template's, each as a table of keys × languages (single, set or pattern), with what each row gives with the current data: text, color, thumbnail, font sample. "+ file…" copies a font or an image into the template folder.
- **Styles**: the template's styles (✎ opens a style in the Properties panel).

Below the canvas, like in Paint.NET: canvas size, cursor position, zoom (menu with presets), *zoom to window ⇄ 100%* button, zoom slider with − and +. Zoom can be any percentage: in pixel-perfect mode the preview is enlarged without smoothing (at fractional zooms pixels have slightly different widths, as in any image editor) and reduced smoothly below 100%; in high resolution it is redrawn at the zoom scale. Ctrl+wheel, Ctrl++ and Ctrl+− change zoom, Ctrl+B zooms to window, Ctrl+0 goes back to 100%. Zoom only affects the preview.
- **Export PNG** (Ctrl+E): with the template's mode (`canvas.mode`, pixel-perfect or high resolution) and scale (`canvas.scale`), which you change in the canvas properties. The preview uses the template's mode too. The suggested name is `{template name}_{Pokémon name}.png` (the data pack's `"name"`; when missing, the nickname or the species in the pack's language).

If you edit a template image or font with another program, or the data pack changes on disk, the preview refreshes.

From the command line:

```
PokePic.Editor --template <template.json> --data <datapack.json> [--lang it|en]
pokepic render --template <template.json> --data <datapack.json> [--mode pixelPerfect|highResolution] [--scale N] --out card.png
pokepic check  --template <template.json> --data <datapack.json>
pokepic format --template <template.json>     # rewrites the file in the editor's format
```

## Templates and data packs

A template is a folder with `template.json` and its files (fonts, images); a data pack is a JSON file with a Pokémon's values and images. How to write them:

- [User guide](docs/guide.md): the editor, the template format, bindings and formats, values and sets, styles, images, languages, data packs.
- [Template reference](docs/template-reference.md): every property, with its values, limits and default.
- [Data keys](docs/data-keys.md): the values and images of a Pokémon available to templates, with their possible values.

The sample templates are in `resources/templates` (the ones that ship with the editor and the plugin), the sample data packs in `resources/data`, the credits of fonts and images in `resources/CREDITS.md`. The reference and the data keys are generated from the code, which they are checked against: after changing properties, values or data keys, run `dotnet run build/generate-reference.cs -- docs`.

## Templates made by an AI agent

An AI coding agent (one that can read and write files and run PowerShell) can turn a description or a sketch of a card into a template and check it with PokePic itself. `pwsh build/package-agent.ps1` builds the package to give it, in `artifacts/PokePic-AgentPackage` (and a zip): the agent's instructions (`AGENTS.md`), the guide and the references, example templates with their expected output for every sample data pack, the data packs and the PokePic CLI as a single Windows program (no .NET needed). Its sources are in [`agent-package/`](agent-package/README.md).

## Disclaimer

PokePic is an unofficial, non-commercial fan project. It is **not affiliated with, endorsed, sponsored or approved by** Nintendo, Game Freak, Creatures Inc. or The Pokémon Company.

Pokémon and Pokémon character names are trademarks of Nintendo, Creatures Inc., Game Freak and The Pokémon Company. All images of Pokémon, items, types and badges used in the samples, in the repository or downloaded by the sample templates (sprites, artwork, type icons and badges) are © Nintendo / Creatures Inc. / GAME FREAK Inc. / The Pokémon Company. **They do not belong to the author of PokePic**: they are included or referenced for illustrative purposes only, to show what the program does, and no ownership is claimed.

Whatever license covers the source code of PokePic does not cover these images. If you are a rights holder and want something removed, please open an issue.
