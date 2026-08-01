> WordOnline에서 2026-08-01에 복제한 사본이다. 이후로는 WordCraft가 자체적으로
> 관리하며, 원본 저장소가 없어도 이 문서만으로 작업이 성립해야 한다.
> WordCraft 고유 설정은 `docs/FACTIONS.md`와 `docs/FACTION-MECHANICS.md`에 쓴다.

# Concept Art Brief

Prompts and generated outcomes for the project's image-generation workflow.
Current comparison boards are published through the project art-direction Site.

## How concept art is used

| Artifact | Role | Fed to the generator as an image reference? |
|---|---|---|
| `.art/anchors/master-v2/*.png` | Defines *how* to draw | **Yes** — same format as the output: transparent, single subject |
| `.art/concept/*.png` | Defines *what* to draw | **No** |
| Palettes in `STYLE.md` | Color lock | No — passed as hex text |

Concept art carries backgrounds, multiple views, and framing. Feed it to the
generator as a reference image and the generator copies that composition too.
Concept art is for a human to look at while writing a prompt.

## Workflow

1. Generate from a prompt below.
2. Save to `.art/concept/<faction>-<variant>.png`.
3. Run `./.art/make-sheets.sh` — it builds a comparison sheet of the variants.
4. Pick a direction; record the decision in `STYLE.md`.
5. Once a direction is chosen, produce one *clean sprite* in that style
   (transparent, single subject) and promote it to `.art/anchors/master-v2/`. The concept
   image itself never becomes an anchor.

Current comparison Site:
<https://wordonline-hellfire-art.dev-yunseong.chatgpt.site>

## Master style selection

Selected shared rendering technique:

- A — `.art/concept/master-style-key-v2.png`: 2.5D cut-paper

Selected: **A — 2.5D cut-paper**, approved 2026-07-28.

Canonical anchor set: `.art/anchors/master-v2/`.

B, C, and D were rejected and removed. Do not restore rejected exploration
files or mix them into the rendering reference set.

---

## Priority 1 — Hellfire legion

The only faction with no valid anchor, and the whole fire set is a redesign
target. Three directions; generate all three, pick one.

The three initial exploration images were rejected after the shared master style
was selected and were removed. The retained direction is
`.art/concept/hellfire-lineup-cut-paper-v2.png`.

Shared prefix for all three:

> Flat cartoon game sprite icon, single subject centered on a fully transparent
> background, no outer contour line, broad flat color blocks with soft internal
> gradient, soft light from upper left, high saturation, no fine detail noise, no
> spark or ember scatter, no background, no ground, no frame, no text, facing
> right, silhouette readable at 64 pixels.

### Variant A — Molten primitive

> ...A demonic molten creature from a hellfire dimension. Dark charred basalt
> crust as the primary mass, glowing magma visible through deep cracks in the
> crust. Jagged asymmetric silhouette, no face, eyes are two small glowing slits.
> Mass reads as rock first and fire second. Colors `#2A1512` `#5A291E` `#9B3014`
> `#E65E08`.

### Variant B — Horned demon

> ...A small demon soldier from a hellfire dimension. Deep red skin, black curved
> horns, visible fangs, angular spiked shoulders, clawed hands. Clear character
> anatomy with head, torso and limbs. Narrow glowing yellow slit eyes, hostile
> expression. Colors `#9B3014` `#5A291E` `#2A1512` with `#E65E08` accents.

### Variant C — Ash wraith

> ...A wraith of burnt ash from a hellfire dimension. Near-black smoky silhouette
> with a torn ragged lower edge dissolving into ash, inner core glowing orange
> through the body. Tall and narrow, hollow eye sockets with orange points
> inside. No limbs, no visible face. Colors `#2A1512` `#6D4A39` `#9B3014`
> `#E65E08`.

**Judging criteria.** Reject any variant that (a) reads cute, (b) has large round
friendly eyes, (c) is flame-shaped rather than mass-shaped, or (d) shows
painterly ember scatter. The current `FireSpirit` fails (a)(b)(c); the current
`MagmaSpirit` fails (d).

---

## Priority 2 — Human / golem separation

Both factions currently sample to the same grey. Generate one of each, side by
side, to confirm the split reads.

### Human device

> [shared prefix] ...A built defensive device of the human faction. Cut stone
> blocks, riveted steel plates, wooden beams and rope. Straight lines, right
> angles, bilateral symmetry. No face, no eyes — a machine, not a creature. Cool
> grey stone `#C4CCC8` `#A09C92` `#616662` with steel blue `#6191A3` and bronze
> `#5D4032` accents.

### Rock golem

> [shared prefix] ...A rock golem tribe warrior. Body built from stacked blocky
> boulders with visible seams, heavy wide stance, small head on a large torso.
> Green moss and small plants growing across the stone. Warm tan stone `#CDC8B8`
> `#A3A29D` `#8A857A` with moss `#55572D` `#374725`.

Reject if the two read as the same material at thumbnail size.

---

## Priority 3 — Faction lineup boards

Not for generation reference — for a human to hold the world in their head. One
per faction, backgrounds allowed here since these are never fed back in.

> Character lineup sheet, four to six creatures of one faction standing in a row
> on a neutral flat background, consistent scale showing size tiers from small to
> large, flat cartoon style, no text.

Factions: spirits (lightning/nature/wind), water slimes, rock golems, humans,
hellfire legion.

Generated on 2026-07-28:

- `.art/concept/world-tree-spirits-lineup.png`
- `.art/concept/water-slimes-lineup.png`
- `.art/concept/rock-golems-lineup.png`
- `.art/concept/human-magic-civilization.png`

Additional character and lore exploration:

- `.art/concept/apprentice-player.png` — player appearance is not canonized
- `.art/concept/word-world-tree-key-art.png` — Word remains symbolic because
  the character's established `WordVenture` appearance was not available as a
  reference

### World Tree key art

The lore center — Word died and the tree grew from where he fell, spirits arising
around it. Worth one piece even though no sprite depends on it directly.

> Key art of a colossal ancient world tree, glowing runes in the bark, small
> elemental spirits drifting around its roots, flat cartoon style, warm light, no
> text.

---

## Recording the outcome

After picking, write down in `STYLE.md` which variant won and why. A chosen
direction with no recorded reason gets re-litigated every time someone adds art.
