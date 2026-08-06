# Design

<!-- impeccable:design-schema 1 -->

Recorded from the built world after the finish review, not from intention. Where this file and
the code disagree, the code is right and this file is stale — fix it.

**World:** Split-Flap Concourse. Seed key `11888fdd`, chosen by the user over assigned index 6.
**Lineage:** railway departure boards. **Visitor mode:** Operate.
**Direction contract:** lives as a real HTML comment in the served document — see
`src/PoRedoImage.Web/Components/App.razor`, field `DirectionContract`. Audit with
`curl -s <host>/login | grep 11888fdd`.

---

## The one idea

The queue is the interface. Every job is a live board row that changes state in place. This is a
refusal of the dark-glass AI dashboard and its grid of same-size feature cards — the look the
incumbent codebase had, and the look the detector's `slop` rules exist to catch.

Rows are the live entities; **columns never move**. When you add a surface, ask what its rows are
before you ask what its panels look like.

---

## Palette

Fixed. This world does not re-theme, and there is no light mode: a departure board does not have
one. `color-scheme: dark` is declared and there is no `prefers-color-scheme` block.

| Token | Value | Role |
|---|---|---|
| `--flap-black` | `#0D0D0F` | flap face — the ground of every panel |
| `--flap-shadow` | `#1B1B1E` | recessed cell / elevated face |
| `--flap-white` | `#F2F2F2` | painted letters, and the DONE state |
| `--amber` | `#FFB400` | WORKING, and the single accent |
| `--amber-dim` | `#8a6200` | unlit lamp |
| `--cancel-red` | `#D32F2F` | FAILED — fills and borders only |
| `--cancel-red-tx` | `#F0736F` | FAILED — small text (6.4:1 on flap-black) |
| `--steel` | `#B6BBC2` | frame highlight, secondary text |
| `--steel-dark` | `#7D838C` | frame body, labels (4.9:1 on flap-black) |
| `--board-ground` | `#070708` | concourse wall behind the boards |

Measured contrast on `--flap-black`: flap-white ≈ 18:1, amber ≈ 10:1, steel ≈ 9.6:1,
steel-dark ≈ 4.9:1. `--cancel-red` is ~3.9:1 and is therefore **never used for small text** —
that is what `--cancel-red-tx` exists for. Steel rails are a *light* surface: text on them is
near-black (`#14161a`, `#22252b`), never the board's dim greys.

### Status is a four-value law

`steel-dark = QUEUED` · `amber = WORKING` · `flap-white = DONE` · `red = FAILED`.

Do not invent a fifth. Every state also carries its own **word** (`.status--*`), so the board reads
correctly in monochrome and for colour-blind users — colour is never the sole signal. Gallery item
kind is likewise carried by a drawn badge glyph, with the frame colour only echoing it.

---

## Type

One family, two widths, loaded in `App.razor` from Google Fonts.

- `--font-flap` — **Archivo Narrow**. Flap faces, headings, labels, controls, status, all data.
  Always upper-case, tracked `0.1em`–`0.2em`.
- `--font-ui` — **Archivo**. Running copy, descriptions, error text. Mixed case, `line-height: 1.6`,
  measure capped at 60–68ch.
- Numerals are `tabular-nums` globally, because most numbers here are positions and counts.

Inter was removed. It was declared but never loaded, so the previous build was rendering its display
voice in the platform system font.

---

## Materials

Three surfaces. Everything is composed from these; do not introduce a fourth.

1. **Flap cell** (`--inset-cell` + a mid-height split line) — a value sitting in a cell.
2. **Brushed steel** (`--steel-face`) — the gantry. Header, footer, and every panel rail. Bolt heads
   are a background layer at the rails' two ends.
3. **Row lamp** (`--lamp-lit` / `--lamp-dark`) — amber dot matrix, lit only while something is
   actually working.

### Banned in this world

Square corners only — every `--radius-*` token is `0` and exists only because older markup names
them. No glass or `backdrop-filter` as decoration (the `--glass-*` tokens are neutralised, not
honoured). No gradient text. No colored glow: shadows carry a real offset and blur and are neutral
black. Steel and lamp are the only gradients, and both are material.

---

## Composition

- Everything is a **board**: a framed panel, a steel rail that names it, ruled rows inside.
- Lists are rows separated by `1px solid var(--rule)` hairlines — **not** cards. The Studio's five
  transformations are a ruled service list with a SERVICE/STATUS header, not a card grid.
- Grouped cells are separated by `gap: 1px` over a `--rule` background, so the gap reads as a rule.
- `.board-row` / `.board-rows` / `.board-row__name` are the row vocabulary.

> **`.row` is Bootstrap's.** Nine pages use `<div class="row g-4">`. Defining a bare `.row` rule
> here turned every one of them into a 3-column grid and collapsed their columns to ~10px. That bug
> shipped once during this build and was caught in inspection. Never reintroduce a bare `.row` rule.

---

## Motion

**One authored moment: the flap cascade.** `FlapText` renders each character as its own `.flap`
cell, `@key`'d by position + glyph. When a value changes, Blazor replaces only the cells whose
character actually differs, so the ripple runs across just the part that changed — the real board's
behaviour. Animating the whole strip would read as a fade.

Everything else is a 120ms state change. Easing is exponential ease-out
(`cubic-bezier(0.16, 1, 0.3, 1)`); `--motion-ease-spring` is deliberately mapped to the same curve
because flaps do not bounce. Progress and reveals animate `transform`/`opacity`, never `width`.

`prefers-reduced-motion` collapses the cascade to a single update, which is the source world's own
stated rule.

Where flaps are used: the brand wordmark, page titles, bulk bay numbers, and the session cost
counter. They are for **values and identity**, not for body copy.

---

## Responsive

Portrait is the primary composition; desktop is the adaptation. Breakpoints: 900px (Studio splits
to two columns), 639px (chrome tightens), 576px (board reflows), 479px (wordmark drops), 340px.

Rules learned the hard way during this build:

- **Intake before configuration, at every width.** `.studio-upload-col > .ai-picker { order: 2 }`
  sits outside any media query. The contract promises intake at the head of the board; with the
  provider panel on top, the primary action sat under six selectors.
- **Flap strips wrap** (`flex-wrap: wrap`). Cells are fixed-width, so "IMAGE REGENERATION" is
  ~330px of them and overflowed a 390px viewport. A board wraps a long name; it never scrolls
  sideways. Title flex parents need `min-width: 0` for this to take effect.
- **When a column moves, its header goes with it.** Below 576px the status column drops beneath the
  name, so `.row-head` is hidden rather than left pointing at the wrong place.
- Bulk generate keeps **two bays across** on a phone; one column pushed bay 10 far below the fold.
- Provider selectors stack label-above-value at all widths — the option strings are long enough that
  a side-by-side grid truncated every one.

---

## Framework relationship

Bootstrap and Radzen stay referenced and functional; only their visual layer is overridden, from
`app.css`. Packages and component usage were not removed.

- Bootstrap's `primary` blue does not exist here: `.text-primary` / `.border-primary` map to amber.
- `.bg-success`/`.text-success` map to `--flap-white` (DONE), `warning` to amber, `danger` to red.
- Radzen's Material elevation and rounding are stripped (`.rz-*` overrides).
- Legacy `--color-*` tokens are remapped to board values so any rule this system misses still lands
  inside the world instead of reverting to the old navy/plum.

### Scoped CSS trap

Blazor's per-component attribute means a layout's scoped stylesheet **cannot** reach a child
component's markup. `.mock-data-banner` was styled in `MainLayout.razor.css` and rendered as an
unstyled white bar across every page until it moved to `app.css`. The same applies to
`.app-nav__user` and `.session-cost`. Rules for a child component's own elements belong in the
global sheet or in that component's own scoped file.

---

## Icons

Bootstrap Icons only, one size and weight per context. Emoji were removed from `FeatureCatalog` and
`MyImagesGallery`: they render in whatever colour and weight the platform font supplies, which a
board built on one consistent stroke cannot absorb.

---

## Open / not done

- Flap cells on bulk bay numbers and the session-cost counter are implemented and compile, but the
  empty-state screenshots this build was reviewed against never exercise them. Verify visually on
  the next run that generates images.
- The steel is a CSS gradient, not a rendered material, and carries less grain and hardware than the
  reference board. If image generation becomes available, the rails are the first place to spend it.
- Primary buttons do not carry the reference's trailing arrow. Accepted adaptation, not an oversight.
- `src/PoRedoImage.Client/wwwroot/index.html` is not the served host document (that is
  `PoRedoImage.Web/Components/App.razor`) and is kept in step manually. It is a candidate for
  deletion under the Zero-Waste policy; left in place because nothing verified it is truly unused.
