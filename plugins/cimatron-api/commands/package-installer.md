---
description: Package the current Cimatron API plugin as a single .exe installer that end-users can double-click to deploy the DLL into their Cimatron Program folder and register it in ExternalCommands.ini.
argument-hint: [--configuration <Debug|Release>] [--out <dir>] [--no-uninstall] [--target-version <2024.0|2025.0|2026.0|any>]
---

Package the Cimatron API plugin in the current working directory into a single self-contained `.exe` installer that an end-user can run on their own machine. The installer bundles the plugin DLL (and `icon.ico`) as embedded resources, self-elevates via UAC, detects the user's installed Cimatron, copies the DLL into `<CimatronRoot>\Program\`, and writes the `[Plugin Ext Commands]` entry into `ExternalCommands.ini`. Running it again with `/uninstall` reverses both steps.

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

4. **Stage the installer build folder.** Create a clean temp folder at `<plugin>/obj/installer/` (delete and recreate if present — stale state from a previous run is the most common failure mode). Copy these files from the `package-installer` skill's `installer-template/` directory into the temp folder:
   - `Installer.csproj`
   - `Program.cs`
   - `app.manifest`

   Then copy the plugin's built artifacts into the same temp folder:
   - `<CimatronRootPath>\<ApiName>.dll` → `<temp>/Payload/<ApiName>.dll`
   - `<plugin>/icon.ico` → `<temp>/Payload/icon.ico` (only if it exists)

   The `<EmbeddedResource>` glob in `Installer.csproj` picks up everything under `Payload/` automatically, so any extra files (additional `.ico`s, an `appsettings.json`, etc.) the developer drops into `<plugin>/Payload/` next to their csproj will also be picked up if they exist at copy time.

5. **Template the installer source.** In the copied `Program.cs`, replace the placeholders:
   - `@@API_NAME@@` → `<ApiName>` (e.g. `MyTool`)
   - `@@DLL_NAME@@` → `<ApiName>.dll`
   - `@@PLUGIN_CLASS@@` → `<Namespace>.<PluginClass>` (the `ICimApiCommandPlugin` class, used as the INI key)
   - `@@VERSION@@` → the version from step 2 (used in the installer's startup banner)
   - `@@TARGET_VERSION@@` → the value of `--target-version` (default `any`)
   - `@@HAS_ICON@@` → `true` if `icon.ico` was copied in step 4, `false` otherwise

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

8. **Report.** Print:
   - The path to the installer EXE.
   - Its size (sanity check — anything under a few MB is normal; anything bigger suggests the embedded DLL has unwanted dependencies).
   - The plugin class key that will be written to the INI.
   - The default install target (`any version >= 2024.0`, or the specific version if `--target-version` was set).
   - A one-line reminder: **the end-user must close Cimatron and run the EXE as Administrator.** The UAC manifest will request elevation automatically; if the user is on a managed machine where UAC is disabled, the install will fail with "access denied" on the Program folder.

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
