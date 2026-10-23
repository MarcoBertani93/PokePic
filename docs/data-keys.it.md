# Chiavi dei dati

[English](data-keys.md) | **Italiano**

I valori e le immagini di un Pokémon che un template può usare nei binding (`"{level}"`). Ogni pacchetto dati creato dal plugin PokePic ha tutte queste chiavi, per ogni Pokémon, quindi un template che le usa funziona con qualunque Pokémon. Come funzionano binding e formati lo spiega la [guida](guide.it.md).

*Generato da `build/generate-reference.cs` da ciò che PokePic.Adapter produce per un Pokémon di PKHeX (gli esempi: Charizard Lv. 50 di Scarlatto/Violetto, soprannominato "Drago", con due mosse): non modificarlo a mano.*

| Chiave | Tipo | Significato | Esempio |
|---|---|---|---|
| `ability.name` | testo tradotto | Abilità, tradotta. | `Aiutofuoco` |
| `ball.name` | testo tradotto | Poké Ball in cui è stato catturato, tradotta. | `Poké Ball` |
| `ball.sprite` | immagine | Immagine della ball. | PNG |
| `base.atk` | intero | Statistica base della specie: Attacco. | `84` |
| `base.def` | intero | Statistica base della specie: Difesa. | `78` |
| `base.hp` | intero | Statistica base della specie: PS. | `78` |
| `base.spa` | intero | Statistica base della specie: Attacco Speciale. | `109` |
| `base.spd` | intero | Statistica base della specie: Difesa Speciale. | `85` |
| `base.spe` | intero | Statistica base della specie: Velocità. | `100` |
| `base.total` | intero | Statistica base della specie: somma delle sei. | `534` |
| `ev.atk` | intero | EV (punti allenamento): Attacco. | `0` |
| `ev.def` | intero | EV (punti allenamento): Difesa. | `0` |
| `ev.hp` | intero | EV (punti allenamento): PS. | `4` |
| `ev.spa` | intero | EV (punti allenamento): Attacco Speciale. | `252` |
| `ev.spd` | intero | EV (punti allenamento): Difesa Speciale. | `0` |
| `ev.spe` | intero | EV (punti allenamento): Velocità. | `252` |
| `ev.total` | intero | EV (punti allenamento): somma delle sei. | `508` |
| `form` | intero | Numero della forma: 0 per la forma base. | `0` |
| `friendship` | intero | Amicizia, 0 … 255. | `120` |
| `gender.isFemale` | vero/falso | Vero se femmina. | `false` |
| `gender.isMale` | vero/falso | Vero se maschio. | `true` |
| `gender.isNeutral` | vero/falso | Vero se senza sesso. | `false` |
| `gender.key` | testo | Sesso: `male`, `female` o `neutral`. | `male` |
| `gender.symbol` | testo | `♂`, `♀`, o vuoto se senza sesso. | `♂` |
| `hasHeldItem` | vero/falso | Vero se il Pokémon tiene uno strumento. | `true` |
| `hasMega` | vero/falso | Vero se la specie ha una megaevoluzione e il gioco del Pokémon la permette (da X/Y a Sole/Luna, Leggende Z-A). | `false` |
| `hasMove1` | vero/falso | Vero se lo slot della mossa 1 non è vuoto. | `true` |
| `hasMove2` | vero/falso | Vero se lo slot della mossa 2 non è vuoto. | `true` |
| `hasMove3` | vero/falso | Vero se lo slot della mossa 3 non è vuoto. | `false` |
| `hasMove4` | vero/falso | Vero se lo slot della mossa 4 non è vuoto. | `false` |
| `hasTera` | vero/falso | Vero se il Pokémon ha un teratipo (solo Pokémon di Scarlatto/Violetto). | `true` |
| `hasType2` | vero/falso | Vero se il Pokémon ha due tipi. | `true` |
| `heldItem.name` | testo tradotto | Strumento tenuto, tradotto; vuoto senza strumento. | `Avanzi` |
| `heldItem.sprite` | immagine | Immagine dello strumento tenuto; un pixel trasparente senza strumento. | PNG |
| `isNicknamed` | vero/falso | Vero se il Pokémon ha un soprannome. | `true` |
| `isShiny` | vero/falso | Vero se il Pokémon è cromatico. | `false` |
| `iv.atk` | intero | IV (valore individuale): Attacco. | `0` |
| `iv.def` | intero | IV (valore individuale): Difesa. | `31` |
| `iv.hp` | intero | IV (valore individuale): PS. | `31` |
| `iv.spa` | intero | IV (valore individuale): Attacco Speciale. | `31` |
| `iv.spd` | intero | IV (valore individuale): Difesa Speciale. | `31` |
| `iv.spe` | intero | IV (valore individuale): Velocità. | `31` |
| `iv.total` | intero | IV (valore individuale): somma delle sei. | `155` |
| `level` | intero | Livello, 1 … 100. | `50` |
| `move1.name` | testo tradotto | Nome della mossa 1, tradotto; vuoto se lo slot è vuoto. | `Lanciafiamme` |
| `move1.pp` | intero | PP attuali della mossa 1 (il massimo non è nei dati); 0 se lo slot è vuoto. | `15` |
| `move1.type.color` | colore | Colore del tipo della mossa 1 (colori di Scarlatto/Violetto). | `#E62829` |
| `move1.type.id` | intero | Numero del tipo della mossa 1 in PKHeX: normal 0 … fairy 17, stellar 18. | `9` |
| `move1.type.key` | testo | Identificativo del tipo della mossa 1: inglese, minuscolo (`fire`). Per i set di immagini. | `fire` |
| `move1.type.name` | testo tradotto | Nome del tipo della mossa 1, tradotto. | `Fuoco` |
| `move2.name` | testo tradotto | Nome della mossa 2, tradotto; vuoto se lo slot è vuoto. | `Eterelama` |
| `move2.pp` | intero | PP attuali della mossa 2 (il massimo non è nei dati); 0 se lo slot è vuoto. | `15` |
| `move2.type.color` | colore | Colore del tipo della mossa 2 (colori di Scarlatto/Violetto). | `#81B9EF` |
| `move2.type.id` | intero | Numero del tipo della mossa 2 in PKHeX: normal 0 … fairy 17, stellar 18. | `2` |
| `move2.type.key` | testo | Identificativo del tipo della mossa 2: inglese, minuscolo (`fire`). Per i set di immagini. | `flying` |
| `move2.type.name` | testo tradotto | Nome del tipo della mossa 2, tradotto. | `Volante` |
| `move3.name` | testo tradotto | Nome della mossa 3, tradotto; vuoto se lo slot è vuoto. | `""` |
| `move3.pp` | intero | PP attuali della mossa 3 (il massimo non è nei dati); 0 se lo slot è vuoto. | `0` |
| `move3.type.color` | colore | Colore del tipo della mossa 3 (colori di Scarlatto/Violetto). | `#9FA19F` |
| `move3.type.id` | intero | Numero del tipo della mossa 3 in PKHeX: normal 0 … fairy 17, stellar 18. | `0` |
| `move3.type.key` | testo | Identificativo del tipo della mossa 3: inglese, minuscolo (`fire`). Per i set di immagini. | `normal` |
| `move3.type.name` | testo tradotto | Nome del tipo della mossa 3, tradotto. | `Normale` |
| `move4.name` | testo tradotto | Nome della mossa 4, tradotto; vuoto se lo slot è vuoto. | `""` |
| `move4.pp` | intero | PP attuali della mossa 4 (il massimo non è nei dati); 0 se lo slot è vuoto. | `0` |
| `move4.type.color` | colore | Colore del tipo della mossa 4 (colori di Scarlatto/Violetto). | `#9FA19F` |
| `move4.type.id` | intero | Numero del tipo della mossa 4 in PKHeX: normal 0 … fairy 17, stellar 18. | `0` |
| `move4.type.key` | testo | Identificativo del tipo della mossa 4: inglese, minuscolo (`fire`). Per i set di immagini. | `normal` |
| `move4.type.name` | testo tradotto | Nome del tipo della mossa 4, tradotto. | `Normale` |
| `nature.down.atk` | vero/falso | Vero se la natura diminuisce Attacco. | `true` |
| `nature.down.def` | vero/falso | Vero se la natura diminuisce Difesa. | `false` |
| `nature.down.spa` | vero/falso | Vero se la natura diminuisce Attacco Speciale. | `false` |
| `nature.down.spd` | vero/falso | Vero se la natura diminuisce Difesa Speciale. | `false` |
| `nature.down.spe` | vero/falso | Vero se la natura diminuisce Velocità. | `false` |
| `nature.name` | testo tradotto | Natura, tradotta. | `Modesta` |
| `nature.up.atk` | vero/falso | Vero se la natura aumenta Attacco. | `false` |
| `nature.up.def` | vero/falso | Vero se la natura aumenta Difesa. | `false` |
| `nature.up.spa` | vero/falso | Vero se la natura aumenta Attacco Speciale. | `true` |
| `nature.up.spd` | vero/falso | Vero se la natura aumenta Difesa Speciale. | `false` |
| `nature.up.spe` | vero/falso | Vero se la natura aumenta Velocità. | `false` |
| `nickname` | testo | Soprannome, o il nome della specie se non ne ha (non tradotto). | `Drago` |
| `pokeapi.artwork` | testo | L'artwork ufficiale di PokeAPI (`…/sprites/pokemon/other/official-artwork/`), con `shiny/` quando c'è (senza immagini femminili), ad esempio `shiny/10034`. | `6` |
| `pokeapi.id` | testo | Nome delle immagini del Pokémon nel repository degli sprite di PokeAPI, per la sua forma: `6`, `10034` (MegaCharizard X), `10103` (Vulpix di Alola), `666-tundra` (Vivillon). | `6` |
| `pokeapi.move1.type.id` | intero | Numero del tipo della mossa 1 in PokeAPI (`id` + 1): normal 1 … fairy 18, stellar 19. | `10` |
| `pokeapi.move2.type.id` | intero | Numero del tipo della mossa 2 in PokeAPI (`id` + 1): normal 1 … fairy 18, stellar 19. | `3` |
| `pokeapi.move3.type.id` | intero | Numero del tipo della mossa 3 in PokeAPI (`id` + 1): normal 1 … fairy 18, stellar 19. | `1` |
| `pokeapi.move4.type.id` | intero | Numero del tipo della mossa 4 in PokeAPI (`id` + 1): normal 1 … fairy 18, stellar 19. | `1` |
| `pokeapi.path` | testo | Percorso dell'immagine del Pokémon relativo a una raccolta di immagini di PokeAPI (`…/sprites/pokemon/other/home/`, `…/sprites/pokemon/`…), con le varianti che esistono: `shiny/`, `female/`, ad esempio `shiny/female/3`. Una chiave per un set di immagini; una forma senza immagine propria dà quella della specie. Le varianti sono verificate nella raccolta HOME, la più completa: altre raccolte possono non avere alcune immagini. | `6` |
| `pokeapi.teratype.id` | intero | Numero del teratipo in PokeAPI (`id` + 1): normal 1 … fairy 18, stellar 19. Senza teratipo ripete il primo tipo. | `16` |
| `pokeapi.type1.id` | intero | Numero del primo tipo in PokeAPI (`id` + 1): normal 1 … fairy 18, stellar 19. | `10` |
| `pokeapi.type2.id` | intero | Numero del secondo tipo in PokeAPI (`id` + 1): normal 1 … fairy 18, stellar 19. Con un solo tipo ripete il primo tipo. | `3` |
| `species` | intero | Numero del Pokédex nazionale. | `6` |
| `species.name` | testo tradotto | Nome della specie, tradotto. | `Charizard` |
| `species.sprite` | immagine | Sprite di gioco del Pokémon (da PKHeX, circa 68×56, pixel art). | PNG |
| `stat.atk` | intero | Statistica al livello attuale: Attacco. | `89` |
| `stat.def` | intero | Statistica al livello attuale: Difesa. | `98` |
| `stat.hp` | intero | Statistica al livello attuale: PS. | `154` |
| `stat.spa` | intero | Statistica al livello attuale: Attacco Speciale. | `161` |
| `stat.spd` | intero | Statistica al livello attuale: Difesa Speciale. | `105` |
| `stat.spe` | intero | Statistica al livello attuale: Velocità. | `152` |
| `stat.total` | intero | Statistica al livello attuale: somma delle sei. | `759` |
| `teratype.color` | colore | Colore del teratipo (colori di Scarlatto/Violetto). Senza teratipo ripete il primo tipo. | `#5061E1` |
| `teratype.id` | intero | Numero del teratipo in PKHeX: normal 0 … fairy 17, stellar 18. Senza teratipo ripete il primo tipo. | `15` |
| `teratype.key` | testo | Identificativo del teratipo: inglese, minuscolo (`fire`). Per i set di immagini. Senza teratipo ripete il primo tipo. | `dragon` |
| `teratype.name` | testo tradotto | Nome del teratipo, tradotto. Senza teratipo ripete il primo tipo. | `Drago` |
| `type1.color` | colore | Colore del primo tipo (colori di Scarlatto/Violetto). | `#E62829` |
| `type1.id` | intero | Numero del primo tipo in PKHeX: normal 0 … fairy 17, stellar 18. | `9` |
| `type1.key` | testo | Identificativo del primo tipo: inglese, minuscolo (`fire`). Per i set di immagini. | `fire` |
| `type1.name` | testo tradotto | Nome del primo tipo, tradotto. | `Fuoco` |
| `type2.color` | colore | Colore del secondo tipo (colori di Scarlatto/Violetto). Con un solo tipo ripete il primo tipo. | `#81B9EF` |
| `type2.id` | intero | Numero del secondo tipo in PKHeX: normal 0 … fairy 17, stellar 18. Con un solo tipo ripete il primo tipo. | `2` |
| `type2.key` | testo | Identificativo del secondo tipo: inglese, minuscolo (`fire`). Per i set di immagini. Con un solo tipo ripete il primo tipo. | `flying` |
| `type2.name` | testo tradotto | Nome del secondo tipo, tradotto. Con un solo tipo ripete il primo tipo. | `Volante` |

## Valori possibili

Un set scelto da una chiave (`{set[chiave]}`) ha bisogno di un elemento (un file o un valore), o di un `fallback`, per ogni valore che la chiave può assumere:

- `type1.key`, `type2.key`, `move1.type.key` … `move4.type.key`: `normal`, `fighting`, `flying`, `poison`, `ground`, `rock`, `bug`, `ghost`, `steel`, `fire`, `water`, `grass`, `electric`, `psychic`, `ice`, `dragon`, `dark`, `fairy` (18 valori).
- `teratype.key`: gli stessi, più `stellar`.
- `*.id`: 0 … 17 nell'ordine qui sopra, 18 = `stellar` (solo teratipo). `pokeapi.*.id`: 1 … 19.
- `gender.key`: `male`, `female`, `neutral`.
- `species`: 1 … 1025. `form`: 0 per la forma base, altrimenti il numero della forma in PKHeX (megaevoluzioni comprese).
- `level` 1 … 100, `friendship` 0 … 255, `iv.*` 0 … 31 (`iv.total` fino a 186), `ev.*` 0 … 252 (255 prima di X/Y; `ev.total` fino a 510), `base.*` 1 … 255.

Gli slot vuoti mantengono le loro chiavi: senza terza mossa `move3.name` è vuoto e `move3.type.*` è `normal`; con un solo tipo `type2.*` ripete `type1.*`; senza teratipo `teratype.*` ripete `type1.*`; senza strumento `heldItem.sprite` è un pixel trasparente. Usa `hasMove3`, `hasType2`, `hasTera`, `hasHeldItem` in `visible` per nascondere quelle parti.
