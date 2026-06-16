---
name: logs
description: Tail Cimatron user logs for the current plugin project. Surfaces the LogInfo/LogException bookend entries the Cimatron command standard requires on every entry point, grouped by run. Use after F5/run to see what happened on the last invocation.
argument-hint: [--project <path>] [--command <Name>] [--last <N>] [--failed-only]
---

# Cimatron Plugin Log Tail

You are a specialized assistant for surfacing the user-visible log entries a
Cimatron 2026 plugin writes through its `Logger.LogInfo` / `Logger.LogException`
bookends.

The Cimatron command standard (see `[[command-standard]]`) requires every
entry point — every `ICimWpfCommand.OnCommand`, every DmHook callback, every
Feature Guide `OnApply`/`OnEvent` — to open with `Logger.LogInfo("… started")`
and close with `Logger.LogInfo("… finished")`, with any thrown exception
captured by `Logger.LogException` in between. Those bookends are how customer
issues become traceable. This skill reads the file the bookends write to and
surfaces it grouped by run.

This skill is read-only. It does not edit, rotate, or delete log files.

## What this skill does

| Concern              | Behavior                                                                                              |
|----------------------|-------------------------------------------------------------------------------------------------------|
| Cimatron log location | Resolved from the project's `helpers/Logger.cs` — never hardcoded. See "How to find the log path".  |
| Plugin filter        | Inferred from the project's `AssemblyName` (defaults to the project file's base name).                |
| Run grouping         | A run is one `… started` line through the matching `… finished` line; an `EXCEPTION` between flags fail. |
| Failure-only mode    | `--failed-only` suppresses passing runs entirely.                                                     |
| Multi-command filter | `--command <Name>` narrows to runs whose `started`/`finished` message mentions `<Name>`.              |

## How to find the log path

Do **not** hardcode `%LOCALAPPDATA%`, `%APPDATA%`, or `%USERPROFILE%\Downloads`.
The actual path is written verbatim by the plugin's own `helpers/Logger.cs`.
Read that file in the resolved project and follow its `LogFilePath` expression.

For the bundled template (`plugins/cimatron-api/template/helpers/Logger.cs`),
the path resolves to:

```
%USERPROFILE%\Downloads\<AssemblyName>.log.txt
```

…because `LogFilePath` is `Path.Combine(GetDownloadsDirectory(), "ApiName.log.txt")`
and `<ApiName>` is substituted with the project's `AssemblyName` at scaffold
time. If a downstream project has edited `Logger.cs` to write elsewhere
(`%LOCALAPPDATA%\Cimatron\…`, a UNC share, etc.), follow that file — it is the
source of truth, not this skill.

**Always surface the resolved path back to the user** so they can navigate to
it manually if they want the raw file. If the file does not exist yet, say so
plainly: the plugin has not run since it was last built, or `LogExternal` was
disabled.

## How to resolve the plugin name

1. If `--project <path>` is supplied, use that directory.
2. Otherwise default to the current working directory if it contains exactly
   one `*.csproj`. If zero or multiple, stop and ask the user to pass
   `--project`.
3. Read the `<AssemblyName>` element from the `.csproj`. If absent, fall back
   to the `.csproj` filename without extension.
4. The log file is `<AssemblyName>.log.txt` under the directory `Logger.cs`
   resolves to.

## Output format

Each log line in the file looks like:

```
[2026-05-18 14:32:07.412] INFO: HelloCimatron command invoked.
[2026-05-18 14:32:07.580] INFO: HelloCimatron command finished.
```

Group consecutive lines that share a logical run. The opener is the line
matching `… invoked.` or `… started`; the closer is the next `… finished` or
`… completed` for the same command/hook name. Any `EXCEPTION` between them
marks the run as failed.

Pass example:

```
[14:32:07.412 → 14:32:07.580]  PASS  HelloCimatron command
  INFO  HelloCimatron command invoked.
  INFO  HelloCimatron command finished.
```

Fail example:

```
[14:35:18.901 → 14:35:18.954]  FAIL  HelloCimatron command
  INFO       HelloCimatron command invoked.
  EXCEPTION  Object reference not set to an instance of an object.
               at HelloCimatron.HelloCimatronCommand.OnCommand() in C:\…\HelloCimatronCommand.cs:line 22
  INFO       HelloCimatron command finished.
```

If the same run logs more than ~10 lines or the `EXCEPTION` stack trace runs
longer than the first frame plus message, head it — show the message and the
first stack frame, and tell the user how many frames were elided. Do not
paste full multi-KB stack traces into the conversation.

## Workflow

1. Resolve `--project` (or fall back to cwd as described above) and read the
   project's `<AssemblyName>`.
2. Read the project's `helpers/Logger.cs` and extract the path expression it
   uses for `LogFilePath`. Resolve that expression against the current
   environment to a concrete file path. Echo the resolved path.
3. If the file does not exist, report that clearly and stop.
4. Read the file with `Get-Content -Tail <buffer>` where `buffer` is large
   enough to contain `--last <N>` runs (a safe default is `N * 20` lines, or
   the whole file if it is small). Do not load multi-MB files in full.
5. Parse the lines into runs using the `started`/`finished` bookends. If
   `--command <Name>` is supplied, drop runs whose bookend text does not
   contain `<Name>`.
6. If `--failed-only`, drop passing runs *first*, then trim to the last `<N>`
   runs (default 50). Filtering after trimming would silently return fewer
   than `<N>` failures when the buffer held more.
7. Print the grouped output as shown in "Output format", then offer to open
   the raw file path in the user's editor on request.

## Things to avoid

- **Do not tail logs from multiple plugins at once unless the user explicitly
  omitted `--project`.** Mixing two plugins' runs in one output makes the
  bookend pairing ambiguous. If the user is in a workspace with several
  plugin projects and didn't pick one, ask which `--project` they meant.
- **Do not paste the full log body if it exceeds a reasonable size** (rule
  of thumb: more than ~40 lines per run or more than ~200 lines total).
  Head each run with its summary line; for failures, include the exception
  message and the first stack frame, and note how many frames were elided.
- **Do not hardcode the log path** (`%USERPROFILE%\Downloads\…`,
  `%LOCALAPPDATA%\Cimatron\…`, etc.). Always resolve via the project's own
  `helpers/Logger.cs`. Downstream projects do edit that file.
- **Do not modify the log file.** No rotation, no truncation, no rewrites.
  Read-only.
- **Do not invent log entries that aren't in the file.** If a run has no
  `finished` line, label it `INCOMPLETE` and show what's there — don't
  fabricate a closer.

## See also

- `[[command-standard]]` — the entry-point bookend convention this skill
  reads. If a plugin's commands don't follow it, this skill will have
  nothing to group.
