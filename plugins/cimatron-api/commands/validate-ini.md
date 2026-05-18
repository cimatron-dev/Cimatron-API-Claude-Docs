---
description: Audit C:\\ProgramData\\Cimatron\\Cimatron\\2026.0\\Data\\ExternalCommands.ini for wrong-class plugin keys, stale entries, duplicates, encoding/BOM drift, and malformed flags. Read-only — reports findings, never edits. Use /register-command and /unregister-command to apply fixes.
argument-hint: [--ini-path "<path>"] [--cimatron-root "<C:\\Path\\To\\Cimatron\\Program>"]
---

Audit `ExternalCommands.ini` for the classes of breakage that silently disable Cimatron plugins. Read-only — this command never edits the INI. To apply a fix, use `[[register-command]]` or `[[unregister-command]]`.

Arguments: $ARGUMENTS

## Background — what this audits

`ExternalCommands.ini` is the file Cimatron reads at startup to decide which plugin DLLs to load. A single mistyped key or a stale entry pointing at a deleted DLL is enough to silently disable a working plugin — the DLL sits in the Program folder, but no toolbar button appears, and there's no error dialog. This audit catches the six failure modes we've actually seen bite users:

1. **Wrong-class registration** — the key under `[Plugin Ext Commands]` points at the `…Command` class (`ICimWpfCommand`) instead of the `…Plugin` class (`ICimApiCommandPlugin`). The cast inside `CimUIInfrastructure.dll` throws `InvalidCastException` at load time. Symptom: the plugin "loads" but no toolbar button appears. See `[[command-standard]]` for the rule that the key must be the `ICimApiCommandPlugin` class.
2. **Stale entry pointing at a deleted DLL** — the key references `<Namespace>.<Class>`, but no DLL named `<Namespace>.dll` (or matching `AssemblyName`) sits in the Cimatron Program folder. The plugin can't load.
3. **Duplicate keys** — the same key appears twice in `[Plugin Ext Commands]`, or in both `[Plugin Ext Commands]` and `[COM Ext Commands]`. Cimatron's behavior is implementation-defined here; flag it.
4. **Encoding / BOM drift** — Cimatron's own writer produces UTF-8 with BOM. Hand-edits in some editors (Notepad++ "Encode in UTF-8 without BOM", VS Code with `files.encoding=utf8`) strip the BOM, which corrupts the loader in older Cimatron builds.
5. **Missing `[Global Flags]` section with `ResetApiCommands=1`** — the file may be a fresh skeleton that Cimatron has never re-read. See the skeleton in `[[register-command]]`.
6. **`@<flag>` value out of `{0, 1}`** — malformed reload flag.

## Severity buckets

Same buckets as `api-reviewer.md` so output is consistent across the plugin:

| Severity | Meaning |
|---|---|
| **Critical** | Plugin will not load, or Cimatron will refuse to read the file. Must fix before shipping. |
| **Should fix** | Loads today but behavior is implementation-defined or surprising. Will bite the next maintainer. |
| **Nice to have** | Convention / hygiene drift. Not blocking. |

Cite the INI line number for each finding using `ini:<line>` (mirrors `api-reviewer.md`'s `file:line` citations). Use 1-based line numbers from the raw file.

## Steps

1. **Parse arguments.**
   - `--ini-path` defaults to `C:\ProgramData\Cimatron\Cimatron\2026.0\Data\ExternalCommands.ini`.
   - `--cimatron-root` defaults to `C:\Program Files\3D Systems\Cimatron\2026.0\Program`. It cannot be derived from `--ini-path` (the INI lives under `ProgramData`, the DLLs under `Program Files` — different roots). If the default doesn't exist, check `$env:CimatronRootPath`; if that's also empty, ask the user to supply `--cimatron-root` explicitly rather than guessing further.

2. **Read the INI as bytes.** Use the Read tool, then inspect the first three bytes for the BOM (`Get-Content -AsByteStream -TotalCount 3` in PowerShell, or `head -c 3 <path> | xxd` via Bash). Record:
   - The raw byte length.
   - Whether the first three bytes are `EF BB BF` (UTF-8 BOM present) or not.
   - The line ending style (CRLF vs LF) — note it but don't flag it; Cimatron tolerates both.

   If the file doesn't exist, stop with a friendly message. Nothing to audit — and **do not** create the skeleton. That's `/register-command`'s job.

3. **Parse the INI into sections.** Walk the file line by line, tracking the current `[Section]` header. For each non-blank, non-comment line inside a section, split on the first `=` into `key` and `value`. Keep the line number with every entry — you'll need it for citations.

4. **Check 1 — Wrong-class registrations (Critical).** For each entry in `[Plugin Ext Commands]`:
   - Extract the key (`<Namespace>.<ClassName>`).
   - If `<ClassName>` ends in `Command` and does not end in `PluginCommand`, flag it. This is the classic shape of the `ICimWpfCommand` class accidentally registered instead of the `ICimApiCommandPlugin` class.
   - `…PluginCommand` is the older convention for the plugin class itself (e.g. `TrimAnglePluginCommand`) — do **not** flag those.
   - Cite `ini:<line>` and recommend re-running `/register-command` from the project folder, which discovers the correct class automatically.

5. **Check 2 — Stale entries pointing at deleted DLLs (Critical).** List `--cimatron-root` **once** into a set of filenames, then check each entry against the cached set (don't re-glob per entry). For each entry in `[Plugin Ext Commands]` and `[COM Ext Commands]`:
   - The expected DLL is `<Namespace>.dll` where `<Namespace>` is everything before the last `.` in the key.
   - Also accept the case where the DLL's `AssemblyName` differs from the namespace — if a DLL whose filename stem matches the namespace's last segment exists, count that as found. (Cheap proxy without loading assemblies.)
   - If neither is present, flag it. Cite `ini:<line>` and recommend `/unregister-command` to remove the stale entry.
   - If `--cimatron-root` couldn't be resolved, **skip this check** and note the skip in the report's "File shape" preamble. Don't false-positive everything.

6. **Check 3 — Duplicate keys (Should fix).**
   - Within `[Plugin Ext Commands]`, group entries by key. Two or more entries with the same key → flag both line numbers.
   - Cross-section: if the same key appears in both `[Plugin Ext Commands]` and `[COM Ext Commands]`, flag both. Cimatron's behavior in this case is implementation-defined.
   - Cite each occurrence's `ini:<line>`.

7. **Check 4 — Encoding / BOM drift (Should fix).**
   - If the file is missing the UTF-8 BOM (first three bytes not `EF BB BF`), flag once at `ini:1`. Note that Cimatron's own writer produces BOM'd UTF-8 and older builds choke on stripped-BOM files.
   - If the file contains any non-ASCII bytes that fail to decode as UTF-8 (e.g. it was saved as Windows-1252 with an accented character in a comment), flag as **Critical** instead — Cimatron will not read past the bad byte.

8. **Check 5 — Missing `[Global Flags]` / `ResetApiCommands` (Should fix).**
   - If the file has no `[Global Flags]` section, flag at `ini:1`.
   - If `[Global Flags]` exists but has no `ResetApiCommands` key, flag at the section header line.
   - The presence of `ResetApiCommands=0` is *not* a finding — Cimatron auto-flips `1` to `0` after a reload, so `0` is the steady state. Only flag absence.

9. **Check 6 — Malformed `@<flag>` value (Critical).** For each entry in `[Plugin Ext Commands]`:
   - The value should be `<Namespace>.<ClassName>@<flag>` where `<flag>` is `0` or `1`.
   - Missing `@` separator entirely → Critical, cite `ini:<line>`.
   - `@` present but flag is not `0` or `1` (e.g. `@2`, `@`, `@true`) → Critical, cite `ini:<line>`.
   - The class part of the value should match the key. If they diverge (key is `A.B` but value is `C.D@1`), flag as **Should fix** — it loads, but is suspicious.

10. **Emit the report** in the format below. Do **not** write the file. Do not propose fixes by writing them — describe the fix in prose and point at `/register-command` or `/unregister-command`.

## Failure modes

- **INI file missing:** stop with a friendly message, do not create it. `/register-command` creates the skeleton when it needs to.
- **`--cimatron-root` does not exist:** ask the user for the correct path. If they decline, skip check 2 only and continue with the rest. Note the skip prominently in the report.
- **File not readable (locked by Cimatron):** stop and tell the user to close Cimatron, then re-run. Don't audit a partial read.
- **File parses as something other than INI** (e.g. someone replaced it with HTML, JSON, a backup with an unrelated extension): flag as Critical at `ini:1` and stop — the rest of the checks would produce noise.

## Output format

```
# validate-ini — <ini-path>

## File shape
<one line: BOM present/absent, byte length, section list found, cimatron-root used (or "skipped check 2: --cimatron-root not provided")>

## Critical
- ini:<line> — <issue>

## Should fix
- ini:<line> — <issue>

## Nice to have
- ini:<line> — <issue>

## Verdict
<one line: "Ready." if no Critical, otherwise "Blockers found — fix Critical items before relaunching Cimatron.">
```

Sample report:

```
# validate-ini — C:\ProgramData\Cimatron\Cimatron\2026.0\Data\ExternalCommands.ini

## File shape
UTF-8 with BOM, 1,847 bytes, sections: [Global Flags] [COM Ext Commands] [Plugin Ext Commands] [External Pane]. Cimatron root: C:\Program Files\3D Systems\Cimatron\2026.0\Program.

## Critical
- ini:14 — Wrong-class key `TrimAngle.TrimAngleCommand`. The key must be the `ICimApiCommandPlugin` class (typically `TrimAngle.TrimAnglePlugin`), not the `ICimWpfCommand` class. Cast fails at load. Re-run /register-command from the project folder.
- ini:17 — Stale entry `OldFeature.OldFeaturePlugin`: no `OldFeature.dll` in the Cimatron Program folder. Use /unregister-command to remove.
- ini:21 — Malformed reload flag `@2` (must be `0` or `1`).

## Should fix
- ini:14, ini:18 — Duplicate key `TrimAngle.TrimAngleCommand` appears twice in [Plugin Ext Commands].

## Nice to have
- (none)

## Verdict
Blockers found — fix Critical items before relaunching Cimatron.
```

If a severity section has nothing, write `(none)` — don't omit it. Group related findings (e.g. one key duplicated across three lines) under one bullet that cites all the line numbers, not three repeats.

## Hard rules

- **Read-only.** This command never writes to the INI. To fix what it finds, use `[[register-command]]` (to add/correct an entry) or `[[unregister-command]]` (to remove a stale entry).
- **Don't propose fixes by writing them.** Describe the fix in prose and point at the appropriate slash command.
- **Don't re-derive the rules.** The plugin-vs-command-class rule lives in `[[command-standard]]` and the slash commands' source. If those documents disagree, surface the conflict in the report rather than picking a side.
