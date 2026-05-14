---
description: Scaffold a new Cimatron 2026 API plugin in the current folder with VSCode F5 wired up to build, deploy, launch Cimatron, and attach the managed debugger.
argument-hint: <ApiName> [--menu "<Toolbar Menu Path>"] [--cimatron-root "<C:\\Path\\To\\Cimatron\\Program>"]
---

Scaffold a fresh Cimatron API plugin project alongside the user's current working directory.

Arguments: $ARGUMENTS

## Steps

1. **Parse arguments.**
   - `<ApiName>` is required. Must be a valid C# identifier: starts with a letter, contains only letters/digits/underscores. If invalid, stop and tell the user to pick a different name.
   - Default `--menu` to `"APIs\n<ApiName>"` if not provided.
   - Default `--cimatron-root` to `C:\Program Files\Cimatron\Cimatron\2026.0\Program` if not provided. **Do not include a trailing backslash** — PowerShell parses `\"` inside a quoted arg as an escaped quote, so `"...\Program\"` gets passed to `dotnet new` as `...\Program"` (stray quote, no backslash). The template's `Directory.Build.props` normalizes either form, but the no-trailing-backslash default avoids the trap entirely.

2. **Refuse to overwrite.** Check whether `./<ApiName>` already exists in the current working directory. If it does, stop and tell the user to pick a different name or `rm` the existing folder.

3. **Ensure the dotnet new template is installed.** Run:

   ```powershell
   dotnet new list cimatron-api
   ```

   If no template is listed, install the bundled template from this plugin:

   ```powershell
   dotnet new install "${CLAUDE_PLUGIN_ROOT}\template"
   ```

   `${CLAUDE_PLUGIN_ROOT}` resolves to this plugin's directory at runtime.

4. **Scaffold the project.** From the user's current working directory, run:

   ```powershell
   dotnet new cimatron-api -n <ApiName> --menuPath "<MenuPath>" --cimatronRoot "<CimatronRoot>"
   ```

   The template uses `sourceName: "ApiName"`, so all occurrences of `ApiName` in filenames and file content are replaced with the supplied name. `menuPath` and `cimatronRoot` are template parameters that override the defaults baked into the project.

5. **Register the plugin in `ExternalCommands.ini`.** A fresh DLL in the Cimatron Program folder is **not enough** — Cimatron only loads plugins listed under `[Plugin Ext Commands]` in `C:\ProgramData\Cimatron\Cimatron\2026.0\Data\ExternalCommands.ini`. Invoke the `/register-command` slash command from inside the new project folder (or apply its logic directly): pass `<ApiName>.<ApiName>Plugin` (the class implementing `ICimApiCommandPlugin`, **not** the `<ApiName>Command` `ICimWpfCommand` class) so the entry `<ApiName>.<ApiName>Plugin=<ApiName>.<ApiName>Plugin@1` is added.

   Pointing the INI key at the wrong class throws `InvalidCastException` from `CimUIInfrastructure.dll` at plugin load — Cimatron casts the instantiated type to `ICimApiCommandPlugin` and the cast fails. The `*Plugin` class is the one that has `AppendCommand()`; that's the right one.

   If registration fails with access denied, do not abort the scaffold — print the exact line that needs to be added and tell the user to add it manually (or relaunch elevated and rerun `/register-command`). The project is fine; only the registration step needs admin.

6. **Report what got built.** Print a short summary listing:
   - The created folder
   - The plugin entry-point file (`<ApiName>Plugin.cs`)
   - The default menu path
   - The Cimatron root path that was baked into `.vscode/launch.json`
   - Whether the `ExternalCommands.ini` entry was successfully added (and the line that was added)
   - The next step: "Open `./<ApiName>` in VSCode (running as Administrator — see `.vscode/tasks.json`), close Cimatron, then press F5."

7. **Do not commit anything for the user.** The scaffolded folder is a fresh project; let them decide how to version-control it.

## Prerequisites the user must already have

- .NET Framework 4.8 Developer Pack (or .NET SDK with the 4.8 targeting pack)
- Cimatron 2026 installed at `--cimatron-root`
- VSCode + the `ms-dotnettools.csdevkit` extension (which brings the `clr` debugger needed to attach to .NET Framework processes)
- VSCode launched as Administrator (the project's build output path is inside `Program Files`)

If any of those is missing the build/F5 flow will fail. Surface that to the user before they press F5, not after.

## Failure modes

- **Template not installed and `dotnet new install` fails:** print the install command verbatim and stop. Don't try to scaffold from a partial install.
- **ApiName collides with a folder:** stop, do not proceed. Picking a fresh name is cheaper than reasoning about merges.
- **Cimatron root doesn't exist on disk:** warn the user but proceed with scaffolding — the path can be edited in `Directory.Build.props` later.
