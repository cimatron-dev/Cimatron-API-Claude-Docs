---
name: run-cimatron
description: Mid-conversation F5 equivalent for a Cimatron API plugin project. Closes any running CimatronE.exe, builds the project, launches Cimatron, and attaches the managed (clr) debugger so the user can hit breakpoints without leaving the chat. Reads CimatronRootPath from the project's Directory.Build.props.
argument-hint: [--project <path>] [--no-rebuild]
---

Run the same build → deploy → launch → attach sequence that VSCode's F5 performs, from inside the conversation. Use this when the user wants to iterate on a Cimatron API plugin without alt-tabbing to VSCode.

Arguments: $ARGUMENTS

## What this skill does

| # | Step | Why it matters |
|---|---|---|
| 1 | Close any running `CimatronE.exe` | The build output drops the plugin DLL into `$(CimatronRootPath)`. If Cimatron is running, the DLL is locked and `dotnet build` fails with "the process cannot access the file because it is being used by another process". See `plugins/cimatron-api/template/CLAUDE.md`. |
| 2 | Resolve `CimatronRootPath` from `Directory.Build.props` | The template bakes the user's chosen install path into `<CimatronRootPath>` at scaffold time. Reading it here keeps a multi-version machine on the same target VSCode is wired up to, no second prompt required. |
| 3 | `dotnet build` the `.csproj` | The project's `OutputPath` is `$(CimatronRootPath)`, so the build *is* the deploy. Skipped when `--no-rebuild` is passed. |
| 4 | Launch `CimatronE.exe` | `Start-Process -FilePath "<CimatronRootPath>\CimatronE.exe"` — non-blocking, returns once the process is started. Cimatron's own UI takes a while to come up; that's the user's problem from here. |
| 5 | Attach the managed debugger | Prefer the VSCode path (`code` with the workspace's `launch.json` "Attach to running Cimatron"). Fall back to `vsdbg` if it's installed. Fall back to "press F5 in VSCode" if neither is available. |

`--project <path>` points at the plugin folder (the one containing the `.csproj` and `.vscode/launch.json`). Default is the current working directory. `--no-rebuild` skips step 3 — useful when the user has already built and just wants to relaunch + reattach.

## Workflow

### 1. Resolve the project folder.

```powershell
$proj = if ($projectArg) { $projectArg } else { (Get-Location).Path }
if (-not (Test-Path $proj)) { throw "Project path does not exist: $proj" }
$csproj = Get-ChildItem -Path $proj -Filter '*.csproj' -File | Select-Object -First 1
if (-not $csproj) { throw "No .csproj found in $proj — is this a Cimatron API plugin folder?" }
$buildProps = Join-Path $proj 'Directory.Build.props'
if (-not (Test-Path $buildProps)) { throw "Directory.Build.props not found in $proj — this skill expects a project scaffolded from /new-cimatron-api." }
```

If any of those checks fails, stop with a clear message. Don't try to `dotnet build` something that isn't a Cimatron API plugin — the F5 contract depends on the scaffolded layout.

### 2. Read `CimatronRootPath` from `Directory.Build.props`.

```powershell
[xml]$props = Get-Content -Raw $buildProps
$rootRaw = $props.Project.PropertyGroup.CimatronRootPath |
    Where-Object { $_ -is [string] -and $_ -notmatch 'EnsureTrailingSlash' } |
    Select-Object -First 1
if (-not $rootRaw) { throw "Could not read <CimatronRootPath> from $buildProps." }
$cimRoot = $rootRaw.TrimEnd('"').TrimEnd('\') + '\'
$cimExe = Join-Path $cimRoot 'CimatronE.exe'
if (-not (Test-Path $cimExe)) {
    throw "CimatronE.exe not found at $cimExe. Edit <CimatronRootPath> in $buildProps and re-run."
}
```

`Directory.Build.props` declares `<CimatronRootPath>` twice — once with the raw value, once wrapped in the `EnsureTrailingSlash` normalization. PowerShell's XML member enumeration flattens both PropertyGroups into the same accessor; filtering out the entry that mentions `EnsureTrailingSlash` keeps the literal value. The same `TrimEnd('"')` the props file applies absorbs the PowerShell `\"` escape pitfall covered in `commands/new-cimatron-api.md`.

If `CimatronE.exe` genuinely isn't at the resolved path, surface the resolved path and tell the user to fix `Directory.Build.props`. Don't try to autodetect — VSCode's `launch.json` is wired to whatever `Directory.Build.props` says, so guessing here would diverge from F5.

### 3. Close any running Cimatron.

```powershell
$running = Get-Process -Name 'CimatronE' -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Closing $($running.Count) running CimatronE.exe process(es) so the build can write to $cimRoot..."
    & taskkill /IM CimatronE.exe /F | Out-Null
    Start-Sleep -Milliseconds 500
}
```

`taskkill /F` is the right tool here — Cimatron doesn't expose a clean-shutdown CLI, and the build will hard-fail anyway if a stale DLL handle survives. Don't loop waiting for graceful exit; the user wanted F5 semantics, which is "kill, build, relaunch".

### 4. Verify the shell is elevated.

```powershell
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw @"
This skill needs to be run from an elevated shell. The project's build output is written into
$cimRoot
which lives under Program Files. Without admin rights, dotnet build fails with 'access denied'.

Relaunch your Claude Code session from a Windows Terminal / PowerShell window started with 'Run as administrator', then retry /run-cimatron.
"@
}
```

This matches the `check-admin` task in the template's `tasks.json` (see `plugins/cimatron-api/template/.vscode/tasks.json`). Surface the failure *before* the build runs — a 30-second `dotnet restore` followed by "access denied" is worse UX than a one-line precheck.

### 5. Build the project (unless `--no-rebuild` was passed).

```powershell
& dotnet build $csproj.FullName -p:Platform=x64 /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE. Fix the build errors above and retry." }
```

Same flags the `build` task in `tasks.json` uses, so the behavior matches F5 exactly. If a sibling `/build` skill has landed by the time this is reviewed, swap the inline call for it.

Do not pass `--no-restore` — the user may have edited `Directory.Build.props` since the last build and the references need to re-resolve.

### 6. Launch Cimatron.

```powershell
Start-Process -FilePath $cimExe -WorkingDirectory $cimRoot
```

`Start-Process` is non-blocking. Don't `Wait-Process` — Cimatron's UI takes 10-30 seconds to come up and the user drives interaction from there; the skill's job is to launch and attach, not to babysit startup.

### 7. Attach the managed debugger.

Pick the first option that works:

**Option A — VSCode (default).** If `code` is on PATH and `$proj/.vscode/launch.json` exists, open the workspace and tell the user to run the "Attach to running Cimatron" configuration. There's no `code` CLI flag that triggers a debug configuration directly, but opening the folder positions VSCode so they can hit F5:

```powershell
$launch = Join-Path $proj '.vscode\launch.json'
$codeOnPath = Get-Command code -ErrorAction SilentlyContinue
if ($codeOnPath -and (Test-Path $launch)) {
    & code $proj
    Write-Host "VSCode opened on $proj. In the Run and Debug panel, pick 'Attach to running Cimatron' and press F5."
    return
}
```

**Option B — standalone vsdbg.** If `%USERPROFILE%\.vsdbg\vsdbg.exe` is present (installed alongside the C# Dev Kit), invoke it directly:

```powershell
$vsdbg = Join-Path $env:USERPROFILE '.vsdbg\vsdbg.exe'
if (Test-Path $vsdbg) {
    $cimPid = (Get-Process -Name 'CimatronE' -ErrorAction SilentlyContinue | Select-Object -First 1).Id
    if ($cimPid) {
        Write-Host "Attaching vsdbg to CimatronE.exe (PID $cimPid). Use --interpreter=vscode or DAP over stdio depending on your client."
        # The caller is responsible for hooking vsdbg's stdio to a debug UI; we just locate and announce it.
        Write-Host "vsdbg path: $vsdbg"
        Write-Host "Target PID: $cimPid"
        return
    }
}
```

`vsdbg` is a DAP server, not an interactive REPL — the agent shouldn't try to spawn it raw and pipe protocol bytes. Surface the path and PID so the user (or a downstream client) can attach properly.

**Option C — fallback.** Neither VSCode on PATH nor `vsdbg` available:

```powershell
Write-Host "Could not auto-attach a debugger. Open the project folder in VSCode (running as Administrator), open the Run and Debug panel, and pick 'Attach to running Cimatron'."
```

That's still a successful run — Cimatron is launched and the plugin is loaded. The user can attach manually.

### 8. Report what happened.

Short summary at the end:

```
Project:        <path to project folder>
CimatronRoot:   <resolved path>
Build:          OK (or SKIPPED if --no-rebuild)
Cimatron:       launched (PID <n>)
Debugger:       VSCode opened / vsdbg located / manual attach required
```

The user takes it from there.

## When to invoke this skill

- After the user edits plugin code mid-conversation and wants to verify the change in Cimatron without alt-tabbing to VSCode.
- After `/new-cimatron-api` finishes scaffolding, if the user says "run it" or "try it" rather than opening VSCode themselves.
- Never as part of `/new-cimatron-api` itself — scaffolding and running are separate concerns, and the user should see the project in VSCode before the first F5.

## Things to avoid

- **Don't skip the `taskkill` step "to be polite".** Cimatron has no clean-shutdown CLI and the build will fail with a locked-file error anyway. The whole point of this skill is the F5 contract, which assumes Cimatron gets killed.
- **Don't prompt for the Cimatron root if `Directory.Build.props` already declares it.** That's what the props file is for. Asking again diverges from what VSCode's F5 does and confuses users with multiple Cimatron versions installed.
- **Don't `Wait-Process` on `CimatronE.exe`.** Cimatron's startup is slow (10-30s) and the user wants to interact with it. The skill's job ends when the process is launched and the debugger handoff is done.
- **Don't try to drive `vsdbg` interactively.** It's a Debug Adapter Protocol server — without a DAP client wired to its stdio, raw invocation just hangs. Locate it, report it, and let VSCode or another client own the protocol.
- **Don't pass `--no-restore` to `dotnet build`.** A user that just edited `Directory.Build.props` (e.g. switched Cimatron versions) needs the references re-resolved; skipping restore turns a one-line fix into a confusing build error.
- **Don't auto-edit `Directory.Build.props` when `CimatronE.exe` isn't at the resolved path.** Surface the mismatch and stop. Editing it silently would also desync `launch.json`, which embeds the same path under `__CIMATRON_ROOT_FORWARD__` at scaffold time.
- **Don't commit anything.** This skill mutates the local machine and the build output; it doesn't touch source. Anything the user wants in git is their call.
