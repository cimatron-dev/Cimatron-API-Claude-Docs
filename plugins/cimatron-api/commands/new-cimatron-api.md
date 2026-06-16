---
description: Scaffold a new Cimatron 2026 API plugin in the current folder with VSCode F5 wired up to build, deploy, launch Cimatron, and attach the managed debugger.
argument-hint: <ApiName> [--menu "<Toolbar Menu Path>"] [--cimatron-root "<C:\\Path\\To\\Cimatron\\Program>"] [--skip-env-check]
---

Scaffold a fresh Cimatron API plugin project alongside the user's current working directory.

Arguments: $ARGUMENTS

## Steps

1. **Parse arguments.**
   - `<ApiName>` is required. Must be a valid C# identifier: starts with a letter, contains only letters/digits/underscores. If invalid, stop and tell the user to pick a different name.
   - Default `--menu` to `"API\n<ApiName>"` if not provided.
   - `--cimatron-root` — if **not** provided, leave it empty for now; Step 1.5 below will pick a value from the installed Cimatron versions. If **provided**, use it verbatim and **do not include a trailing backslash** — PowerShell parses `\"` inside a quoted arg as an escaped quote, so `"...\Program\"` gets passed to `dotnet new` as `...\Program"` (stray quote, no backslash). The template's `Directory.Build.props` normalizes either form, but the no-trailing-backslash default avoids the trap entirely.
   - `--skip-env-check` (optional flag) — skip the prerequisite check in Step 1.5. Use only when the user has already run `/setup-env` and knows their machine is ready, or when running in a noninteractive environment.

1.5. **Run the environment precheck.** Unless `--skip-env-check` was passed, spawn the `cimatron-api:setup-env` agent (with `allow-install: false`) to verify the machine has Git, VSCode, the C# Dev Kit extension, the .NET Framework 4.8 targeting pack, and at least one installed Cimatron ≥ 2024.0. Parse the JSON it returns.

   - **If `allGreen` is `true`:** continue silently.
   - **If anything other than `cimatron` is missing:** stop and tell the user to run `/setup-env` to install the missing pieces. Print the agent's JSON report so they can see exactly what's missing. Don't try to scaffold against a broken toolchain — the user will hit the same wall at build time.
   - **If `cimatron` is missing (zero versions found):** stop and tell the user Cimatron isn't installed at the expected root. They need to either install it or pass `--cimatron-root "<path>"` explicitly.

   Then resolve the Cimatron root from the agent's `cimatron.versions` list:

   - **If `--cimatron-root` was passed explicitly:** use it as-is. Skip the version prompt.
   - **If exactly one Cimatron version is installed:** silently set `--cimatron-root` to `C:\Program Files\Cimatron\Cimatron\<version>\Program` for that version.
   - **If two or more Cimatron versions are installed:** ask the user which one this project should target. Show them the list newest-first (the order the agent returned) and recommend the newest. Set `--cimatron-root` to `C:\Program Files\Cimatron\Cimatron\<chosenVersion>\Program`. Per-project choice — don't try to persist this answer for future runs.

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

6. **Offer to customize the toolbar icon.** Before the final summary, ask the user once (single multiple-choice prompt) how to handle the plugin's icon. The template ships with a placeholder `<ApiName>.ico` (per-plugin filename — generic `icon.ico` would collide in the shared `Program\` folder); this step decides whether to replace it before the user hits F5.

   Options to offer:

   - **Have Claude design one based on the plugin's purpose** (recommended for new plugins). If chosen, ask one follow-up: "Briefly, what does this plugin do?" Build a procedural-design brief from `<ApiName>`, the resolved `<MenuPath>`, and the user's one-line description, then spawn the `cimatron-api:icon-creator` agent with that brief. Explicitly tell the agent to write `<ApiName>.ico` at the project root (matching the template's per-plugin filename and the wired `IconSource` path), draw procedurally with GDI+, and verify the `.ico` magic + 32×32 size.
   - **Use an existing image.** Ask for an absolute path to a PNG/JPG/BMP/GIF or existing `.ico`. Spawn `cimatron-api:icon-creator` in convert mode against that source; pass `<ApiName>.ico` as the output filename so the existing `IconSource` wiring keeps working without a plugin-class edit.
   - **Keep the placeholder.** Leave `<ApiName>.ico` alone; the plugin builds and runs identically. The user can run `cimatron-api:icon-creator` later from inside the project folder to swap it out.

   Default to **keep the placeholder** if the user dismisses or doesn't pick. Don't loop — one ask, one outcome. Don't try to invent a description for the user; if they don't have one, fall back to the letter-fallback mode of icon-creator (1–2 uppercase characters drawn from `<ApiName>`) and warn that the result is a placeholder.

   If the icon-creator agent fails for any reason, don't abort the scaffold — log what failed, leave the placeholder in place, and continue to step 7. A plugin with the default icon is fully functional.

7. **Offer to initialize a git repository.** Invoke the `cimatron-api:git-init` skill against the just-scaffolded project folder. That skill prompts the user once (default **Yes**), and on accept writes a `.gitignore`, runs `git init`, stages the scaffold, and makes a single initial commit. It skips silently if the folder is already inside an existing git repo (e.g. the user scaffolded inside a monorepo).

   Pass the absolute path to `./<ApiName>` as the skill's `<path>` argument so it doesn't depend on the current shell's working directory. Capture the skill's one-line outcome — you'll surface it in step 8.

   If the skill fails for any reason (git missing, commit failed, user has unconfigured `user.email`), **do not abort the scaffold**. The plugin is fully functional un-versioned; surface the failure verbatim in the summary and let the user fix it themselves.

8. **Report what got built.** Print a short summary listing:
   - The created folder
   - The plugin entry-point file (`<ApiName>Plugin.cs`)
   - The default menu path
   - The Cimatron root path that was baked into `.vscode/launch.json`
   - Whether the `ExternalCommands.ini` entry was successfully added (and the line that was added)
   - The icon outcome from step 6 (designed by Claude / converted from `<path>` / kept the placeholder)
   - The git outcome from step 7 (initialized with initial commit `<shortsha>` / skipped — already inside `<toplevel>` / skipped by user / git missing — run `/setup-env`)
   - The next step: "Open `./<ApiName>` in VSCode (running as Administrator — see `.vscode/tasks.json`), close Cimatron, then press F5."

## Prerequisites the user must already have

Step 1.5 above already verifies the first five via the `cimatron-api:setup-env` agent and short-circuits the scaffold if any are missing. The list is reproduced here for the record:

- Git on PATH (Claude Code shells out to `git` for `/plugin marketplace add`, and the optional Step 7 git-init also needs it)
- .NET Framework 4.8 Developer Pack (or .NET SDK with the 4.8 targeting pack)
- Cimatron ≥ 2024.0 installed at `--cimatron-root` (or under `C:\Program Files\Cimatron\Cimatron\<version>\Program\` if not overridden)
- VSCode + the `ms-dotnettools.csdevkit` extension (which brings the `clr` debugger needed to attach to .NET Framework processes)
- VSCode launched as Administrator (the project's build output path is inside `Program Files`) — **not auto-verifiable**, surface to the user before F5.

If any of those is missing, the build/F5 flow will fail. The first four are caught by the precheck; the admin-launch requirement is the one to mention in your final summary so the user doesn't hit it after pressing F5.

## Failure modes

- **Template not installed and `dotnet new install` fails:** print the install command verbatim and stop. Don't try to scaffold from a partial install.
- **ApiName collides with a folder:** stop, do not proceed. Picking a fresh name is cheaper than reasoning about merges.
- **Cimatron root doesn't exist on disk:** warn the user but proceed with scaffolding — the path can be edited in `Directory.Build.props` later.
