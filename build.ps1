<#
.SYNOPSIS
  Builds the single shippable nplus.exe.

.DESCRIPTION
  Produces ONE file: dist\nplus.exe — a Native AOT launcher that ensures the
  .NET 8 Desktop Runtime is installed (prompting + downloading it if needed),
  then runs the real WinForms app embedded inside it.

  Steps:
    1. Publish the WinForms app as a framework-dependent single file
       (native Scintilla/Lexilla DLLs are embedded inside it already).
    2. Stage that exe as the launcher's embedded payload.
    3. Publish the Native AOT launcher, embedding the payload.
    4. Copy the result to dist\nplus.exe.

.EXAMPLE
  .\build.ps1
#>
param(
    [string]$Configuration = "Release",
    [string]$Rid = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# Native AOT links with the MSVC toolchain. Bring the Visual Studio developer
# environment (link.exe, INCLUDE/LIB, vswhere on PATH) into this session so the
# AOT publish in step 3 can find it. No-op if it's already set up.
function Enter-DevEnvironment {
    if ($env:VSCMD_VER) { return }  # already inside a VS dev shell

    $installerDir = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
    $vswhere = Join-Path $installerDir "vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        throw "vswhere.exe not found. Install the Visual Studio C++ build tools (Desktop development with C++)."
    }

    # The Native AOT link target shells out to a bare `vswhere.exe`; make sure the
    # VS Installer directory that contains it is on PATH for the build.
    if (($env:PATH -split ';') -notcontains $installerDir) {
        $env:PATH = "$installerDir;$env:PATH"
    }

    $vsPath = & $vswhere -latest -prerelease -products * `
        -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        -property installationPath
    if (-not $vsPath) { throw "No VS install with the VC++ tools (Microsoft.VisualStudio.Component.VC.Tools.x86.x64) was found." }

    $devShell = Join-Path $vsPath "Common7\Tools\Microsoft.VisualStudio.DevShell.dll"
    Import-Module $devShell
    Enter-VsDevShell -VsInstallPath $vsPath -SkipAutomaticLocation `
        -DevCmdArguments "-arch=x64 -host_arch=x64" | Out-Null
    # Enter-VsDevShell changes the working dir; restore it.
    Set-Location $root
}

Write-Host "[1/4] Publishing WinForms app (framework-dependent single file)..." -ForegroundColor Cyan
dotnet publish "$root\nplus.csproj" -c $Configuration -r $Rid
if ($LASTEXITCODE -ne 0) { throw "App publish failed." }

$appExe = Join-Path $root "bin\$Configuration\net8.0-windows\$Rid\publish\nplus.exe"
if (-not (Test-Path $appExe)) { throw "Expected app exe not found: $appExe" }

Write-Host "[2/4] Staging payload for the launcher..." -ForegroundColor Cyan
$payloadDir = Join-Path $root "Bootstrap\payload"
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null
Copy-Item $appExe (Join-Path $payloadDir "nplus.app.bin") -Force

Write-Host "[3/4] Publishing Native AOT launcher (embeds payload)..." -ForegroundColor Cyan
Enter-DevEnvironment
dotnet publish "$root\Bootstrap\nplus.bootstrap.csproj" -c $Configuration -r $Rid
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed." }

$launcher = Join-Path $root "Bootstrap\bin\$Configuration\net8.0\$Rid\publish\nplus.exe"
if (-not (Test-Path $launcher)) { throw "Expected launcher exe not found: $launcher" }

Write-Host "[4/4] Copying to dist..." -ForegroundColor Cyan
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$final = Join-Path $dist "nplus.exe"
Copy-Item $launcher $final -Force

$sizeMB = [math]::Round((Get-Item $final).Length / 1MB, 1)
Write-Host ""
Write-Host "Done. Single-file build -> $final ($sizeMB MB)" -ForegroundColor Green
