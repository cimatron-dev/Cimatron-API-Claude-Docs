# bootstrap.ps1 — registration-free COM connect into a running/new Cimatron.
# Dot-source this from another script; afterwards $acc, $app, $pdm are in scope,
# plus a Stop-Cimatron function. No COM registration, no CadCimAiShell, no admin.
# See SKILL.md for usage, the late-bound modeling idioms, and the limits.
$ErrorActionPreference = 'Stop'

# --- locate the newest installed Cimatron Program folder + the .NET interop PIAs ---
$base = 'C:\Program Files\Cimatron\Cimatron'
$verDir = Get-ChildItem $base -Directory -ErrorAction SilentlyContinue |
          Where-Object { $_.Name -match '^\d' } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $verDir) { throw "No Cimatron install found under $base" }
$Program = Join-Path $verDir.FullName 'Program'
$Interop = 'C:\Program Files\Cimatron\API\DotNetClass'   # the .NET interop PIAs
if (-not (Test-Path (Join-Path $Interop 'interop.CimAppAccess.dll'))) { $Interop = $Program }  # fallback

# --- Side-by-Side activation context: lets the AppAccess coclass activate WITHOUT a
#     registry registration. lpSource points at CimAppAccess.dll's embedded RT_MANIFEST
#     (resource id 2), which declares the Cimatron coclasses. This is the whole trick. ---
if (-not ([System.Management.Automation.PSTypeName]'Sxs.Native').Type) {
  Add-Type -Namespace Sxs -Name Native -MemberDefinition @'
[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
public struct ACTCTX { public int cbSize; public uint dwFlags; public string lpSource; public ushort wProcessorArchitecture; public ushort wLangId; public string lpAssemblyDirectory; public IntPtr lpResourceName; public string lpApplicationName; public IntPtr hModule; }
[DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern IntPtr CreateActCtxW(ref ACTCTX a);
[DllImport("kernel32.dll", SetLastError=true)] public static extern bool ActivateActCtx(IntPtr h, out IntPtr cookie);
'@
}
$ctx = New-Object Sxs.Native+ACTCTX
$ctx.cbSize = [Runtime.InteropServices.Marshal]::SizeOf([type]([Sxs.Native+ACTCTX]))
$ctx.dwFlags = 0x8 -bor 0x4          # ACTCTX_FLAG_RESOURCE_NAME_VALID | ACTCTX_FLAG_ASSEMBLY_DIRECTORY_VALID
$ctx.lpSource = Join-Path $Program 'CimAppAccess.dll'
$ctx.lpAssemblyDirectory = $Program
$ctx.lpResourceName = [IntPtr]2      # RT_MANIFEST id 2
$hCtx = [Sxs.Native]::CreateActCtxW([ref]$ctx)
if ($hCtx -eq [IntPtr]::Zero -or $hCtx -eq [IntPtr](-1)) { throw "CreateActCtxW failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
$cookie = [IntPtr]::Zero
if (-not [Sxs.Native]::ActivateActCtx($hCtx, [ref]$cookie)) { throw "ActivateActCtx failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }

# --- load the interop PIAs so typed enums (ExtrudeSweepMode, MdProcedureType, ...) resolve ---
[Reflection.Assembly]::LoadFrom((Join-Path $Interop 'interop.CimAppAccess.dll')) | Out-Null
foreach ($dll in (Get-ChildItem (Join-Path $Interop 'interop.*.dll') -ErrorAction SilentlyContinue)) {
  try { [Reflection.Assembly]::LoadFrom($dll.FullName) | Out-Null } catch {}
}

$acc = New-Object interop.CimAppAccess.AppAccessClass

# Snapshot existing CimatronE PIDs so we can tell launched-vs-attached afterwards.
$cimPreIds = @(Get-Process CimatronE -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)

# Attach to a running instance if possible; otherwise cold-launch via GetApplication().
# GetActiveApplication() uses registry-based GetActiveObject and THROWS "Class not
# registered" when nothing is running (the SxS context does not help it) — so try it,
# but fall through to GetApplication(), which CoCreateInstances under the activation
# context: it attaches to a running instance, or launches CimatronE.exe (~2-3 min cold).
$app = $null
try { $app = $acc.GetActiveApplication() } catch { }
if (-not $app) {
  Write-Host "[bootstrap] attach failed; connecting via GetApplication (cold launch can take ~2-3 min)..."
  for ($i = 0; $i -lt 200 -and -not $app; $i++) {
    try { $app = $acc.GetApplication() } catch { }
    if (-not $app) { Start-Sleep -Milliseconds 1000 }
  }
}

# A CimatronE PID that did NOT exist before means WE launched it; otherwise we attached
# to a pre-existing user session. This drives whether Stop-Cimatron may close it.
$cimNewIds = @(Get-Process CimatronE -ErrorAction SilentlyContinue | Where-Object { $cimPreIds -notcontains $_.Id } | Select-Object -ExpandProperty Id)
$cimLaunched = ($cimNewIds.Count -gt 0)
$cimPid = if ($cimLaunched) { $cimNewIds[0] } else { $null }

if (-not $app) {
  # Clean up anything we spawned during the failed launch so it can't linger and hang the desktop.
  if ($cimLaunched) { $cimNewIds | ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue } }
  throw "Could not obtain IApplication (launch timed out). Tip: open Cimatron manually, then re-run to ATTACH (faster and avoids cold-launch flakiness)."
}
$pdm = $app.GetPdm()
if ($cimLaunched) { Write-Host "[bootstrap] launched a new Cimatron (PID $cimPid); Stop-Cimatron will close it." }
else { Write-Host "[bootstrap] attached to a pre-existing Cimatron (Stop-Cimatron will leave it running)." }
Write-Host "[bootstrap] OK - Cimatron $($verDir.Name); `$app and `$pdm in scope."

# CRITICAL: a Cimatron launched purely for COM automation does NOT exit when your script
# ends - it lingers with an unresponsive window and HANGS the user's desktop. Always call
# Stop-Cimatron in a finally{} after your work. It closes ONLY an instance WE launched
# (detected by new PID), never a pre-existing user session, and kills rather than Exit()s
# to avoid a "save changes?" dialog blocking shutdown. Also: do NOT run COM-driving
# unattended in the background - keep it foreground so a stuck launch is visible.
function Stop-Cimatron {
  if (-not $cimLaunched) { return }
  if ($cimPid) {
    $p = Get-Process -Id $cimPid -ErrorAction SilentlyContinue
    if ($p) { Stop-Process -Id $cimPid -Force -ErrorAction SilentlyContinue; Write-Host "[bootstrap] closed the Cimatron we launched (PID $cimPid)." }
  }
}
