---
description: Build the current Cimatron API plugin project with dotnet build, dropping the DLL straight into the Cimatron Program folder. Build is deploy.
argument-hint: [--no-restore] [--configuration <Debug|Release>] [--project <path>]
---

Build a Cimatron 2026 API plugin in the current working directory. The csproj sets `OutputPath=$(CimatronRootPath)`, so a successful build writes the DLL directly into `C:\Program Files\Cimatron\Cimatron\2026.0\Program` (or wherever `CimatronRootPath` resolves). **The build is the deploy.** There is no separate publish step.

Arguments: $ARGUMENTS

## Steps

1. **Parse arguments.**
   - `--no-restore` skips the implicit `dotnet restore` (use after a clean restore when iterating).
   - `--configuration` defaults to `Debug`. `Release` is fine too; the template doesn't gate behavior on configuration.
   - `--project` defaults to the single `.csproj` in the current working directory. If there are zero or multiple csprojs at the project root and no explicit `--project`, stop and tell the user to pass one.

2. **Pre-flight: is Cimatron running?** Run:

   ```powershell
   tasklist /FI "IMAGENAME eq CimatronE.exe"
   ```

   If `CimatronE.exe` appears in the output, stop and tell the user to close Cimatron first. The build output path is the running Program folder; Cimatron holds an exclusive lock on every loaded DLL, so the link step will fail with "access denied" or "file in use". Do not auto-kill — the user may have unsaved work. If they explicitly say "kill it", `taskkill /IM CimatronE.exe /F` is the escape hatch.

3. **Run the build.** From the project directory:

   ```powershell
   dotnet build "<project>" --configuration <Debug|Release> [--no-restore]
   ```

   Capture stdout and stderr together. `dotnet build` exit code 0 = success; non-zero = at least one error in the output.

4. **On success, report.** Print:
   - The DLL path that was written (the csproj's `OutputPath` + `<AssemblyName>.dll`).
   - The configuration that was built.
   - The next step: if the user is iterating on a command they've already registered, just relaunch Cimatron (F5 in VSCode, or open Cimatron manually). If this is a fresh plugin, remind them to run `/register-command` once before the first launch.

5. **On failure, classify before dumping output.** Read the build output and check, in order, for the three known patterns in [Failure modes](#failure-modes). When one matches, print the matched remediation **above** the raw build output, not below — the raw output is long and the user shouldn't have to scroll to find the answer. If none match, just print the build output and let the user read the compiler errors.

## Failure modes

Pattern-match the build output for these before falling back to generic "here are the errors" reporting.

### 1. CimatronE.exe is locking the output DLL

**Symptoms in output:**
- `MSB3021: Unable to copy file`
- `The process cannot access the file ... because it is being used by another process`
- `access is denied` referencing the output `.dll` or `.pdb`

**Remediation:** Cimatron is open and holds the DLL. Tell the user to close Cimatron normally first. If Cimatron is hung or has been force-detached and the process is still running, the escape hatch is:

```powershell
taskkill /IM CimatronE.exe /F
```

Then rerun `/build`. The pre-flight check in step 2 should catch this before the build runs — if it didn't, a second `CimatronE.exe` instance probably started between the check and the build. Re-check `tasklist`.

### 2. VSCode/PowerShell is not elevated

**Symptoms in output (distinguishing from #1: the denied path is the *directory* or a fresh DLL that doesn't exist yet, not a DLL Cimatron is holding open):**
- `MSB3021` or `access is denied` on the `OutputPath` directory under `Program Files`
- `Could not write to output file` pointing at a path under `Program Files`
- `Access to the path '<CimatronRootPath>...' is denied` where `CimatronE.exe` is **not** running (confirm via the step 2 `tasklist` check)

**Remediation:** The output path is under `Program Files`, which is admin-protected. The current shell isn't elevated. Tell the user to:

1. Close VSCode (or the current PowerShell).
2. Relaunch it **as Administrator** (right-click → Run as administrator).
3. Reopen the project and rerun `/build`.

The project's `.vscode/tasks.json` assumes an elevated shell — there is no workaround that keeps the output path inside `Program Files` and avoids admin. Don't suggest redirecting `OutputPath` elsewhere; that breaks the "build is deploy" model and Cimatron won't load the DLL from a non-Program location anyway.

### 3. .NET Framework 4.8 reference assemblies are missing

**Symptoms in output:**
- `MSB3644: The reference assemblies for .NETFramework,Version=v4.8 were not found`
- `To resolve this, install the Developer Pack (SDK/Targeting Pack) for this framework version`

**Remediation:** The .NET Framework 4.8 Targeting Pack (or Developer Pack) isn't installed on this machine. Run:

```
/setup-env
```

That command installs the targeting pack and re-validates the toolchain. Then rerun `/build`.

### 4. Anything else

If none of the above match, print the raw `dotnet build` output and let the user read it. Two patterns worth a one-line hint when you spot them:

- **`CS8XXX` errors:** the project pins `LangVersion=7.3` (because of net48). A C# 8+ feature snuck in — `using` declarations, target-typed `new`, `record`, switch expressions, pattern combinators. Rewrite the offending line in C# 7.3-compatible form. Do not bump `LangVersion`.
- **`CS0104` "ambiguous reference":** the file imports both `interop.CimBaseAPI` and `interop.CimMdlrAPI` (and sometimes `interop.CimServicesAPI`) without file-scoped aliases. See `[[command-standard]]` and the canonical alias block in the project's `CLAUDE.md` — copy it verbatim into the offending file.

For deeper diagnostics, the Cimatron-side runtime logs are documented at `[[logs]]`; build-time errors won't appear there, but post-build "the plugin loaded but the button doesn't appear" symptoms will.
