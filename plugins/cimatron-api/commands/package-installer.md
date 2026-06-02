---
description: Package the current Cimatron API plugin as a single .exe installer that end-users can double-click to deploy the DLL into their Cimatron Program folder and register it in ExternalCommands.ini.
argument-hint: [--configuration <Debug|Release>] [--out <dir>] [--no-uninstall] [--target-version <2024.0|2025.0|2026.0|any>]
---

Package the Cimatron API plugin in the current working directory into a single self-contained `.exe` installer that an end-user can run on their own machine. The installer bundles the plugin DLL (and the per-plugin `<ApiName>.ico`, with a legacy-`icon.ico` fallback for older projects) as embedded resources, self-elevates via UAC, detects the user's installed Cimatron, copies the DLL into `<CimatronRoot>\Program\`, and writes the `[Plugin Ext Commands]` entry into `ExternalCommands.ini`. Running it again with `/uninstall` reverses both steps.

Arguments: $ARGUMENTS

## When to use this

Plugin developers run this **after** their plugin compiles cleanly and works under F5. The output is a shareable artifact — a single `<ApiName>-Installer-<version>.exe` — that any teammate or customer can run on a machine with Cimatron installed, without needing VSCode, the .NET SDK, admin-elevated shells, or any knowledge of the INI registration step.

**This skill is not for the dev iteration loop.** The dev loop is `/build` + F5; the installer is for distribution.

## Steps

1. **Parse arguments.**
   - `--configuration` defaults to `Release`. End-users get the Release build; do not ship Debug DLLs unless explicitly requested.
   - `--out` defaults to `./dist`. The installer EXE lands at `<out>/<ApiName>-Installer-<version>.exe`.
   - `--no-uninstall` builds an install-only EXE. Default is **with uninstall support** (run the EXE again with `/uninstall` to reverse).
   - `--target-version` controls which Cimatron version the installer is willing to register against. `any` (default) means the installer accepts any installed `>= 2024.0`. A specific value (e.g. `2026.0`) means the installer refuses to deploy unless that exact version is found.

2. **Identify the plugin.** From the current working directory:
   - Find the unique `.csproj` at the project root. If zero or multiple, stop and ask the user to `cd` into the plugin folder.
   - Read `<AssemblyName>` (or fall back to `<RootNamespace>`) → `<ApiName>`.
   - Find the unique class implementing `CimUIInfrastructure.PlugIn.ICimApiCommandPlugin` (same discovery logic as `/register-command`). Capture `<Namespace>.<PluginClass>` — this is what the installer will write into the INI.
   - Read `Directory.Build.props` `<Version>` for the installer EXE's version stamp. If missing, default to `1.0.0`.

   If any of these can't be resolved, stop. Don't ship an installer with placeholder metadata.

3. **Build the plugin (if needed).** Verify the built DLL exists at `<CimatronRootPath>\<ApiName>.dll`. If not, invoke `/build --configuration <configuration>` (or run `dotnet build` directly with the same args). The installer EXE will embed whatever DLL is on disk at this path — make sure it's a fresh, correct build.

   **Also locate `icon.ico`** at the project root. If present, it gets embedded alongside the DLL. If absent, the installer skips icon deployment and the plugin will fall back to whatever `IconSource` resolves to at runtime.

3a. **Pre-flight: icon-encoding vs loader compatibility.** Cimatron has two icon-load paths plus a cache layer the toolbar renders from:

   - **`CimWpfContracts.WpfImageIdentifier`** (the toolbar `IconSource` path) uses WPF/WIC. It reads the **sibling 32×32 `<basename>.png`** that Cimatron caches next to the source `.ico` and renders the toolbar button from *that*. The seed `.ico` must have **BMP-in-ICO** frames so the cache can be (re)generated successfully; PNG-in-ICO seeds fail the regen and leave the toolbar blank under Cimatron's `@1` re-read.
   - **`System.Drawing.Icon.ToBitmap`** (the typical `LoadIconAsBitmap` / `new Icon(path).ToBitmap()` shape used to feed `fg.SetBitmap(Bitmap)`) walks classic `BITMAPINFOHEADER` + XOR/AND payloads and **throws `ArgumentOutOfRangeException`** on PNG-in-ICO. Wants **BMP-in-ICO**.
   - **`Image.FromFile`** (e.g. the template's `PictureLoader.Load` for FG *stage* bitmaps) is GDI+ and tolerates either. Safe.

   F5 development does **not** bump `[Plugin Ext Commands]` from `@0` to `@1`, so a broken toolbar icon is silently masked by Cimatron's cached UI metadata. The installer **does** bump to `@1`, which forces a fresh `AppendCommand()` evaluation on the next Cimatron launch — and that's when the bug appears in front of the end-user. Catch it here, before shipping.

   For each `.ico` referenced by `<Content Include="X.ico">` in the csproj:

   1. **Inspect ICO frame encoding.** For each entry in the ICO directory, look at the first 8 bytes at the frame's data offset. `89 50 4E 47 0D 0A 1A 0A` ⇒ PNG-encoded frame. Anything else ⇒ BMP-encoded frame (classic `BITMAPINFOHEADER`). Bucket each file as `PNG-only`, `BMP-only`, or `mixed`. Pillow is the cleanest tool; the inline Python recipe lives at the bottom of this section.
   2. **Grep the project source for consumers of that file.** Use the icon's filename (no path) and classify each call site:
      - `WpfImageIdentifier(Path.Combine(..., "X.ico"), ...)` ⇒ **WPF/WIC consumer** (wants BMP-in-ICO seed + a sibling `<basename>.png` cache).
      - `new Icon("X.ico"...).ToBitmap()` *or* a helper named `LoadIconAsBitmap`/`IconToBitmap`/similar ⇒ **System.Drawing.Icon consumer** (wants BMP-in-ICO).
      - `Image.FromFile(... "X.ico" ...)` *or* the template's `PictureLoader.Load("X.ico")` ⇒ **GDI+ consumer** (tolerant; ignore).
   3. **Flag mismatches** before doing anything else:
      - **Critical:** an ICO consumed by `WpfImageIdentifier` or `Icon.ToBitmap` / `LoadIconAsBitmap` has **PNG-encoded** frames. WPF/WIC's cache regen leaves the toolbar blank; `Icon.ToBitmap` throws `ArgumentOutOfRangeException` at runtime. Both want **BMP-in-ICO**. Fix: re-encode every frame in that ICO as BMP/DIB. The scaffolder template's `icon.ico` is the reference layout (4 bpp + 8 bpp + 32 bpp, 16×16 and 32×32, all BMP).
      - **Warning:** an ICO consumed by `WpfImageIdentifier` has **no sibling `<basename>.png`** listed in the csproj's `<Content>` items. Cimatron will try to regenerate the cache on the first `@1` re-read, which has been observed to leave the toolbar blank on some machines (ManufacturingPlanning, May 2026). Mitigations (any one suffices):
        - Pre-generate the cache by running the plugin once under F5 and copying `<CimatronRoot>\Program\<basename>.png` back into the project root + csproj as `<Content CopyToOutputDirectory="PreserveNewest">`. Step 4 will then auto-include it in the Payload.
        - Confirm the plugin's `AppendCommand` calls a helper like `EnsureToolbarIconCache` that materializes the cache at runtime via `System.Drawing.Icon(icoPath, 32, 32).ToBitmap().Save(pngPath, ImageFormat.Png)`. The scaffolder template ships this helper by default; a plugin inherited from before the helper was added may need it backfilled.

   If any **Critical** flag fires, stop and surface the findings. **Warning** flags are non-blocking but should be reported in the final report so the developer can decide whether to take a mitigation before shipping.

   Reference snippet for the ICO-encoding inspection:

   ```python
   # Identify per-frame encoding of an .ico
   import struct, sys
   with open(sys.argv[1], 'rb') as f: d = f.read()
   _, _, n = struct.unpack('<HHH', d[:6])
   for i in range(n):
       *_ , size, offs = struct.unpack('<BBBBHHII', d[6+i*16:6+(i+1)*16])
       print('PNG' if d[offs:offs+8] == b'\x89PNG\r\n\x1a\n' else 'BMP')
   ```

4. **Stage the installer build folder.** Create a clean temp folder at `<plugin>/obj/installer/` (delete and recreate if present — stale state from a previous run is the most common failure mode). Copy these files from the `package-installer` skill's `installer-template/` directory into the temp folder:
   - `Installer.csproj`
   - `Program.cs`
   - `app.manifest`

   Then copy the plugin's built artifacts into the same temp folder:
   - `<CimatronRootPath>\<ApiName>.dll` → `<temp>/Payload/<ApiName>.dll`
   - **Every `<Content Include="...">` file in the plugin's `.csproj`** whose path resolves to an existing file under `<plugin>/` → `<temp>/Payload/<filename>`. This catches every plugin asset the project explicitly ships: the toolbar `.ico`, the FG bitmap `.ico`, any sibling `<basename>.png` cache files for `WpfImageIdentifier`, plus any other config files (`appsettings.json`, etc.). Flatten paths — the installer extracts flat into `<CimatronRoot>\Program\`, so subdirectories aren't supported. If two Content entries resolve to the same filename, stop and surface the conflict.
   - For backward compatibility, also include `<plugin>/icon.ico` if it exists and isn't already covered by a Content entry.

   The `<EmbeddedResource>` glob in `Installer.csproj` picks up everything under `Payload/` automatically, so any extra files the developer drops into `<plugin>/Payload/` next to their csproj (without csproj entries) will also be picked up if they exist at copy time.

5. **Template the installer source.** In the copied `Program.cs`, replace the placeholders:
   - `@@API_NAME@@` → `<ApiName>` (e.g. `MyTool`)
   - `@@DLL_NAME@@` → `<ApiName>.dll`
   - `@@PLUGIN_CLASS@@` → `<Namespace>.<PluginClass>` (the `ICimApiCommandPlugin` class, used as the INI key)
   - `@@VERSION@@` → the version from step 2 (used in the installer's startup banner)
   - `@@TARGET_VERSION@@` → the value of `--target-version` (default `any`)
   - `@@HAS_ICON@@` → `true` if **any** `.ico` ended up in `<temp>/Payload/` (from a Content entry or the legacy `icon.ico` fallback), `false` otherwise. Used only for the end-user summary banner; the actual extraction is driven by what's embedded.

   In `Installer.csproj`, replace:
   - `@@INSTALLER_ASSEMBLY_NAME@@` → `<ApiName>-Installer`
   - `@@INSTALLER_VERSION@@` → the version from step 2

   Refuse to write any placeholder back to disk if `<ApiName>` or `<Namespace>.<PluginClass>` would contain characters illegal in a C# string literal (i.e. an unescaped backslash or quote). If that happens, something is wrong with the plugin discovery in step 2 — stop and surface what was found.

6. **Build the installer.** From the temp folder:

   ```powershell
   dotnet build Installer.csproj --configuration Release --nologo
   ```

   This produces a single `.exe` at `<temp>/bin/Release/<ApiName>-Installer.exe`. .NET Framework 4.8 console builds are not "self-contained" in the .NET-Core sense — the EXE just assumes net48 is present on the target machine, which is already a requirement for Cimatron itself. So the artifact is a single file, no runtime bundling required, typically under 50 KB.

   If the build fails, the failure is almost always in step 5 (a bad placeholder substitution). Read the compiler errors, surface them, and stop — don't ship a broken installer.

7. **Stage the final artifact.** Copy `<temp>/bin/Release/<ApiName>-Installer.exe` to `<out>/<ApiName>-Installer-<version>.exe`. Create `<out>/` if it doesn't exist.

   Optionally also write a tiny `<out>/README.txt` with one-liner usage instructions for the end-user:

   ```
   <ApiName> installer
   ===================

   1. Close Cimatron if it's running.
   2. Right-click <ApiName>-Installer-<version>.exe → Run as administrator.
   3. The installer copies <ApiName>.dll into the Cimatron Program folder
      and registers it in ExternalCommands.ini.
   4. Launch Cimatron. The new command appears in the "<menu path>" toolbar.

   To uninstall: run the same EXE again with the /uninstall flag from an
   elevated terminal:  <ApiName>-Installer-<version>.exe /uninstall
   ```

   The README is optional — if the user passed `--no-uninstall` or asked for a leaner output, skip it.

8. **Drop the `deploy.ps1` bootstrap into the plugin root (first run only).** Check the plugin root for `deploy.ps1`. If it exists, leave it alone — the developer may have customized it. If it doesn't:
   - Copy `plugins/cimatron-api/skills/package-installer/deploy-template/deploy.ps1` to `<plugin>/deploy.ps1`.
   - Copy `plugins/cimatron-api/skills/package-installer/deploy-template/deploy.cmd` to `<plugin>/deploy.cmd`.

   These give the developer a `./deploy` entry point they can re-run **without invoking Claude Code**: the script caches the installer template under `<plugin>/.tools/package-installer/`, downloads it on first run (and on `-Update`), then calls `build-installer.ps1` to do the full plugin-discovery + DLL build + installer build flow this slash command does. After this step the developer can iterate purely via `./deploy` (or `./deploy.ps1 -Configuration Debug`, `-TargetVersion 2026.0`, `-Update`, etc.).

   Mention the new files in the report below so the developer knows they're there.

9. **Report.** Print:
   - The path to the installer EXE.
   - Its size (sanity check — anything under a few MB is normal; anything bigger suggests the embedded DLL has unwanted dependencies).
   - The plugin class key that will be written to the INI.
   - The default install target (`any version >= 2024.0`, or the specific version if `--target-version` was set).
   - A one-line reminder: **the end-user must close Cimatron and run the EXE as Administrator.** The UAC manifest will request elevation automatically; if the user is on a managed machine where UAC is disabled, the install will fail with "access denied" on the Program folder.
   - If `deploy.ps1` was just dropped in by step 8, also mention: **from now on the developer can re-run `./deploy` (or `./deploy.ps1 -Update` to refresh the cached template) without re-invoking this slash command.**

## Failure modes

- **No `*.csproj` at the current directory root, or multiple csprojs:** stop. The skill operates on one plugin at a time. Ask the user to `cd` into the specific plugin folder.
- **No class implementing `ICimApiCommandPlugin` discovered:** stop. The project isn't a Cimatron 2026 plugin in the expected shape. Don't ship an installer that registers nothing.
- **Plugin DLL doesn't exist at `<CimatronRootPath>\<ApiName>.dll`:** stop and tell the user to run `/build` first. The installer has nothing to embed.
- **`dotnet build Installer.csproj` fails:** the placeholder substitution in step 5 was wrong. Surface the compiler errors and stop. Don't ship a broken installer.
- **`<out>` directory already contains an older installer EXE for the same `<ApiName>` and version:** overwrite it. The version stamp is the source of truth; if the user wants to keep older builds they should bump `<Version>` in `Directory.Build.props` first.

## Things this skill does **not** do

- **It does not sign the installer.** The output `.exe` is unsigned. End-users will see a SmartScreen warning on first launch. Code signing requires a certificate, the user's own signing pipeline, and is out of scope. If the user wants signing, they `signtool sign` the artifact themselves after this skill produces it.
- **It does not push the installer anywhere.** No GitHub release, no shared drive, no UNC copy. The artifact lands in `<out>/`; the user distributes it however they distribute things.
- **It does not bundle the .NET Framework runtime.** End-users are expected to have .NET Framework 4.8 already, which is a hard requirement for Cimatron itself. If they can run Cimatron, they can run the installer.
- **It does not handle multi-DLL plugins** (a single plugin DLL that depends on other private assemblies the developer ships alongside). For that, drop the additional DLLs into a `<plugin>/Payload/` folder before running this skill — see step 4. The skill's `<EmbeddedResource>` glob will pick them up, and the installer extracts the whole `Payload/` contents into `<CimatronRoot>\Program\` at install time.

## Internals

The detailed installer source, csproj, UAC manifest, and end-user runtime behavior live in the `package-installer` skill (`plugins/cimatron-api/skills/package-installer/SKILL.md`). This command is the orchestration layer; the skill is the recipe.
