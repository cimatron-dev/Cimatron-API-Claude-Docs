---
description: Register a Cimatron API plugin in ExternalCommands.ini so Cimatron exposes its command in the UI on next launch.
argument-hint: [<Namespace.PluginClass>] [--no-reload] [--ini-path "<path>"]
---

Register a Cimatron 2026 plugin by adding (or updating) an entry in `ExternalCommands.ini`. The INI is what makes the plugin DLL visible to Cimatron — without it, the DLL sits in the Program folder but never loads.

Arguments: $ARGUMENTS

## Background — what goes in the INI

```ini
[Plugin Ext Commands]
<Namespace>.<PluginClass>=<Namespace>.<PluginClass>@<ReloadFlag>
```

**The key MUST be the class that implements `CimUIInfrastructure.PlugIn.ICimApiCommandPlugin`** (the class with `AppendCommand()` returning an `ApiCommand`). **NOT** the class that implements `CimUIInfrastructure.Commands.ICimWpfCommand` (the class with `OnCommand`, `OnCommandUI`, etc.).

Cimatron reads the key, instantiates that type via reflection, and casts it to `ICimApiCommandPlugin`. Pointing at the wrong class throws `System.InvalidCastException` inside `CimUIInfrastructure.dll` at load time — symptom is "plugin loaded but the command never appears in the toolbar".

`<ReloadFlag>` is `1` to make Cimatron re-read the command's properties (caption, menu path, icon, etc.) on next startup, or `0` to skip. Default to `1` — Cimatron auto-flips it back to `0` after the reload, so you can think of `1` as "force one reload now". Use `--no-reload` to explicitly request `0`.

## Steps

1. **Parse arguments.**
   - `<Namespace.PluginClass>` is optional. If omitted, discover it from the current working directory (see step 2).
   - `--no-reload` writes `@0` instead of the default `@1`.
   - `--ini-path` defaults to `C:\ProgramData\Cimatron\Cimatron\2026.0\Data\ExternalCommands.ini`.

2. **Discover the plugin class (if not provided).** Find the unique `.cs` file in the project that declares a class implementing `CimUIInfrastructure.PlugIn.ICimApiCommandPlugin`. The fastest way is to grep the project for `: CimUIInfrastructure.PlugIn.ICimApiCommandPlugin` or the unqualified `: ICimApiCommandPlugin` with the matching `using`. Read the top-level `namespace` declaration and the class name. The result is `<Namespace>.<ClassName>`.

   Common class-name patterns observed in working plugins:
   - `<ApiName>Plugin` (this is what `/new-cimatron-api` scaffolds)
   - `<ApiName>PluginCommand` (older convention, e.g. `TrimAnglePluginCommand`)

   If exactly one such class exists, register it. If zero exist, stop — the project isn't a Cimatron API plugin. If more than one exists, stop and ask the user which class to register; Cimatron only loads one `ICimApiCommandPlugin` per DLL.

   **Do not** register the `ICimWpfCommand`-implementing class. That class is referenced *from* the plugin class (as `ApiCommand.ExecuteCommand = new SomeCommand()`); it never goes in the INI.

3. **Read the INI.** Use the Read tool. If the file doesn't exist, create it with this skeleton (preserve the leading comment so the file is recognizable to Cimatron's loader):

   ```ini
   ;ExternalCommands.ini

   [Global Flags]
   ResetApiCommands =1

   [COM Ext Commands]

   [Plugin Ext Commands]

   [External Pane]
   ```

   The file lives under `ProgramData` and requires Administrator write access. If the write fails with access-denied, stop and tell the user to relaunch their shell (or VSCode) as Administrator. Do not silently fall back to a per-user path — Cimatron only reads the system path.

4. **Locate or create the `[Plugin Ext Commands]` section.** If the section header is missing entirely, append it before `[External Pane]` (or at end of file if `[External Pane]` is absent).

5. **Add or update the entry.**
   - Build the line: `<Namespace>.<PluginClass>=<Namespace>.<PluginClass>@<ReloadFlag>` (`<ReloadFlag>` is `1` unless `--no-reload`).
   - If a line with the same key already exists in `[Plugin Ext Commands]`, replace it in place (preserves the entry's position in the section).
   - If a stale line exists with a wrong-class key (e.g. `<Namespace>.<SomethingCommand>=...` from a buggy earlier registration), remove that stale line **as well** so the plugin isn't double-registered. Print both the removed and the added line so the user can sanity-check.
   - Otherwise, append the line at the end of the `[Plugin Ext Commands]` section, before the next section header or end of file.

6. **Preserve everything else.** Leave the leading comment, every other section, every other key, blank lines, and `ResetApiCommands` exactly as they were. Do not reformat, do not sort, do not strip trailing whitespace. Write the file back with the same encoding the existing file uses (UTF-8 BOM is common for files Cimatron wrote).

7. **Report.** Print:
   - The INI path that was modified.
   - The exact line that was added or updated (and any stale wrong-class line that was removed).
   - A reminder: **Cimatron must be fully closed and reopened** for the change to take effect. The INI is only read at startup.
   - Note that Cimatron will flip the `@1` flag back to `@0` automatically after the next launch — that's not a regression, it's how the file works.

## Failure modes

- **No class implementing `ICimApiCommandPlugin` in the project:** stop. The project isn't a Cimatron API plugin (or the interface name has been imported via an unusual alias that you couldn't grep).
- **Multiple classes implement `ICimApiCommandPlugin`:** stop, ask which one. Cimatron only loads one per DLL.
- **User passes the `*Command` class name explicitly via the argument:** warn and refuse. The cast inside `CimUIInfrastructure.dll` will fail at runtime. If the user insists they know what they're doing, do not write — point them at the `ICimApiCommandPlugin` requirement.
- **Write fails with access denied:** stop and surface the Administrator requirement. Do not write to a user-writable shadow path; Cimatron only reads the system path.
