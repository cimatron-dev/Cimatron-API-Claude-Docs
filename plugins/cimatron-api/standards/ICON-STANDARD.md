# The Cimatron icon standard

This document is the canonical source of truth for **how a Cimatron API plugin icon should look** so it reads as a native Cimatron / 3DXpert icon rather than a foreign add-on. Where `COMMAND-STANDARD.md` governs how a command is *named and wired*, this file governs how its icon is *styled and coloured*.

**Source.** Transcribed from `Cimatron_Icon-Definition.pdf` — the last published Cimatron/3DXpert icon colour scheme, sent by Danny Branch (CAD product owner, current CAD icon designer) on 2026-06-17 in the "CAM Icons for Claude" thread. Per Danny, that colour scheme "is still relevant to the icons we have today." Andrew DeVries framed the goal as giving the existing icon look "more modern depth and a 3D look" without redesigning it wholesale. Changing the palette itself would need management + marketing buy-in, so treat the values below as fixed.

> **Note on fidelity.** The values here were extracted from the PDF text. Palette 6's *Dark* and *Outline* tones did not survive extraction and are marked as such below — consult the original PDF (or Danny) before relying on palette 6 in full. Every other value is verbatim from the document.

## Design goals (from the PDF)

The icon system exists to deliver three things. Hold a generated icon against these before shipping it:

1. **Distinctive style** — an icon should be identifiable at a glance as a "Cimatron/3DXpert icon," not a generic glyph.
2. **Consistency** — the whole icon set has to work together visually. A new plugin icon should look like it belongs next to the shipped ones, which is what the shared palette below buys you.
3. **Natural progression of the actual icons** — evolve the current look (more depth, a subtle 3D feel), don't reinvent it.

The "3D depth" look is produced mechanically by the **four-tone body ramp** in each palette: a light top face, a medium mid-tone, a dark lower face, and a dark outline. Shading a shape with Light → Medium → Dark across its faces plus the Outline around its silhouette is what makes a flat glyph read as a dimensional object.

## Colour scheme

Each palette is a 4-tone ramp meant to shade one body: **Body Light** (top / highlight face), **Body Medium** (main face), **Body Dark** (shadowed face), **Body Outline** (silhouette stroke). Use one palette per object in the icon; combine palettes only when the icon depicts genuinely distinct objects (see the per-module table — many modules legitimately mix 2–4 palettes).

| # | Family | Body Light | Body Medium | Body Dark | Body Outline |
|---|---|---|---|---|---|
| 1 | Neutral / grey | `#FCFCFC` (252,252,252) | `#EFEFEF` (239,239,239) | `#BEBEBE` (190,190,190) | `#3C3C3C` (60,60,60) |
| 2 | Green | `#C6ECCB` (198,236,203) | `#9FE0A9` (159,224,169) | `#40C253` (64,194,83) | `#01953F` (1,149,63) |
| 3 | Orange / gold | `#FBE9BC` (251,233,188) | `#F7D694` (247,214,148) | `#FFA748` (255,167,72) | `#ED8733` (237,135,51) |
| 4 | Blue | `#A6D3FF` (166,211,255) | `#72B6F8` (114,182,248) | `#3083D9` (48,131,217) | `#134E90` (19,78,144) |
| 5 | Red | `#FFC5C5` (255,197,197) | `#FF9898` (255,152,152) | `#E47474` (228,116,116) | `#B63C3B` (182,60,59) |
| 6 | Magenta / pink | `#FFEBFE` (255,235,254) | `#FF53FF` (255,83,255) | *(not captured — see PDF)* | *(not captured — see PDF)* |

## Per-module palette assignments

Pick the palette(s) for a plugin's icon from the module the command lives in (this is usually the second `MenuPath` segment / the command's functional area). Numbers refer to the palettes above.

| Module / area | Palettes |
|---|---|
| Assembly | 1, 2, 3 |
| Die Design | **3** (orange) — the scheme replaces Die Design's main colour with the orange palette; legacy was 1, 2, 3 |
| Drafting | 1, 3, 4, 6 |
| Electrode | 1, 2, 3, 5 |
| Parting | 2, 4, 5 |
| NC | 1, 3, 4, 5 |
| Mold Design — Cooling | 3, 4 |
| Mold Design — Mold Component | 2, 3, 4 |
| Mold Design — Ejection | 1, 2, 5 |
| Mold Design — Insert | 1, 2, 3 |
| Mold Design — Lifter | 1, 2, 3 |
| Mold Design — Mold Base | 1, 3, 4 |
| Mold Design — Runner | 1, 2, 3 |
| Sketcher | 1, 2 |
| Part — Curve | 1, 2 |
| Part — Faces | 1, 2, 5 |
| Part — Solid | 1, 2, 5 |
| Mesh | 1, 2, 3 |
| 3D Printing — Printing Tools | 1, 2, 3 |
| 3D Printing — Multiple Parts Tools | 1, 2 |
| 3D Printing — Lattice Tools | 1, 2, 3 |
| 3D Printing — Supports | 1, 3, 5 |
| Environment | 1, 2 |
| Component Operations | 1, 2, 3 |

If a plugin doesn't map cleanly to any of these, default to palette **1 (neutral grey)** for the main body and add a single accent palette that matches its functional area. When unsure which area applies, ask the user rather than guessing a colour.

## Sizes

Native Cimatron icons ship **7 sizes** in a single icon resource:

`64×64` · `48×48` · `40×40` · `32×32` · `24×24` · `20×20` · `16×16`

Two consequences that hold even when you only emit one frame:

- **The design must read at the smallest size.** In CAD the icons appear in the feature tree at **16×16**, so the glyph has to survive that. Danny: "for sure in CAD we must be as simplistic as possible." Design the silhouette so it's legible at 16×16 — bold shapes, 2–3 tones, no fine interior detail.
- **64×64 is the ceiling.** Without major GUI changes the largest rendered size is 64×64, so don't invest in detail that only appears above that.

> The shipped `/icon-creator` workflow emits a single **32×32** frame (see `agents/icon-creator.md`). That is intentional for the API-plugin case; this section documents the native multi-size convention so the *design* respects the 16×16 floor even though only one frame is written. Emitting the full 7-frame set is a deliberate, separate change — not the default.

## Badges

The icon system has a fixed vocabulary of overlay badges for common command verbs. When a plugin's command is a variant of one of these actions, reuse the established badge rather than inventing a new glyph, so it reads consistently with the native set:

`Delete` · `Add` · `Save` · `Edit` · `Filter` · `New` · `Exit arrow` · `Transform/move` · `Select` · `Image` · `Camera`

## How this is consumed

Per the migration note in `COMMAND-STANDARD.md`, subagents do **not** auto-load external files at spawn time. The actionable subset of this standard (palettes, design goals, 16×16 floor, module selection, badges) is therefore embedded directly in `plugins/cimatron-api/agents/icon-creator.md` under "Native Cimatron look & feel," mirroring how the command rules are embedded in the scaffold/reviewer agents. This file is the source of truth; when the agent's wording diverges from the values here, this file wins.
