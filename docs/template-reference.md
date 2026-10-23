# Template reference

**English** | [Italiano](template-reference.it.md)

Every property of `template.json`, with its values, limits and default. How a template works is explained in the [guide](guide.md); the data a template can use is in [data keys](data-keys.md).

*Generated from the PokePic code by `build/generate-reference.cs`, which checks every rule below against PokePic's parser: do not edit by hand.*

An unknown property is an error. A property set to `null` counts as absent. Values in a list (`left`, `center`…) are case-insensitive. A *fixed value* cannot be a binding.

## Template (`template.json`)

| Property | Required | Values | Default | Notes |
|---|---|---|---|---|
| `$schema` |  | text |  | Ignored by PokePic (useful for editors). |
| `id` | yes | text |  | Identifier, without spaces; stored in exported cards. |
| `version` |  | integer 1–…, fixed value | `1` | The template's own version. |
| `name` |  | text |  | Display name; also the first part of exported file names. |
| `language` |  | language code (`en`, `it`, `ja`, `zh-Hans`…) |  | Forces the language of the whole card. |
| `canvas` | yes | object, see `canvas` |  |  |
| `values` |  | object |  | Named values by kind (`text`, `int`, `float`, `bool`, `color`, `image`, `font`): see *Values*. |
| `styles` |  | object |  | See *Styles*. |
| `elements` | yes | array of elements |  | Layers, drawn in order: each over the previous ones. |

## `canvas`

| Property | Required | Values | Default | Notes |
|---|---|---|---|---|
| `width` | yes | integer 1–4096, fixed value |  | Template width in pixels: every coordinate is in these pixels. |
| `height` | yes | integer 1–4096, fixed value |  | Template height in pixels. |
| `mode` |  | `highResolution` \| `pixelPerfect` | `highResolution` | `pixelPerfect`: sharp pixels, no smoothed edges (pixel art). `highResolution`: smoothed edges, sharp texts and images at any scale. |
| `scale` |  | number 0.1–16, fixed value | `1` | Multiplier of the PNG: a 192×128 canvas with scale 4 gives a 768×512 PNG. A whole number in pixelPerfect. |
| `background` |  | color or gradient |  | See *Fill*. Absent: transparent. |

## Properties of every element

| Property | Required | Values | Default | Notes |
|---|---|---|---|---|
| `type` | yes | `group` \| `image` \| `shape` \| `line` \| `text` |  | Element type. |
| `name` |  | text |  | Name shown in the editor's Layers panel. |
| `style` |  | text |  | Name of a style of `styles`: the element's own properties win. |
| `visible` |  | `true` \| `false` \| `"{bool}"` \| `"{!bool}"` | `true` | `"{!key}"` is the opposite of the value. |
| `opacity` |  | number 0–1, fixed value | `1` | 0 invisible, 1 opaque. |

## `group`

| Property | Required | Values | Default | Notes |
|---|---|---|---|---|
| `position` |  | `[x, y]`: numbers or numeric bindings | `[0, 0]` | Offset of the children: their coordinates (gradients included) are relative to it. |
| `children` |  | array of elements | `[]` | Elements of the group, drawn in order. |

## `image`

| Property | Required | Values | Default | Notes |
|---|---|---|---|---|
| `rect` | yes | `[x, y, width, height]`: numbers or numeric bindings |  | The rectangle the image is placed in. |
| `image` | yes | `"{name}"`, a path, or `{ "set", "key" }` |  | Which image: see *Images and fonts*. |
| `fit` |  | `none` \| `stretch` \| `contain` \| `cover` \| `scale` | `none` | `none` original size; `stretch` distorted to fill the rectangle; `contain` whole, aspect ratio kept; `cover` fills the rectangle, cropped; `scale` original size times `scale`. |
| `scale` |  | number 0.01–100, fixed value | `1` | Only with `fit: scale` (an error otherwise): `2` double, `0.5` half. |
| `anchor` |  | `topLeft` \| `top` \| `topRight` \| `left` \| `center` \| `right` \| `bottomLeft` \| `bottom` \| `bottomRight` | `center` | Where the image sits inside the rectangle. |
| `flip` |  | `none` \| `horizontal` \| `vertical` \| `both` | `none` | Mirrors the image. |
| `smooth` |  | `true` \| `false`, fixed value |  | Absent: smoothed in highResolution. `false`: enlarges pixel art without smoothing. In pixelPerfect images are never smoothed. |
| `overflow` |  | `none` \| `crop` | `none` | `none`: drawn whole, also outside the rectangle; `crop`: only the part inside it. With `fit: cover` the image is always cropped. |
| `language` |  | language code (`en`, `it`, `ja`, `zh-Hans`…) |  | Forces the language of a localized image set (`{language}` in its pattern). |

## `shape`

| Property | Required | Values | Default | Notes |
|---|---|---|---|---|
| `rect` | yes | `[x, y, width, height]`: numbers or numeric bindings |  | The rectangle of the shape. |
| `shape` |  | `rect` \| `ellipse` | `rect` | `ellipse` is the ellipse inscribed in the rectangle. |
| `radius` |  | number 0–…, fixed value | `0` | Rounded corners (rectangles only). |
| `fill` |  | color or gradient |  | See *Fill*. A shape needs `fill`, `stroke` or both. |
| `stroke` |  | color, or `{ "color", "width" }` |  | The border, drawn inside the rectangle: see *Stroke*. |

## `line`

| Property | Required | Values | Default | Notes |
|---|---|---|---|---|
| `from` | yes | `[x, y]`: numbers or numeric bindings |  | Start of the line. |
| `to` | yes | `[x, y]`: numbers or numeric bindings |  | End of the line. |
| `color` | yes | color or binding to a color |  |  |
| `width` |  | number 0.01–…, fixed value | `1` | Thickness in pixels. With an odd width the coordinates are pixels, both ends included. |

## `text`

| Property | Required | Values | Default | Notes |
|---|---|---|---|---|
| `rect` | yes | `[x, y, width, height]`: numbers or numeric bindings |  | The rectangle the text is laid out in. Texts do not wrap: lines break only at `\n`. |
| `text` | yes | text with bindings |  | The text, with bindings; `\n` breaks the line. |
| `font` | yes | text |  | A font: `"{name}"` of `values.font`, a file path, or `"system:Family name"` for an installed font; see *Images and fonts*. |
| `size` | yes | number 0.5–1000, fixed value |  | Size in pixels. |
| `color` |  | color or binding to a color | `#000000` |  |
| `align` |  | `left` \| `center` \| `right` | `left` |  |
| `valign` |  | `top` \| `middle` \| `bottom` | `top` |  |
| `lineHeight` |  | number 0.5–1000, fixed value |  | Distance between lines, in pixels. Absent: from the font. |
| `language` |  | language code (`en`, `it`, `ja`, `zh-Hans`…) |  | Forces the language of the translated values in this text. |
| `overflow` |  | `none` \| `crop` \| `ellipsis` | `none` | `none`: drawn whole, also outside the rectangle; `crop`: only the part inside it; `ellipsis`: each line that is too long is shortened and ends with `ellipsis`. |
| `ellipsis` |  | text | `.` | Added to shortened lines, e.g. `...` or `…`. |

## Values

- **Color**: `"#RGB"`, `"#RGBA"`, `"#RRGGBB"`, `"#RRGGBBAA"` (the last pair is the transparency: `00` invisible, `FF` opaque), or a binding to a color (`"{type1.color}"`, without format or `!`). Color names are not accepted.
- **Fill**: a color, or a linear gradient `{ "linear": { "from": [x, y], "to": [x, y], "stops": [[0, color], [1, color]] } }`: at least two stops, offsets from 0 to 1 in increasing order. Gradient coordinates are those of the canvas (of the group, inside a group).
- **Stroke**: a color (width 1), or `{ "color": color, "width": number ≥ 0 }`. It is drawn inside the rectangle.
- **Values** (`values.<kind>.<name>`, kinds `text`, `int`, `float`, `bool`, `color`, `image`, `font`): used like data keys, `"{name}"`; names may contain letters, digits, `.`, `_` and `-`, and are unique across the kinds. Every value is written in one of four forms: single (`"#F08030"`, `6`, `"fonts/a.ttf"`); by language (`{ "en": "Lv.", "it": "Liv." }`, `en` required, chosen like the data's); a set (`{ "items": { "fire": …, "water": … }, "fallback": … }`, each item single or by language); a pattern, for texts, images and fonts only (`{ "pattern": "artwork/{key}.png", "fallback": … }`). Colors, numbers and true/false take a literal or a reference of their kind (`"{type1.color}"`, `"{!hasType2}"`, `"{typeColor[type1.key]}"`; a whole number fits a decimal); texts, images and fonts are composed of texts and numbers (`"{species.name} Lv. {level}"`, `"{BASE}/{species:D4}.png"`), or are a lone reference of their kind (`"{frame}"`, `"{species.sprite}"`). `{language}` is always the language of the element. A cycle, an unknown key, a wrong kind or a name equal to a data key is an error.
- **Sets**: `"{set[key]}"` takes the value of `key` and picks that item (`"{typeColor[move1.type.key]}"`); `{ "set": "artwork", "key": ["{species}-{form}", "{species}"] }` also takes a formatted key or candidates tried in order. The first key the set has wins (for a pattern of images or fonts, the first whose file exists), then the `fallback`; without it, a key with nothing is an error when the card is made. A key contains letters, digits, `-` and `_`, in parts separated by `/` (`shiny/female/3`): it cannot leave the folder of a pattern. Files of a pattern are not listed (a set can hold thousands of artworks).
- **Images and fonts**: `"{frame}"` = a value of `values.image` or an image of the data pack (`"{species.sprite}"`); `"{artwork[pokeapi.id]}"` or `{ "set": …, "key": … }` = the item of a set; any other text is a file path, also composed (`"badges/{type1.key}.png"`). A font is `"{name}"` of `values.font`, a path, or `"system:Family"` (an installed font: the card then depends on the computer).
- **File paths**: relative to the template folder (they cannot leave it), `file:///C:/folder/file.png`, or `https://…` (downloaded once per session). A path with `{language}` tries the language, its base language and English, and takes the first file that exists. Fixed paths are checked when the template opens; paths that depend on the data when the card is made.
- **Styles** (`styles.<name>`): properties shared by elements, with an optional `"type"`. With `"type"` a style can set every property of that type of element except `name`, `style` and `children`; without it, only properties the element using it has. The element's own properties win; required properties can come from the style.
