---
name: drive-cimatron-com
description: >-
  Connect to and drive a running or new Cimatron from PowerShell out-of-process
  via REGISTRATION-FREE COM (a Side-by-Side activation context) - no
  CadCimAiShell, no COM registration, no Administrator. Use to open the app,
  create/open a Part, run modeler procedures (sketch, extrude, revolve,
  boolean, ...), enumerate entities, measure (volume/area/center), save, and
  render a picture headlessly. This is the replacement for the old CadCimAiShell
  harness - reach for it whenever you need to script Cimatron, especially when
  CadCimAiShell is missing/broken or the Cimatron COM server isn't registered.
  Drives the MODELER API; it does NOT click a plugin's toolbar command (see end).
---

# Drive Cimatron headlessly via registration-free COM

`bootstrap.ps1` (next to this file) connects PowerShell to Cimatron with **no
COM registration and no `CadCimAiShell`**. It activates a Side-by-Side context
over `CimAppAccess.dll`'s embedded manifest so the `AppAccess` coclass can be
created without being in the registry, loads the .NET interop PIAs, then attaches
to a running Cimatron or cold-launches one. After dot-sourcing it, **`$app` and
`$pdm` are in scope**, plus a **`Stop-Cimatron`** function (see the close rule).

## Connect (and always close)

```powershell
. "$PSScriptRoot\bootstrap.ps1"     # -> $app (IApplication), $pdm (IPdm), Stop-Cimatron
try {
    # ... your work ...
}
finally {
    Stop-Cimatron                   # closes ONLY an instance the bootstrap launched
}
```

Cold launch (no Cimatron running) takes **~2-3 minutes** - set a generous tool
timeout. Do all your work in **one** PowerShell invocation; shell state does not
persist between calls.

**The close rule is not optional.** A Cimatron launched purely for COM automation
does **not** exit when your script ends - it lingers with an unresponsive window
and **hangs the user's desktop**. `Stop-Cimatron` (in a `finally`) kills only the
instance the bootstrap launched (never a pre-existing user session). And **do not
run COM-driving unattended in the background** - keep it foreground so a stuck
launch is visible and the `finally` always runs.

## Drive the modeler - late-bound idioms (READ THIS, they are non-obvious)

Everything is **late-bound** (`System.__ComObject` via IDispatch). The proven
patterns:

- **Create a part:** `$doc = $pdm.CreateDocument($path, 1, 0)` - docType
  `cmPart=1`, unit `cmMillimeter=0`. Then `$model = $doc.Model`.
- **Procedures:** `$p = $model.CreateProcedure([int][interop.CimMdlrAPI.MdProcedureType]::cmExtrudeProcedure)`.
  Use the typed enum cast (sketch `cmSketcherProcedure`, extrude `cmExtrudeProcedure`,
  revolve `cmRevolveProcedure`, merge `cmMergeProcedure`, round `cmRoundProcedure`, ...).
- **`Execute_()` has a trailing underscore.** It's `$p.Execute_()`, not `Execute()`.
- **Entity type/id are `.Type_` and `.ID_`** (trailing underscore): `[int]$e.Type_`,
  `[int]$e.ID_`. `cmBody=2`, `cmFace=3`, `cmEdge=4`.
- **No `GetEntities`.** Enumerate by scanning ids:
  `for ($i=1; $i -le 500; $i++) { $e=$null; try { $e=$model.GetEntityById($i,$model) } catch {} ; ... }`,
  keeping the highest-id match ("NewestBody").
- **A sketched `AddBox` profile is a `cmBody`** - feed that body as the extrude `Contour`.
- **Enum values:** cast the typed enum, e.g.
  `$ex.Mode = [int][interop.CimMdlrAPI.ExtrudeSweepMode]::cmExtrudeSweepModeNew`.
- **Save / render:** `$doc.Save()`, `$doc.SavePicture2($png, 1, 1.0)`.

### Getting an ICimEntityList (for merge / volume / area / center)

You **cannot** `New-Object` a `CimEntityList` out-of-process (the coclass ctor
fails), but you do not need to - **obtain a list from the model and add to it**:

1. **Pick the entities** with the `IEntityQuery` on `$model` (underscore methods):
   ```powershell
   $f = $model.CreateFilter_([int][interop.CimBaseAPI.EFilterEnumType]::cmFilterEntityType)
   $f.Add([int][interop.CimBaseAPI.EntityEnumType]::cmBody)   # add the type(s) you want
   $model.SetFilter_($f)
   $found = $model.Select_()        # NOTE: returns an Object[] of entities, NOT a list
   ```
2. **Get a real `ICimEntityList` container and fill it.** `$model.ModelProcedures`
   returns a live, mutable `ICimEntityList` you can `.Add()` to. The service/modeler
   calls that consume the list ignore non-body members, so the extra procedures it
   already holds are harmless:
   ```powershell
   $list = $model.ModelProcedures            # a real ICimEntityList (do NOT wrap this in a function - see gotcha)
   foreach ($e in $found) { [void]$list.Add($e) }
   $svc = $model.GetGeomServicesObj()
   $vol = $svc.BodiesVolume($list)           # works late-bound (verified: a 100x60x40 box -> 240000)
   $ctr = $svc.BodiesCenter($list)           # double[3]
   ```
   For a boolean: `$m=$model.CreateProcedure([int][...]::cmMergeProcedure); $m.Entities=$list; $m.Execute_()`.

**Gotcha:** **never return a COM list from a PowerShell function.** PS enumerates an
enumerable COM object on return and hands back an `Object[]`, which the list-typed
parameters then reject ("Cannot convert Object[] to Object"). Build and use the
list **inline** (or `Write-Output -NoEnumerate`).

## What still does NOT work out-of-process

- **Strongly-typed QI to a specific sub-interface.** Casting a live COM object to a
  named interface throws `REGDB_E_IIDNOTREG` (e.g. `(ICimDocument)`), and members
  only reachable via a sub-interface read as defaults - e.g. **single-body**
  `IGeom3DBody.Volume` (off `entity.Geometry`) reads 0. **Prefer the list-based
  `IGeomServices` calls** (`BodiesVolume`/`BodiesCenter`/`BodiesSurfArea`), which DO
  work, over single-entity typed-interface members.
- **`GetActiveApplication()` throws "Class not registered"** when nothing is running
  (registry-based; SxS doesn't cover it). The bootstrap handles this - it falls
  through to `GetApplication()` to launch.

These typed-QI limits do not exist **in-process** (inside a loaded plugin), where
`new CimEntityList()` and typed casts are fine.

## Why this over CadCimAiShell

- **Robust:** no dependency on `CadCimAiShell.exe` (which can break - e.g. a
  signed/unsigned `CadCimShell.dll` mismatch) and no need to register Cimatron's
  COM server or run elevated.
- **Self-contained:** one dot-source and you're driving the modeler.

**Caveat:** this drives the **modeler API** (build/inspect/measure/render geometry).
It does **not** click a plugin's `ICimWpfCommand` toolbar command - there's no COM
"run command by name" used here. For command-level validation, run the command
in-process inside Cimatron. To validate a plugin's *logic*, replicate its Cimatron
API calls here (they're the same procedures), which is exactly what the idioms above
let you do.
