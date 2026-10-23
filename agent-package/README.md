# PokePic agent package

Lets an AI coding agent turn a description, sketch or draft of a Pokémon card into a PokePic template, and check it with PokePic itself. Nothing needs to be installed: the PokePic command-line tool is included as a single Windows program.

## Using it

1. Unzip the package and start your agent in its folder. The agent must be able to read and write files and run PowerShell on Windows. Agents that read `AGENTS.md` (Claude Code, Codex and others) find their instructions there by themselves; tell other agents first: *Read AGENTS.md and follow it.*
2. Ask for the card, for example: *Make a template for a pixel-art card with the Pokémon's sprite, name, level and its two types.*
3. The agent writes the template in `work/<name>/`, checks it with `validate.ps1` until it reports `VALID`, and gives you the folder with a short note on its choices.
4. Copy that folder into `plugins/PokePic/templates/` next to PKHeX, or open its `template.json` in the PokePic editor.

`docs/guide.md` explains how templates work, if you want to change one yourself. `CREDITS.md` lists the licenses of the fonts and the disclaimer on Pokémon images.
