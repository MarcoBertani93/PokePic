# From the errors to the fix

The CLI validates in stages: errors in the template's structure (round 1) hide the errors in references and files (round 2). Fix, run again, until `check` succeeds; then `render`, which finds what `check` cannot.

## Round 1: `template.json` → `expected/template.<pack>.txt`

| Error | Fix in `template.round2.json` |
|---|---|
| `canvas.scale`: Pixel-perfect mode requires an integer scale. | `"scale": 2.5` → `"scale": 3` |
| `elements[1].colour`: Unknown property "colour". | `"colour"` → `"color"` (the reference lists every property) |
| `elements[2].color`: Invalid color "white". | `"white"` → `"#FFFFFF"` (no color names) |
| `elements[2].align`: Invalid value "centre". Allowed: left, center, right. | `"centre"` → `"center"` |
| `elements[4].opacity`: Expected a number, found "{level}". | removed: `opacity` is a literal number 0–1, never a binding |
| `elements[5]`: Missing required property "font". | added `"font": "{pixel}"` (a font of `values.font`) |

## Round 2: `template.round2.json` → `expected/template.round2.<pack>.txt`

| Error | Fix in `template.fixed.json` |
|---|---|
| `values.font.title`: File not found: fonts\PokemonSolid.ttf | use a font that exists in the template folder: `fonts/pokemon_pixel_font.ttf` |
| `elements[0].fill`: The card data has no value "type1.colour". | `{type1.colour}` → `{type1.color}` (keys are in `docs/data-keys.md`) |
| `elements[1].text`: The card data has no value "pokemon.name". | `{pokemon.name}` → `{species.name}` |
| `elements[3].image`: "{species.sprite:upper}": a reference to an image cannot have a format. | `"{species.sprite:upper}"` → `"{species.sprite}"` (formats are only for texts) |
| `elements[4].rect[2]`: Numeric property bindings cannot have a format. | `"{iv.hp:D2}"` → `"{iv.hp}"` (formats are only for text) |
| `elements[5].visible`: The card data has no value "shiny". | `{shiny}` → `{isShiny}` |

`template.fixed.json`: `check` and `render` succeed with every data pack (`expected/template.fixed.<pack>.txt` and `.png`).

## Valid for `check`, invalid for `render`: `template.render-error.json`

It adds a type icon from the set `typeIcon` (`"pattern": "types/{key}.png"`), but `types/` has only `fire`, `grass` and `water`. `check` succeeds because the folder exists; `render` fails with Hypno (`psychic`) and succeeds with the Charizard packs (`fire`):

```
Render errors:
  elements[4].image: Set "typeIcon" has nothing for "psychic" and no fallback.
```

Fix: one file per value of the key (the 18 types are listed in `docs/data-keys.md`, *Possible values*), or a `"fallback"` in the set. This is why a template must be rendered with every data pack in `data/`, and why sets keyed by data must cover the whole domain.
