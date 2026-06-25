# Build a Cimatron API plugin installer EXE without invoking Claude Code.
# Mirrors steps 2-8 of /package-installer in pure PowerShell. The /package-installer
# slash command (or deploy.ps1) downloads this file alongside Installer.csproj /
# Program.cs / app.manifest into .tools\package-installer\ and then invokes it.

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Out = './dist',

    [switch]$NoUninstall,

    [string]$TargetVersion = 'any',

    [string]$PluginRoot = (Get-Location).Path,

    [string]$TemplateDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

# ----- Resolve paths -----

$PluginRoot = (Resolve-Path -LiteralPath $PluginRoot).Path
$TemplateDir = (Resolve-Path -LiteralPath $TemplateDir).Path

foreach ($name in 'Installer.csproj', 'Program.cs', 'app.manifest') {
    $p = Join-Path $TemplateDir $name
    if (-not (Test-Path -LiteralPath $p)) {
        throw "Missing installer template file: $p. Re-run deploy.ps1 with -Update to refresh the cache."
    }
}

# ----- Step 1: identify the plugin -----

$csprojFiles = @(Get-ChildItem -LiteralPath $PluginRoot -Filter *.csproj -File)
if ($csprojFiles.Count -eq 0) {
    throw "No .csproj found in $PluginRoot. cd into the plugin folder before running deploy."
}
if ($csprojFiles.Count -gt 1) {
    throw "Multiple .csproj files in $PluginRoot. deploy.ps1 operates on one plugin at a time."
}
$Csproj = $csprojFiles[0]
Write-Host "Plugin project: $($Csproj.Name)"

[xml]$CsprojXml = Get-Content -LiteralPath $Csproj.FullName -Raw
$ApiName = $null
$RootNs = $null
foreach ($pg in @($CsprojXml.Project.PropertyGroup)) {
    if ($pg -and $pg.AssemblyName) { $ApiName = ([string]$pg.AssemblyName).Trim() }
    if ($pg -and $pg.RootNamespace) { $RootNs = ([string]$pg.RootNamespace).Trim() }
}
if (-not $ApiName) { $ApiName = $RootNs }
if (-not $ApiName) {
    throw "Could not determine <AssemblyName> or <RootNamespace> from $($Csproj.Name)."
}

# Sanitize - values get embedded inside C# string literals.
if ($ApiName -match '[\\"]' -or $ApiName -notmatch '^[\x20-\x7E]+$') {
    throw "AssemblyName '$ApiName' contains characters illegal in a C# string literal."
}

# ----- Locate the ICimApiCommandPlugin class -----

$PluginClass = $null
$csFiles = Get-ChildItem -LiteralPath $PluginRoot -Filter *.cs -Recurse -File `
           | Where-Object { $_.FullName -notlike "*\obj\*" -and $_.FullName -notlike "*\bin\*" }
foreach ($f in $csFiles) {
    $content = Get-Content -LiteralPath $f.FullName -Raw
    # Strip line comments + block comments to avoid matching commented-out code.
    $stripped = [regex]::Replace($content, '//[^\r\n]*', '')
    $stripped = [regex]::Replace($stripped, '/\*[\s\S]*?\*/', '')

    $nsMatch = [regex]::Match($stripped, 'namespace\s+([\w\.]+)')
    if (-not $nsMatch.Success) { continue }

    $classMatch = [regex]::Match(
        $stripped,
        '\bclass\s+(\w+)\b[^{};]*?:\s*[^{]*?\bICimApiCommandPlugin\b'
    )
    if ($classMatch.Success) {
        $PluginClass = "$($nsMatch.Groups[1].Value).$($classMatch.Groups[1].Value)"
        break
    }
}
if (-not $PluginClass) {
    throw "No class implementing ICimApiCommandPlugin found under $PluginRoot."
}
if ($PluginClass -match '[\\"]' -or $PluginClass -notmatch '^[\x20-\x7E]+$') {
    throw "Plugin class '$PluginClass' contains characters illegal in a C# string literal."
}

# ----- Read version + CimatronRootPath from Directory.Build.props -----

$DbpVersion = '1.0.0'
$CimatronRootPath = $null
$DbpPath = Join-Path $PluginRoot 'Directory.Build.props'
if (Test-Path -LiteralPath $DbpPath) {
    [xml]$DbpXml = Get-Content -LiteralPath $DbpPath -Raw
    foreach ($pg in @($DbpXml.Project.PropertyGroup)) {
        if (-not $pg) { continue }
        if ($pg.Version -and $DbpVersion -eq '1.0.0') {
            $DbpVersion = ([string]$pg.Version).Trim()
        }
        # CimatronRootPath can appear more than once (the literal value plus the
        # EnsureTrailingSlash normalizer). Accessing it as a property returns an
        # XmlElement array, and [string] of that array yields element *names*, not
        # values. Iterate the elements and read InnerText, taking the first literal
        # (non-normalizer) value.
        if (-not $CimatronRootPath) {
            foreach ($crp in @($pg.SelectNodes('CimatronRootPath'))) {
                $cand = $crp.InnerText.Trim()
                if (-not $cand) { continue }
                if ($cand -like '*EnsureTrailingSlash*' -or $cand -like '*$([MSBuild]*') { continue }
                $CimatronRootPath = $cand.TrimEnd('"').TrimEnd('\','/')
                break
            }
        }
    }
}

Write-Host "  AssemblyName:  $ApiName"
Write-Host "  PluginClass:   $PluginClass"
Write-Host "  Version:       $DbpVersion"
if ($CimatronRootPath) { Write-Host "  CimatronRoot:  $CimatronRootPath" }

# ----- Step 2: build the plugin DLL -----

# Pre-flight: CimatronE.exe holds locks on plugin DLLs in its Program folder.
$cimatronRunning = @(Get-Process -Name 'CimatronE' -ErrorAction SilentlyContinue)
if ($cimatronRunning.Count -gt 0) {
    throw "CimatronE.exe is running. Close Cimatron before re-running deploy - the plugin DLL is locked while Cimatron is open."
}

Write-Host ''
Write-Host "Building plugin DLL (dotnet build --configuration $Configuration)..."
& dotnet build $Csproj.FullName --configuration $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Plugin build failed. Fix the compiler errors above and re-run deploy."
}

# Locate the freshly built DLL. Primary location is $(CimatronRootPath)\<ApiName>.dll
# per the template. Fall back to a recursive bin scan if the property couldn't be read.
$DllPath = $null
if ($CimatronRootPath) {
    $candidate = Join-Path $CimatronRootPath "$ApiName.dll"
    if (Test-Path -LiteralPath $candidate) { $DllPath = $candidate }
}
if (-not $DllPath) {
    $binSearch = Get-ChildItem -LiteralPath $PluginRoot -Filter "$ApiName.dll" -Recurse -File `
                 -ErrorAction SilentlyContinue `
                 | Where-Object { $_.FullName -like '*\bin\*' } `
                 | Sort-Object LastWriteTime -Descending `
                 | Select-Object -First 1
    if ($binSearch) { $DllPath = $binSearch.FullName }
}
if (-not $DllPath) {
    throw "Built DLL '$ApiName.dll' not found. Looked under '$CimatronRootPath' and bin/."
}
Write-Host "  Plugin DLL:    $DllPath"

# ----- Step 3: stage the installer build folder -----

$IconPath = Join-Path $PluginRoot "$ApiName.ico"
if (-not (Test-Path -LiteralPath $IconPath)) {
    $LegacyIconPath = Join-Path $PluginRoot 'icon.ico'
    if (Test-Path -LiteralPath $LegacyIconPath) { $IconPath = $LegacyIconPath }
}
$HasIcon = Test-Path -LiteralPath $IconPath
$IconLeaf = if ($HasIcon) { Split-Path -Leaf $IconPath } else { $null }

$ExtraPayloadDir = Join-Path $PluginRoot 'Payload'
$ExtraPayloadFiles = @()
if (Test-Path -LiteralPath $ExtraPayloadDir) {
    $ExtraPayloadFiles = @(Get-ChildItem -LiteralPath $ExtraPayloadDir -File -Recurse)
}

$StageDir = Join-Path $PluginRoot 'obj\installer'
if (Test-Path -LiteralPath $StageDir) {
    Remove-Item -LiteralPath $StageDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $StageDir | Out-Null
$StagePayloadDir = Join-Path $StageDir 'Payload'
New-Item -ItemType Directory -Force -Path $StagePayloadDir | Out-Null

foreach ($name in 'Installer.csproj', 'Program.cs', 'app.manifest') {
    Copy-Item -LiteralPath (Join-Path $TemplateDir $name) -Destination (Join-Path $StageDir $name) -Force
}

Copy-Item -LiteralPath $DllPath -Destination (Join-Path $StagePayloadDir "$ApiName.dll") -Force
if ($HasIcon) {
    Copy-Item -LiteralPath $IconPath -Destination (Join-Path $StagePayloadDir $IconLeaf) -Force
}
foreach ($extra in $ExtraPayloadFiles) {
    Copy-Item -LiteralPath $extra.FullName -Destination $StagePayloadDir -Force
}

# ----- Step 4: substitute @@PLACEHOLDER@@ values -----

$ProgramCsPath = Join-Path $StageDir 'Program.cs'
$InstallerCsprojPath = Join-Path $StageDir 'Installer.csproj'

$programCs = [System.IO.File]::ReadAllText($ProgramCsPath, [System.Text.UTF8Encoding]::new($false))
$programCs = $programCs.Replace('@@API_NAME@@',       $ApiName)
$programCs = $programCs.Replace('@@DLL_NAME@@',       "$ApiName.dll")
$programCs = $programCs.Replace('@@PLUGIN_CLASS@@',   $PluginClass)
$programCs = $programCs.Replace('@@VERSION@@',        $DbpVersion)
$programCs = $programCs.Replace('@@TARGET_VERSION@@', $TargetVersion)
$programCs = $programCs.Replace('@@HAS_ICON@@',       $(if ($HasIcon) { 'true' } else { 'false' }))
[System.IO.File]::WriteAllText($ProgramCsPath, $programCs, [System.Text.UTF8Encoding]::new($true))

$installerCsproj = [System.IO.File]::ReadAllText($InstallerCsprojPath, [System.Text.UTF8Encoding]::new($false))
$installerCsproj = $installerCsproj.Replace('@@INSTALLER_ASSEMBLY_NAME@@', "$ApiName-Installer")
$installerCsproj = $installerCsproj.Replace('@@INSTALLER_VERSION@@',       $DbpVersion)
[System.IO.File]::WriteAllText($InstallerCsprojPath, $installerCsproj, [System.Text.UTF8Encoding]::new($true))

# ----- Step 5: build the installer EXE -----

Write-Host ''
Write-Host 'Building installer EXE...'
Push-Location $StageDir
try {
    & dotnet build 'Installer.csproj' --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed. The placeholder substitution above probably broke a C# literal - inspect $StageDir\Program.cs."
    }
} finally {
    Pop-Location
}

$BuiltExe = Join-Path $StageDir "bin\Release\$ApiName-Installer.exe"
if (-not (Test-Path -LiteralPath $BuiltExe)) {
    throw "Installer build reported success but '$BuiltExe' is missing."
}

# ----- Step 6: stage the final artifact -----

if ([System.IO.Path]::IsPathRooted($Out)) {
    $OutDir = $Out
} else {
    $OutDir = Join-Path $PluginRoot $Out
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$FinalExe = Join-Path $OutDir "$ApiName-Installer-$DbpVersion.exe"
Copy-Item -LiteralPath $BuiltExe -Destination $FinalExe -Force

if (-not $NoUninstall) {
    $readme = Join-Path $OutDir 'README.txt'
    $banner = "$ApiName installer"
    $rule = '=' * $banner.Length
    $readmeText = @"
$banner
$rule

1. Close Cimatron if it's running.
2. Right-click $ApiName-Installer-$DbpVersion.exe -> Run as administrator.
3. The installer copies $ApiName.dll into the Cimatron Program folder
   and registers it in ExternalCommands.ini.
4. Launch Cimatron. The new command appears in the plugin's toolbar.

To uninstall: run the same EXE again with the /uninstall flag from an
elevated terminal:  $ApiName-Installer-$DbpVersion.exe /uninstall
"@
    [System.IO.File]::WriteAllText($readme, $readmeText, [System.Text.UTF8Encoding]::new($false))
}

# ----- Step 7: report -----

$sizeKb = [math]::Round(((Get-Item -LiteralPath $FinalExe).Length / 1KB), 1)
$targetSummary = if ($TargetVersion -eq 'any') { 'any installed Cimatron >= 2024.0' } else { "Cimatron $TargetVersion" }

Write-Host ''
Write-Host '--- Installer built ---'
Write-Host "  Path:     $FinalExe"
Write-Host "  Size:     $sizeKb KB"
Write-Host "  Plugin:   $PluginClass"
Write-Host "  Target:   $targetSummary"
Write-Host ''
Write-Host 'Reminder: the end-user must close Cimatron and run the EXE as Administrator.'
