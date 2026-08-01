> Imported verbatim from WordOnline `client/.art/STYLE.md`. Reference copy;
> the WordOnline repository stays the source of truth. Do not edit here.

# Art Style

Two axes, kept strictly separate:

- **Rendering technique** — identical across every faction. This is what makes
  the game read as one game.
- **Shape language + palette** — deliberately different per faction. This is
  what makes factions readable at a glance.

Collapsing the two is the failure mode that produced the current drift: "hellfire
should look menacing" turned into "generate hellfire with a different model,"
which broke technique instead of shape.

## Master-style status

One master rendering reference must apply to every character, creature,
building, environment object, and effect. Faction anchors may define subject
identity, shape, and palette, but must not override the master rendering method.

Current status: **A selected and frozen on 2026-07-28**.

Canonical master reference:
`.art/anchors/master-v2/MasterStyleKey.png`

Decision reason: A gives every faction the same popup-book-compatible material,
camera, value structure, and detail density while preserving faction identity
through silhouette and palette. It also fits the existing 2.5D direction better
than vector, clay, or gouache alternatives.

Selected concept:

- A — `.art/concept/master-style-key-v2.png`: 2.5D cut-paper

Use A for all new canonical production art. Rejected B, C, and D exploration
files were removed so they cannot be mixed into generation prompts.

## Shared rendering technique (never varies)

- 2.5D cut-paper cartoon. Build forms from large overlapping matte paper shapes.
- Use three value bands per material: base, broad upper-left highlight, broad
  lower-right shadow. Allow only a very subtle gradient inside a large shape.
- Keep one consistent three-quarter camera facing right.
- Characters use chunky proportions around three heads tall. Buildings and
  environment objects use equally chunky modular forms.
- Use the same simple oval-eye grammar: dark oval with one tiny highlight.
- No painterly brushwork, fine detail noise, or ember/spark scatter.
- No outer contour line. Forms separate by value, not by stroke.
- One soft light source from upper-left. Highlight on top, shade underneath.
- Use short, soft contact shadows only when the asset is placed in a scene.
  Transparent production sprites and anchors carry no baked shadow.
- Restrained saturation, mid-to-high value. Nothing muddy or neon.
- Single subject, centered, transparent background. No scene, no ground plane,
  no framing, no text.
- Characters face right.
- Silhouette must survive downscale to 64px. Thin strokes and scattered
  fragments are rejects — that is why `ChainLightning` and `LightningDrop` fail.
- Max size by tier, aspect preserved, then trimmed tight:
  small `128x128`, middle `192x192`, big `256x256`.

Animation frames and aura assets follow `.art/ANIMATION-ASSETS.md`. Numeric
state names such as `MagmaSpiritAttacking` identify attack frames, not separate units. Keep
body scale, PPU, Bottom Center pivot, and ground contact invariant across frames.
Generate aura as a separate transparent effect asset, never baked into the body.
`CloudDragon` uses a dedicated spherical **water** aura (`cloud.png`), not the
shared wind aura; keep its center quiet enough for the body silhouette to read.

```bash
magick input.png -resize 256x256 -trim +repage output.png
```

**Ground contact:** unresolved. `RockGolem` carries debris and a contact shadow;
most others float clean. Pick one rule per archetype before the next batch.

## Factions

Faction art derives from the confirmed lore in `WORLD.md`. Keep lore facts there
instead of expanding them independently in this style guide.

### Spirits — lightning / nature / wind

Born around the World Tree. The baseline cute register.

- Round, blobby, weightless. Flowing tails and wisps rather than limbs.
- Large friendly eyes, simple mouth. Reads as benign.
- No hard edges, no armor, no tools.
- Anchors: `ThunderSpirit`, `ZapMouse`, `SeedSpiritSwarm`, `VineSpirit`,
  `WindSpirit`, `CloudDragon`

| | Palette |
|---|---|
| Lightning | `#FCFBD5` `#F6DB5F` `#D7A313` `#996B07` |
| Nature | `#B8CE59` `#9DA74D` `#768738` `#384823` |
| Wind | `#E5EBE7` `#B3CCCA` `#9BB3B3` `#5EA191` `#2A6D64` |

### Water slimes — the moderates

Survivors, not spirits. Slightly more substance than the spirit set but still soft.

- Translucent, gel-like body. Volume implied by internal highlight.
- May carry simple gear (`AquaArcher` has a bow) — spirits may not.
- Anchors: `AquaArcher`, `BubbleSpirit`, `WaterSlimeSwarm`, `TideCall`

Palette: `#E8F7EE` `#98DEEB` `#6FCCF4` `#53B2EB` `#2D92E4`

### Rock golems — the tribe

Neither spirit nor demon. Neutral third party.

- Blocky stacked masses, visible seams between blocks.
- Heavy, grounded, wide stance. Small head relative to body.
- Moss and vegetation growing on stone — this is the tribe's signature and the
  thing that separates them from human masonry.
- Anchors: `RockGolem`, `RockMage`

Palette: warm tan stone `#CDC8B8` `#A3A29D` `#8A857A`, moss `#55572D` `#374725`

### Humans — the player's faction

Machinery and masonry. Built, not born.

- Straight lines, right angles, bilateral symmetry. Nothing organic.
- Cut-stone blocks, riveted metal, wood beams, rope.
- No face, no eyes. These are devices, not creatures.
- Anchors: `Cannon`, `ElectricTower`

Palette: cool grey `#C4CCC8` `#A09C92` `#616662`, steel blue `#6191A3`,
bronze/wood `#5D4032`

> **Conflict to fix.** Human grey and golem grey currently sample nearly
> identical (`#A09C92` vs `#A3A29D`). The two factions are not separable by
> color. Resolution: golems go warm tan + moss, humans go cool grey + steel blue
> + bronze. Existing sprites need a pass to enforce this.
>
`Towerback` is assigned to the human faction as a forced weapon: a kidnapped
MiniRock carries a human anti-air tower. Keep the warm living stone and moss of
the captive body visibly separate from the cool masonry, steel-blue metal, and
bronze restraints of the mounted tower.

### Hellfire legion — the demons

**Not spirits.** Every current fire sprite violates this and is a redesign target.

- Angular, spiked silhouette. Horns, fangs, jagged shell, cracked crust.
- No large friendly eyes. Eyes are glowing slits, points, or absent.
- Dark base with internal glow through cracks — mass first, flame second.
  Current sprites are flame-first, which is why they read as spirits.
- Asymmetry allowed; the other factions stay symmetric.

Palette: `#E65E08` `#9B3014` `#5A291E` `#2A1512`, ash `#6D4A39`

**No anchor exists.** `FrenzyTotem` is the only sprite whose shape language is
on-concept (dark crimson, angular, tribal) and whose technique is clean — use it
as the provisional shape/palette reference until a real creature anchor is made.

Redesign list: `FireSpirit`, `EmberSpiritSwarm`, `FireLordSpirit`, `FireTadpole`,
`FireChildSpirit`, `MagmaSpirit`, `MagmaSpiritAttacking`, `MagmaExplosion`, `Crater`,
`FireShot`, `RallyingTorch`.

`MagmaSpirit` and `Crater` already have the right concept; only their technique
is wrong. `FireSpirit` and friends have the right technique and the wrong
concept. Different fixes.

## Palette provenance

Values above were sampled from shipped sprites via quantization, not authored.
They describe what the art currently is. Treat them as a starting point and
replace any that were pulled from a sprite on the redesign list.

```bash
magick Assets/Resources/Game/sprites/VineSpirit.png -background none -flatten \
  -trim +repage -alpha off -colors 12 -format %c histogram:info:- | sort -rn | head
```
