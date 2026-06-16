This is the canonical Cimatron entity-coloring snippet. It is loaded by the `entity-color-scaffold` agent verbatim when adding color support to a plugin. Edit here, not in the agent.

Derived from a verified working implementation (`ManufacturingPlanning/MfgPlanStage.cs!ApplyFaceColor`, itself cribbed from `ExportNcAdvanced.dll!AttributeHandler.attachAttributes`). One deliberate divergence from that reference: where it detached the existing `cmAttColor` and attached a fresh one, this snippet **edits the existing attribute's `Value` in place** and only `Create`+`Attach`es when the entity has no color yet. The gotchas in the "Why it goes wrong" section are the actual causes of "Cimatron colored the entity wrong / not at all" — bake them in, don't simplify them out.

## How coloring works

A display color in Cimatron is a `cmAttColor` **attribute** attached to an entity, not a property you set. You:

1. Get the `IAttributeFactory` from the application (once per batch — it's not per-entity).
2. For each entity, look for its existing `cmAttColor` attribute. **If it has one, just set that attribute's `Value`** to the packed color int. **If it doesn't, `Create` one, set its `Value`, and `Attach` it.** Don't detach-and-reattach when a color is already present — edit it in place.

Coloring touches **only** the entity and the application — it does **not** go through the model (`IMdlrModel` / NC model), so the same code works for Part and NC documents without any per-context cast. (Contrast with sets / filters, which do need the model — see `sets-builder`.)

## The color int format — get this right or red/blue swap

```
colorRef = (R << 16) | (G << 8) | B      // i.e. 0xRRGGBB, R in the high byte
```

Example: R=255 G=80 B=0 → `(255 << 16) | (80 << 8) | 0` → `0xFF5000`.

This is **NOT** the Win32 `COLORREF` / `RGB()` byte order (`0x00BBGGRR`, R in the *low* byte). If you feed a Win32 `COLORREF` or `System.Drawing.Color.ToArgb()` straight in, red and blue come out swapped — a classic "the color is wrong" symptom. Convert explicitly:

```csharp
// From a System.Drawing.Color:
int colorRef = (c.R << 16) | (c.G << 8) | c.B;
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
        // Pack an RGB triple into Cimatron's color int (0xRRGGBB). NOT Win32 COLORREF.
        public static int Rgb(int r, int g, int b) => (r << 16) | (g << 8) | b;

        // Set the display color of every entity in the list to colorRef (0xRRGGBB).
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
                        var attrSink = (IAttributeSink)entity;

                        // Reuse the entity's existing color attribute if it has one.
                        // GetAttribute THROWS COMException when absent — it does not
                        // return null — so treat the throw as "no existing color".
                        IAttribute attr = null;
                        try { attr = attrSink.GetAttribute(AttributeEnumType.cmAttColor, ""); }
                        catch (COMException) { attr = null; }

                        if (attr != null)
                        {
                            // Already coloured — just set the value in place.
                            attr.Value = colorRef;
                        }
                        else
                        {
                            // None yet — create, set value, then attach.
                            attr = attrFactory.Create(AttributeEnumType.cmAttColor, "");
                            attr.Value = colorRef;
                            attrSink.Attach(attr);
                        }
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
| `GetAttribute` blows up / the whole batch aborts | `GetAttribute` **throws `COMException`** when the entity has no color yet (it does not return null) | Wrap the lookup in `try/catch (COMException)` and treat the throw as "none" |
| New color won't attach | Called `Attach` on an entity that already has a `cmAttColor` (duplicate/refused) | Only `Create`+`Attach` when none exists; when one exists, set `Value` on it in place |
| Color set on a new attribute doesn't show | On the **create** path, `Value` set **after** `Attach` | On the create path, set `Value` **before** `Attach` |
| Red and blue swapped | Passed a Win32 `COLORREF` / `Color.ToArgb()` (`0xBBGGRR`) instead of Cimatron's `0xRRGGBB` | Build the int with `Rgb(r,g,b)` = `(r<<16)|(g<<8)|b` |

Two more, less common:

- **Re-attaching when a color already exists.** Don't detach-and-reattach to change a color — fetch the existing `cmAttColor` and set its `Value`. `Create`+`Attach` is only for entities with no color yet.
- **Color set but not visible until something else redraws.** When you color from inside a Feature Guide's `OnApply`/`OnOk`, Cimatron repaints as the interaction commits, so no manual refresh is needed. If you color from a context that doesn't trigger a repaint and the change doesn't show, force a redraw of the view rather than re-touching the attribute — the attribute is already correct.

## Entity types

`cmAttColor` attaches to any entity that implements `IAttributeSink` — faces, edges, curves, surfaces, bodies. The helper takes `IEnumerable<ICimEntity>`, so it's agnostic; the caller decides what to color. Coloring a body vs. its individual faces is a caller choice, not a helper concern.
