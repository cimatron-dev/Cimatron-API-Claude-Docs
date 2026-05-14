---
description: Add a new ApiCommand to a Cimatron API plugin project. In practice this means scaffolding a sibling plugin DLL, since Cimatron 2026 is one-command-per-plugin.
argument-hint: <NewCommandName> [--menu "<Toolbar Menu Path>"] [--caption "<Caption>"] [--tooltip "<Tooltip>"] [--icon "<file>"]
---

Add a new Cimatron API command alongside the plugin in the current working directory.

Arguments: $ARGUMENTS

## Reality check first

Every working Cimatron 2026 plugin observed so far has the same shape:

- One class implementing `CimUIInfrastructure.PlugIn.ICimApiCommandPlugin` per DLL.
- That class has **`AppendCommand()` singular**, returning **one** `ApiCommand`.
- The DLL gets **one** entry in `ExternalCommands.ini` under `[Plugin Ext Commands]`, keyed by the Plugin class.

There is no verified working example of `AppendCommands()` plural / multiple `ApiCommand` returns from a single plugin DLL in Cimatron 2026. Earlier versions of this skill had a "Shape B" branch that rewrote `AppendCommand()` into a list-returning `AppendCommands()`. That branch produced code that compiled but never appeared in the UI — the interface contract in Cimatron 2026 is singular.

**Default behavior of this skill: scaffold a sibling plugin via `/new-cimatron-api`.** That's the path Cimatron 2026 actually supports. The new plugin will be a separate DLL with its own Plugin class, its own Command class, its own INI entry, and its own toolbar button.

If the user insists `AppendCommands()` plural works in their installation, ask them to share a working example (a plugin where multiple buttons appear in the toolbar from a single DLL). Until that's confirmed, don't rewrite their `AppendCommand()` into the list shape — you'll just break their working plugin.

## Steps (sibling-plugin path)

1. **Parse arguments.**
   - `<NewCommandName>` is required. Must be a valid C# identifier. If invalid, stop.
   - `--menu`, `--caption`, `--tooltip`, `--icon` are passed through to `/new-cimatron-api`.

2. **Locate the existing plugin folder** (current working directory). Read its `<ApiName>Plugin.cs` (or equivalent) and capture:
   - The current `MenuPath` (to default the new plugin's menu to the same toolbar group).
   - The Cimatron root path from `Directory.Build.props` so the new plugin builds into the same Program folder.

3. **Scaffold the sibling plugin.** Change to the **parent** of the current plugin folder, then invoke `/new-cimatron-api` with:
   - `<ApiName>` = `<NewCommandName>` (the new plugin's name).
   - `--menu` = the existing plugin's menu group (so both buttons land in the same toolbar), with the leaf entry replaced by `<NewCommandName>`.
   - `--cimatron-root` = the same root the existing plugin uses.

   `/new-cimatron-api` handles its own INI registration (it writes `<NewCommandName>.<NewCommandName>Plugin=<NewCommandName>.<NewCommandName>Plugin@1` under `[Plugin Ext Commands]`).

4. **Report.** Print:
   - The path to the new plugin folder.
   - The menu path the new button will use (so the user can see both old and new buttons sit together).
   - A note that the new plugin is a separate DLL: it builds independently, has its own version, and can be removed via `/unregister-command` + deleting its DLL without affecting the existing one.
   - The next step: open the new folder in VSCode, close Cimatron, press F5.

## When this skill is the wrong tool

- **You want to change what an existing button does.** That's an edit to the existing `OnCommand` method in the Command class — don't run this skill.
- **You want to change the menu path, caption, or icon of an existing button.** Edit the `ApiCommand` initialization in the existing Plugin class and bump the INI reload flag to `@1` via `/register-command`.
- **You're trying to share code between two commands.** Put the shared code in a helper class or a shared library project the two plugin DLLs both reference; don't try to bundle two commands into one DLL.

## Failure modes

- **`<NewCommandName>` collides with the existing plugin name or an existing folder:** stop. Pick a different name.
- **The current directory isn't a Cimatron API plugin:** stop. Run `/new-cimatron-api` instead — there's nothing to "add to".
- **User explicitly requests the in-DLL `AppendCommands()` path:** confirm with them that they have a verified working example. If they don't, refuse and redirect to the sibling-plugin path. If they do, ask them to share it so this skill can be updated.
