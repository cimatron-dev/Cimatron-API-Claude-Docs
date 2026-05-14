---
description: Remove a Cimatron API plugin from ExternalCommands.ini so Cimatron stops loading it.
argument-hint: [<Namespace.PluginClass>] [--ini-path "<path>"]
---

Unregister a Cimatron 2026 plugin by removing its entry from `ExternalCommands.ini`. Inverse of `/register-command`.

Arguments: $ARGUMENTS

## Steps

1. **Parse arguments.**
   - `<Namespace.PluginClass>` is optional. If omitted, discover it from the current working directory the same way `/register-command` does: find the unique class implementing `CimUIInfrastructure.PlugIn.ICimApiCommandPlugin` and use `<Namespace>.<ClassName>`.
   - `--ini-path` defaults to `C:\ProgramData\Cimatron\Cimatron\2026.0\Data\ExternalCommands.ini`.

   **The key is always the `ICimApiCommandPlugin` class**, not the `ICimWpfCommand` class. Even when cleaning up after a buggy registration that used the wrong class, the *current* correct unregister is by Plugin class name. See step 3 for cleanup of legacy wrong-class entries.

2. **Read the INI.** If the file is missing, stop with a friendly message — there's nothing to remove. Don't create the file just to fail to find an entry in it.

3. **Find and remove the entry.**
   - Scan `[Plugin Ext Commands]` for a line whose key matches `<Namespace>.<PluginClass>`.
   - If found, remove the entire line (do not leave a blank where it was; collapse it). Print the removed line so the user can verify.
   - **Also check for a stale legacy line** where the key is `<Namespace>.<CommandClass>` (the `ICimWpfCommand` class — registered by an older buggy version of `/register-command`). If found, remove that too and call it out in the output so the user understands what cleanup happened.
   - If neither is found, print a message saying so and stop — do not write the file unchanged.

4. **Preserve everything else.** Same rules as `/register-command`: leave comments, other sections, blank lines, and `[Global Flags]` exactly as they were. Same encoding as the existing file.

5. **Report.** Print:
   - The INI path that was modified.
   - The exact line(s) that were removed.
   - A reminder: **Cimatron must be fully closed and reopened** before the command disappears from the UI. The INI is only read at startup.
   - Note that the DLL is **not** removed — it still sits in the Cimatron Program folder. If the user wants the DLL gone, suggest `dotnet clean` after closing Cimatron, or deleting `<CimatronRootPath>\<ApiName>.dll` manually.

## When to use this

- Before renaming the plugin class — register the new name, unregister the old.
- When uninstalling a plugin entirely — pair this with deleting the DLL from the Cimatron Program folder.
- To clean up a stale wrong-class entry left over from an older buggy registration (the legacy `<Namespace>.<CommandClass>=...` shape).
- **Not** when temporarily disabling a command. For that, edit the plugin's `OnCommandUI` to return `CommandUIState.Disabled` or `Hidden` — far cheaper than re-registering.

## Failure modes

- **Entry not found:** stop, don't write the file. Tell the user what was searched for so they can spot a typo.
- **INI file missing:** stop, friendly message. Not an error.
- **Write fails with access denied:** stop and surface the Administrator requirement, same as `/register-command`.
