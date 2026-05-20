---
name: package-installer
description: Package a built Cimatron API plugin into a single self-elevating `.exe` installer end-users can double-click to deploy the DLL into their Cimatron Program folder and register it in `ExternalCommands.ini`. Same EXE supports `/uninstall` to reverse both steps. Invoked by the `/package-installer` slash command; also runnable standalone when a caller needs the recipe without the orchestration layer.
---

Build a redistributable installer EXE for a Cimatron 2026 plugin. The output is a single net48 console executable that:

1. Self-elevates via a UAC `requireAdministrator` manifest (no manual `Run as administrator`-clicking required by the end-user — Windows shows the consent prompt automatically when they launch the EXE).
2. Detects installed Cimatron versions under `C:\Program Files\Cimatron\Cimatron\<version>\Program\`.
3. Copies the plugin DLL (and `icon.ico`, plus any extra `Payload/` files) into the chosen Cimatron's `Program\` folder.
4. Edits `C:\ProgramData\Cimatron\Cimatron\<version>\Data\ExternalCommands.ini` to add an entry under `[Plugin Ext Commands]` keyed by the plugin's `ICimApiCommandPlugin` class.
5. Reports what it did and exits.

Running the same EXE with the `/uninstall` (or `--uninstall`) argument reverses both filesystem and INI changes.

## Layout

This skill ships three files in `installer-template/`:

```
installer-template/
├── Installer.csproj   — net48 console project, embeds Payload/ as resources
├── Program.cs         — install/uninstall logic, with placeholders for the
│                        per-plugin values (API name, plugin class, etc.)
└── app.manifest       — UAC requireAdministrator manifest
```

The caller (the `/package-installer` slash command) copies these into a temp build folder, fills the placeholders, copies the plugin DLL + icon into a sibling `Payload/` subfolder, and runs `dotnet build`. The output is a single self-contained .exe (no companion DLLs because the only references are BCL).

## Placeholders

Both `Installer.csproj` and `Program.cs` use `@@NAME@@` style placeholders that the caller must substitute before building:

| Placeholder | Lives in | Value |
|---|---|---|
| `@@API_NAME@@` | `Program.cs` | Plugin display name, e.g. `MyTool`. Used in console output and as the default install target subfolder if multi-payload layout is ever added. |
| `@@DLL_NAME@@` | `Program.cs` | Filename of the plugin DLL inside `Payload/`, e.g. `MyTool.dll`. The installer copies every file in `Payload/` verbatim, but this name is logged so the user sees the primary artifact. |
| `@@PLUGIN_CLASS@@` | `Program.cs` | Fully-qualified `<Namespace>.<PluginClass>` of the class implementing `ICimApiCommandPlugin`. This is the INI key. |
| `@@VERSION@@` | `Program.cs` | Plugin version string from `Directory.Build.props` `<Version>`. Printed in the startup banner. |
| `@@TARGET_VERSION@@` | `Program.cs` | Either `any` (accept any installed Cimatron `>= 2024.0`) or a specific version literal like `2026.0`. |
| `@@HAS_ICON@@` | `Program.cs` | `true` or `false` literal. Only affects the user-facing summary — the icon extraction itself is driven by what's actually embedded under `Payload/`. |
| `@@INSTALLER_ASSEMBLY_NAME@@` | `Installer.csproj` | Output EXE name, conventionally `<ApiName>-Installer`. |
| `@@INSTALLER_VERSION@@` | `Installer.csproj` | Embedded `<Version>` element so the produced EXE has a proper Windows file version. |

Substitute these by reading each file, replacing the literal strings, and writing back. The placeholders are deliberately unusual (`@@NAME@@`) to avoid accidental collisions with valid C# or MSBuild tokens.

**Sanitize before substituting.** If `<ApiName>` or `<Namespace>.<PluginClass>` contains a backslash, double-quote, or non-ASCII character, refuse — the value will end up inside a `const string` literal in `Program.cs` and the C# compiler will reject it (or worse, accept it with a meaning the user didn't intend). The slash command's plugin-discovery step should never produce such a name, but guard at this boundary anyway.

## How the installer works at runtime

### Elevation

The `app.manifest` sets `requestedExecutionLevel level="requireAdministrator"`. Windows handles the UAC prompt before the .NET CLR even starts, so no in-code elevation logic is needed. If the user clicks **No** on the UAC prompt, the process simply never spawns — that's a Windows behavior, nothing the installer has to handle.

If UAC is disabled or the user is on a non-interactive account (e.g. an unattended deploy via SCCM), the installer still runs as long as the calling context has Administrator token. It does **not** attempt a soft-fallback to per-user paths — Cimatron only reads from `C:\Program Files\Cimatron\` and `C:\ProgramData\Cimatron\`, both of which are admin-protected.

### Cimatron version detection

The installer enumerates `C:\Program Files\Cimatron\Cimatron\` for subdirectories whose name matches `^\d{4}\.\d+$` and which contain a `Program\` subfolder. It then filters to `>= 2024.0` (or to `@@TARGET_VERSION@@` exactly if a specific version was baked in at packaging time).

If exactly one version matches, it's used silently. If multiple match, the installer prints a numbered list and prompts the user to pick one. If zero match, the installer prints an error and exits with code 2.

It does **not** search alternate drives or custom install roots. If the end-user has a non-default Cimatron location, they can pass `--root "C:\Custom\Path\Cimatron\2026.0\Program"` to override detection (see the `Program.cs` argument parser).

### Payload extraction

Every embedded resource whose logical name starts with `Payload.` gets extracted to `<root>\Program\<filename>` (the manifest name is `Installer.Payload.<filename>` — strip the `Installer.Payload.` prefix at extraction time). This is a flat copy with no subdirectories — the `Payload/` folder is the only layout the installer understands.

Existing files at the destination are **overwritten silently**. This matches the dev `/build` workflow's behavior (build is deploy; the output path overwrites whatever was there). If a previous version of the plugin shipped extra files that the new version doesn't include, those stale files are **not cleaned up** — uninstall handles cleanup only for files the current installer knows about. This is a deliberate tradeoff: the installer can't safely scan and delete arbitrary files in the Cimatron Program folder without risking deletion of unrelated plugins or Cimatron itself.

### INI mutation

The installer opens `C:\ProgramData\Cimatron\Cimatron\<version>\Data\ExternalCommands.ini`, parses it as a line-list, and:

1. If the file doesn't exist, creates it with the canonical skeleton (matching `/register-command`'s skeleton — see that command for the exact text and rationale).
2. Finds or creates the `[Plugin Ext Commands]` section.
3. Looks for an existing line whose key (the part before `=`) equals `@@PLUGIN_CLASS@@`. If found, the line is replaced in place (preserves position). Otherwise, the line is appended at the end of the section.
4. Writes the file back with the same encoding it was read with (UTF-8 BOM is the common case for files Cimatron itself has touched).

The `@1` reload flag is always written for installs. Cimatron auto-flips it to `@0` after the next launch reads the file, so the flag effectively means "force a reload on the very next start".

### Uninstall

`<exe> /uninstall` (or `--uninstall`, case-insensitive) reverses both halves:

1. Removes every file from `<root>\Program\` whose name matches a name in the EXE's `Payload.*` resource list. (The installer doesn't blow away unrelated files — it only deletes what it would have written on install.)
2. Removes the `@@PLUGIN_CLASS@@=...` line from `[Plugin Ext Commands]` in `ExternalCommands.ini`. Other sections, other entries, and the leading comment are preserved exactly.

If the INI file is missing or the entry isn't there, uninstall reports a friendly "nothing to remove" and exits 0 (not an error — re-running uninstall should be idempotent).

If a DLL is locked because `CimatronE.exe` is running, the installer prints which file is locked, tells the user to close Cimatron, and exits with code 3. It does **not** try to `taskkill` Cimatron — the user might have unsaved work.

## Toolbar icon: the sibling-PNG cache that the renderer actually uses

`CimWpfContracts.WpfImageIdentifier` does NOT render the toolbar button from the source `.ico` directly. On first successful icon load Cimatron extracts a 32×32 PNG (`<basename>.png`) next to the source `.ico` in `<CimatronRoot>\Program\` and renders from *that* cache. The `.ico` is the seed; the `.png` is the live render asset.

When the installer overwrites the `.ico` and bumps the INI to `@1`, Cimatron tries to regenerate the cache on the next `AppendCommand()` re-read. That regen has been observed to fail silently on some machines, leaving the toolbar button blank. F5 doesn't hit this because Cimatron's existing cache is still valid and the INI sits at `@0` (cached-state load path).

**Three-layer mitigation** the slash command and this skill apply by default:

1. **Plugin code.** The scaffolder template's `ApiNamePlugin.cs` ships an `EnsureToolbarIconCache` helper that runs in `AppendCommand` before `WpfImageIdentifier` is constructed. It materializes the sibling `.png` from the `.ico` via `System.Drawing.Icon(icoPath, 32, 32).ToBitmap().Save(pngPath, ImageFormat.Png)`. Silently no-ops on write failure (Program Files may be read-only for non-elevated callers) and lets Cimatron's own regen run as before.
2. **Installer Payload.** Step 4 of `/package-installer` auto-copies every `<Content Include="...">` file from the plugin's csproj into the Payload — so when the developer follows the convention of tracking the `<basename>.png` cache as a Content entry (alongside the `.ico`), it ships in the installer automatically and first-launch needs zero I/O on the user's machine.
3. **Icon encoding.** The `.ico` seed must have BMP-encoded frames (see step 3a of the slash command). PNG-in-ICO seeds break both the `WpfImageIdentifier` regen path and `System.Drawing.Icon.ToBitmap`, so the runtime cache-materialization in (1) fails too.

A plugin can opt out of (1) by removing the helper from `AppendCommand`, and out of (2) by not listing the `.png` as Content. The encoding constraint in (3) is non-negotiable for any plugin that uses `WpfImageIdentifier` or `Icon.ToBitmap`.

## Edge cases worth being explicit about

- **Multiple plugins, one shared `icon.ico`:** The Cimatron 2026 plugin template ships every plugin with an `icon.ico` next to the DLL. When two installers both deploy an `icon.ico` to `<root>\Program\`, the second overwrites the first. This matches the dev `/build` behavior (where each F5 deploy overwrites the shared file) and isn't a bug in the installer. If a developer wants per-plugin icons that don't clobber each other, they have to rename their icon at packaging time (`<ApiName>.ico`) and update the `IconSource` line in their `<ApiName>Plugin.cs`. The installer doesn't do that rename automatically — it copies whatever filename it received in `Payload/`. The same rename should be applied to the sibling `<basename>.png` cache so the two stay paired.
- **End-user has only a 2024.0 install but the installer was packaged with `--target-version 2026.0`:** the installer exits with code 2 and prints which version it expected vs what's installed. The fix is to repackage from the dev side with a different `--target-version`, not to teach the installer to downgrade-deploy.
- **End-user runs the installer on a machine without .NET Framework 4.8:** the EXE won't start (Windows will offer to install the framework). This is rare in practice — Cimatron itself requires .NET 4.8, so any machine that has Cimatron has the framework. We don't try to bundle the framework.
- **End-user runs the installer twice in a row:** install is idempotent. The second run overwrites the DLL (same content), the INI line is found and rewritten in place (no duplicates), and the user sees the same success summary. No "already installed" detection — it's cheaper and more reliable to just re-deploy.

## When this skill is the wrong tool

- **The developer wants to deploy to their own dev machine.** Use `/build` — the project's `OutputPath` already points at the Cimatron Program folder, so build *is* deploy on the dev side. The installer is for end-user distribution.
- **The developer wants a Windows MSI with a proper Add/Remove Programs entry, custom branding, and a license dialog.** This skill doesn't produce MSIs. Use WiX or Inno Setup downstream and feed it the `Payload/` folder this skill stages. Don't try to bolt MSI features onto the .exe.
- **The developer wants to deploy multiple plugins together.** Run `/package-installer` once per plugin and ship multiple EXEs. There's no "bundle" mode here — bundling would require renaming icons (see edge cases above) and coordinating two `[Plugin Ext Commands]` entries in one INI mutation, which doubles the surface area of what can go wrong on the end-user's machine for little benefit.

## Pointers

- The INI conventions (skeleton, section ordering, encoding, why `@1` flips to `@0`) are spelled out in `commands/register-command.md`. The installer's INI logic is a direct mirror; if the canonical behavior ever changes, update both.
- The Cimatron version detection logic is borrowed from `skills/setup-env/SKILL.md` step 1 / check 5. Same semantics; same `^\d{4}\.\d+$` filter.
- For the dev iteration loop (build + F5 + attach), see `commands/build.md` and `skills/run-cimatron/SKILL.md`. The installer EXE has none of that — it's strictly for getting a DLL onto a machine that doesn't have the SDK installed.
