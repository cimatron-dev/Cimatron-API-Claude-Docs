This is the canonical Cimatron entity-coloring snippet. It is loaded by the `entity-color-scaffold` agent verbatim when adding color support to a plugin. Edit here, not in the agent.

Derived from the verified working implementations `OccToCimatron/Core/Exchange.cs!SetEntityColor` and `FaceColorTool` (runtime-confirmed in Cimatron 2026). The write is dead simple: for every entity, `Create` a `cmAttColor` (empty name), set its `Value`, and `Attach`. `Attach` **replaces** the entity's single (unnamed) color attribute, so there's no need to look up an existing one first — it works whether or not the entity was already colored. An earlier version of this snippet tried to edit an existing attribute's `Value` in place and only `Create`+`Attach` when none existed; that path did **not** repaint reliably, so don't reintroduce it. The gotchas in the "Why it goes wrong" section are the actual causes of "Cimatron colored the entity wrong / not at all" — bake them in, don't simplify them out.

## How coloring works

A display color in Cimatron is a `cmAttColor` **attribute** attached to an entity, not a property you set. You:

1. Get the `IAttributeFactory` from the application (once per batch — it's not per-entity).
2. For each entity, `Create` a `cmAttColor` attribute (empty name `""`), set its `Value` to the packed color int, then `Attach` it via the entity's `IAttributeSink`. `Attach` replaces the entity's single (unnamed) color attribute, so this is correct whether or not the entity is already colored — you do **not** need to look up the existing attribute or edit it in place.

Coloring touches **only** the entity and the application — it does **not** go through the model (`IMdlrModel` / NC model), so the same code works for Part and NC documents without any per-context cast. (Contrast with sets / filters, which do need the model — see `sets-builder`.)

## The color int format — get this right or red/blue swap

`cmAttColor.Value` is a packed Win32 **`COLORREF`** int — `0x00BBGGRR`, with **R in the low byte and B in the high byte**:

```
colorRef = R | (G << 8) | (B << 16)      // i.e. 0x00BBGGRR, Win32 COLORREF
```

Example: R=255 G=80 B=0 → `255 | (80 << 8) | (0 << 16)` → `0x0050FF`.

This was runtime-confirmed in Cimatron 2026: feeding `0xFF0000` painted the entity **blue**, proving B is the high byte. It is **NOT** `0xRRGGBB`, and it is **NOT** a Cimatron palette index (small ints like 3/5/9 decode near `0x000000` and render jet black — that's the tell that the field is a packed RGB, not an index).

`System.Drawing.Color.ToArgb()` gives `0x00RRGGBB` (R high), which is the *opposite* order — feeding it straight in swaps red and blue. Convert explicitly:

```csharp
// From a System.Drawing.Color:
int colorRef = c.R | (c.G << 8) | (c.B << 16);
```

## The helper (`helpers/EntityColor.cs`)

Drop this in alongside the project's existing `helpers/Logger.cs`. It is self-contained; the only project-specific edits are the namespace and (if the project's logger differs) the `LogException` calls.

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using interop.CimBaseAPI;
using ICimEntity = interop.CimBaseAPI.ICimEntity;

namespace <YourNamespace>.Helpers
{
    // Applies / clears the cmAttColor display attribute on entities.
    // Coloring is attribute work on the entity + application only; it does not
    // touch the model, so this is Part/NC neutral.
    internal static class EntityColor
    {
        // Pack an RGB triple into Cimatron's color int (Win32 COLORREF, 0x00BBGGRR).
        public static int Rgb(int r, int g, int b) => r | (g << 8) | (b << 16);

        // Set the display color of every entity in the list to colorRef (0x00BBGGRR).
        public static void Apply(IEnumerable<ICimEntity> entities, int colorRef)
        {
            try
            {
                var appProvider = new interop.CimServicesAPI.CimApplicationProvider();
                var app = (interop.CimatronE.IApplication)appProvider.GetApplication();
                var attrFactory = (IAttributeFactory)app.GetAttributeFactory();

                foreach (var entity in entities)
                {
                    try
                    {
                        // Create a fresh cmAttColor (empty name), set Value, then Attach.
                        // Attach REPLACES the entity's single color attribute, so this
                        // works whether or not the entity was already colored — no
                        // get-before-create needed. Always set Value before Attach.
                        var attr = attrFactory.Create(AttributeEnumType.cmAttColor, "");
                        attr.Value = colorRef;
                        ((IAttributeSink)entity).Attach(attr);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "EntityColor.Apply: entity " + entity.ID);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "EntityColor.Apply failed");
            }
        }

        // Remove the explicit color, reverting the entity to its default shading.
        public static void Clear(IEnumerable<ICimEntity> entities)
        {
            try
            {
                foreach (var entity in entities)
                {
                    try
                    {
                        var attrSink = (IAttributeSink)entity;
                        IAttribute existing = null;
                        try { existing = attrSink.GetAttribute(AttributeEnumType.cmAttColor, ""); }
                        catch (COMException) { existing = null; }
                        if (existing != null) attrSink.Detach(existing);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, "EntityColor.Clear: entity " + entity.ID);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "EntityColor.Clear failed");
            }
        }
    }
}
```

Call site:

```csharp
// e.g. from OnApply, OnCommand, or a hook — after the entities are picked.
Helpers.EntityColor.Apply(pickedFaces, Helpers.EntityColor.Rgb(255, 80, 0));
```

## Why it goes wrong (the three real bugs)

| Symptom | Cause | Fix baked into the helper |
|---|---|---|
| Recoloring an already-colored entity doesn't take | Tried to fetch the existing `cmAttColor` and edit its `Value` in place — that doesn't repaint reliably | Always `Create`+`Attach` a fresh attribute; `Attach` replaces the entity's single color attribute |
| Color set on the attribute doesn't show | `Value` set **after** `Attach` | Set `Value` **before** `Attach` |
| Red and blue swapped | Built the int as `0xRRGGBB` (or passed `Color.ToArgb()`, which is `0x00RRGGBB`) instead of Cimatron's Win32 `COLORREF` | Build the int with `Rgb(r,g,b)` = `r\|(g<<8)\|(b<<16)` (`0x00BBGGRR`) |
| Whole entity is jet black | Passed a tiny int (e.g. a palette index like 3/5/9) — those decode near `0x000000` | The field is a packed RGB, not a palette index; build it with `Rgb(r,g,b)` |

Two more:

- **`GetAttribute` throws when absent.** It raises `COMException` (it does **not** return null) for an entity with no color. The `Apply` path sidesteps this entirely by always creating + attaching, but `Clear` (and any read-back) must wrap `GetAttribute` in `try/catch (COMException)` and treat the throw as "no color".
- **Color set but not visible until something else redraws.** When you color from a Feature Guide's `OnApply`/`OnOk`, Cimatron repaints as the interaction commits, so no manual refresh is needed. If you color from a context that does **not** commit the interaction — a per-pick event or an SP-button click handler — the change may not show until a redraw. Nudge a redraw of the affected entities (e.g. clear/re-set the document selection) rather than re-touching the attribute, which is already correct.

## Entity types

`cmAttColor` attaches to any entity that implements `IAttributeSink` — faces, edges, curves, surfaces, bodies. The helper takes `IEnumerable<ICimEntity>`, so it's agnostic; the caller decides what to color. Coloring a body vs. its individual faces is a caller choice, not a helper concern.
