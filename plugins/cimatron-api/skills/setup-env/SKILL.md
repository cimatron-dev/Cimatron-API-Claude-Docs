---
name: setup-env
description: Verify the local environment is ready for Cimatron API plugin development — VSCode, the C# Dev Kit extension, the .NET Framework 4.8 targeting pack, and at least one installed Cimatron 2024.0 or newer. Reports a pass/fail table and offers per-item install for anything missing. Run this before `/new-cimatron-api` on a fresh machine.
argument-hint: [--quiet] [--no-install]
---

Verify and (optionally) install the prerequisites a Cimatron API plugin project needs to build and F5-debug.

Arguments: $ARGUMENTS

## What this skill checks

| # | Prereq | Why it matters |
|---|---|---|
| 1 | VSCode (`code` on PATH) | The template's `.vscode/launch.json` and `tasks.json` drive the F5 → build → deploy → attach flow. No VSCode, no F5. |
| 2 | `ms-dotnettools.csdevkit` extension | Brings the `clr` debugger needed to attach to .NET Framework processes (Cimatron is .NET Framework 4.8 / x64). |
| 3 | .NET Framework 4.8 targeting pack | The template targets `net48`. Without the targeting pack, `dotnet build` fails with `MSB3644: The reference assemblies for .NETFramework,Version=v4.8 were not found`. |
| 4 | Cimatron ≥ 2024.0 installed | The plugin DLL is deployed into `<CimatronRoot>\Program\`. The template's `Directory.Build.props` defaults to the latest installed version. |

`--no-install` skips the "offer to install" step and just reports. `--quiet` shrinks the report to the pass/fail table only (no explanatory text). Default is interactive: report, then ask per missing item whether to install.

## Workflow

### 1. Run the four checks in parallel.

Issue all four detection commands as a single batch of `PowerShell` tool calls. Don't ask the user anything yet — gather state first.

**Check 1 — VSCode on PATH**

```powershell
$cmd = Get-Command code -ErrorAction SilentlyContinue
if ($cmd) {
    $ver = (& code --version 2>$null | Select-Object -First 1)
    "VSCode: OK ($ver) at $($cmd.Source)"
} else {
    "VSCode: MISSING"
}
```

**Check 2 — C# Dev Kit extension**

```powershell
$cmd = Get-Command code -ErrorAction SilentlyContinue
if (-not $cmd) {
    "csdevkit: SKIPPED (no code on PATH)"
} else {
    $hit = & code --list-extensions 2>$null | Where-Object { $_ -ieq 'ms-dotnettools.csdevkit' }
    if ($hit) { "csdevkit: OK" } else { "csdevkit: MISSING" }
}
```

`code --list-extensions` is idempotent and fast. Don't try to verify the extension by poking `%USERPROFILE%\.vscode\extensions\` — extension folders carry a version suffix and the layout differs between stable and insiders builds.

**Check 3 — .NET Framework 4.8 targeting pack**

The runtime is almost always present on a developer machine; the *targeting pack* (also called the Developer Pack) is what's usually missing. Both must be present.

```powershell
# 4.8 runtime (Release >= 528040 means 4.8 is installed)
$rt = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
$rtOK = ($rt -ge 528040)

# 4.8 reference assemblies (= targeting pack)
$refDir = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$tpOK = Test-Path (Join-Path $refDir 'mscorlib.dll')

if ($rtOK -and $tpOK)      { ".NET 4.8 dev pack: OK" }
elseif ($rtOK -and -not $tpOK) { ".NET 4.8 dev pack: MISSING (runtime OK, targeting pack absent)" }
else                            { ".NET 4.8 dev pack: MISSING (runtime absent)" }
```

**Check 4 — Installed Cimatron versions ≥ 2024.0**

```powershell
$root = 'C:\Program Files\Cimatron\Cimatron'
if (-not (Test-Path $root)) {
    "Cimatron: MISSING (no install under $root)"
} else {
    $versions = Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d{4}\.\d+$' } |
        Where-Object {
            $parts = $_.Name -split '\.'
            [int]$parts[0] -ge 2024 -and (Test-Path (Join-Path $_.FullName 'Program'))
        } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -ExpandProperty Name
    if ($versions.Count -eq 0) {
        "Cimatron: MISSING (no usable version >= 2024.0 found under $root)"
    } else {
        "Cimatron: OK (" + ($versions -join ', ') + ")"
    }
}
```

Cimatron's install root is configurable, but the default on every observed install has been `C:\Program Files\Cimatron\Cimatron\<version>\Program`. If a user has installed Cimatron elsewhere, surface that as a follow-up: ask them for their root and re-run the version scan against it. Don't try to enumerate every drive.

### 2. Print the report.

Show a compact pass/fail table:

```
Prereq                        Status   Detail
----------------------------  -------  ----------------------------------------
VSCode                        OK       1.92.1 at C:\Users\...\Code\bin\code.cmd
C# Dev Kit (csdevkit)         OK
.NET Framework 4.8 dev pack   MISSING  runtime OK, targeting pack absent
Cimatron >= 2024.0            OK       2026.0, 2025.0, 2024.0
```

When multiple Cimatron versions are present, list them newest-first — that's the order `/new-cimatron-api` will use when prompting the user to pick a target version.

After the table, list anything missing as numbered items so the user can refer to them by number when answering install prompts.

### 3. For each missing item, offer to install (unless `--no-install` was passed).

Ask the user **one combined question** listing every missing item with its install action. Don't ask one question per item — it's noisy and they may want to skip the whole step.

Install actions per item:

#### VSCode

```powershell
winget install -e --id Microsoft.VisualStudioCode --scope user --silent --accept-package-agreements --accept-source-agreements
```

`--scope user` avoids the admin prompt and installs into `%LOCALAPPDATA%\Programs\Microsoft VS Code`. The installer adds `code` to the user PATH; the **current PowerShell session won't see it** until restart. Tell the user to open a new shell (or just press F5 in VSCode after launching it from the Start menu) before running `/setup-env` again.

If `winget` itself isn't on the machine (Windows Server, old Win10), fall back to printing the direct link: `https://code.visualstudio.com/download`.

#### C# Dev Kit extension

```powershell
code --install-extension ms-dotnettools.csdevkit
```

Idempotent — safe to re-run. Requires `code` on PATH, so if VSCode was just installed in the same flow, this will fail in the current session; tell the user to restart the shell and rerun `/setup-env`.

#### .NET Framework 4.8 targeting pack

`winget` exposes the developer pack but the ID has shifted across winget releases. Try this order and stop at the first success:

```powershell
winget install -e --id Microsoft.DotNet.Framework.DeveloperPack_4 --silent --accept-package-agreements --accept-source-agreements
# if that fails, try:
winget install -e --id Microsoft.DotNetFramework.DeveloperPack_4 --silent --accept-package-agreements --accept-source-agreements
```

If both fail, print the official download link and stop: `https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48`. The installer requires admin — if the user isn't elevated, the MSI will silently prompt UAC; that's fine, just tell them what to expect.

#### Cimatron

**Never auto-install Cimatron.** It's licensed software with a multi-GB installer and per-customer configuration. Print this and stop:

> Cimatron isn't a download we can fetch. Install it from your Cimatron distributor's installer (or your IT-managed deployment). The expected layout is `C:\Program Files\Cimatron\Cimatron\<version>\Program\`. Re-run `/setup-env` once it's installed.

### 4. Re-verify after installs.

After running any install commands, re-run the four detection blocks from step 1 and print a final table. **Don't claim a fix succeeded based on a non-zero install exit code** — installs that exit 0 can still leave the targeting pack uninstalled (e.g. winget reports success after fetching but the silent MSI bailed). Verification = detection passes again, not "the install command exited 0".

If anything is still missing after a re-check, surface the residual list clearly. Don't loop on the install.

### 5. Report Cimatron versions for downstream use.

When this skill is invoked as a precheck (i.e. before `/new-cimatron-api`), the final user-facing line should explicitly list the available Cimatron versions in newest-first order, e.g.:

```
Cimatron versions available: 2026.0 (default), 2025.0, 2024.0
```

That's the cue for `/new-cimatron-api` to ask the user which one to target. If only one version is installed, just say:

```
Cimatron versions available: 2026.0
```

No prompt needed in that case — `/new-cimatron-api` should silently use the only one.

## When to invoke this skill

- **As a standalone command**, when the user runs `/setup-env` from any folder.
- **As a precheck**, from `/new-cimatron-api` Step 0 on a fresh machine. The agent form (`cimatron-api:setup-env`) is the right invocation in that context — see `plugins/cimatron-api/agents/setup-env.md`. Skip the precheck if the user passed `--skip-env-check` to `/new-cimatron-api`, or if a previous `/setup-env` in the same conversation reported all green.

## Things to avoid

- **Don't run `winget install -e --id Microsoft.VisualStudioCode` with `--scope machine`** unless the user is already elevated. The default user-scope install is fine for development and avoids the UAC prompt entirely.
- **Don't try to detect the C# Dev Kit by looking inside `%USERPROFILE%\.vscode\extensions\`.** The folder name carries a version suffix and the layout changes between stable and insiders. `code --list-extensions` is the supported way.
- **Don't conflate the .NET 4.8 runtime and the 4.8 targeting pack.** Almost every Windows machine has the runtime; missing targeting pack is the failure mode this check catches. The report must distinguish them.
- **Don't enumerate every drive for Cimatron.** The default install root is `C:\Program Files\Cimatron\Cimatron\<version>\Program\`. If a user is on a custom root, they can tell you and you can re-scan; don't speculatively crawl the filesystem.
- **Don't filter Cimatron versions by an exact equality check (`= 2026.0`).** The spec is "any 2024.0 or newer". The version compare uses `[version]` cast so `2025.10` correctly sorts after `2025.2`.
- **Don't auto-install Cimatron itself.** Licensed software, no.
- **Don't loop on installs.** Run them once, re-detect, report what's still missing, stop. Looping hides root-cause issues like a UAC denial or a network failure.
- **Don't commit anything.** This skill mutates the local machine, not the repo. Whatever the user wants in git is their call.
