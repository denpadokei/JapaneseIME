# Builds JapaneseIME.KanjiNative.dll with the MSVC toolchain.
# Called from Japanese_IME.csproj; skips quietly when no C++ toolchain exists,
# because the plugin can fall back to JapaneseIME.KanjiHost.exe.
[CmdletBinding()]
param(
    [string]$OutputDir,
    [string]$Configuration = 'Release',
    [ValidateSet('x64', 'x86')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot 'KanjiNative.cpp'
if (-not $OutputDir) {
    $OutputDir = Join-Path $PSScriptRoot "bin\$Configuration"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$vcvarsName = if ($Platform -eq 'x86') { 'vcvars32.bat' } else { 'vcvars64.bat' }

function Find-VcVars {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $root = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($root) {
            $candidate = Join-Path $root.Trim() "VC\Auxiliary\Build\$vcvarsName"
            if (Test-Path $candidate) {
                return $candidate
            }
        }
    }

    $roots = @(
        'C:\Program Files\Microsoft Visual Studio',
        'C:\Program Files (x86)\Microsoft Visual Studio'
    )

    foreach ($root in $roots) {
        if (-not (Test-Path $root)) {
            continue
        }

        foreach ($edition in Get-ChildItem $root -Directory -ErrorAction SilentlyContinue) {
            $candidate = Join-Path $edition.FullName "VC\Auxiliary\Build\$vcvarsName"
            if (Test-Path $candidate) {
                return $candidate
            }
        }
    }

    return $null
}

$vcvars = Find-VcVars
if (-not $vcvars) {
    Write-Warning 'MSVC was not found; JapaneseIME.KanjiNative.dll is not built and the helper process is used instead.'
    exit 0
}

$dll = Join-Path $OutputDir 'JapaneseIME.KanjiNative.dll'
$objects = Join-Path $OutputDir "native-obj-$Platform"
New-Item -ItemType Directory -Force -Path $objects | Out-Null

$optimize = if ($Configuration -eq 'Debug') { '/Od /Zi' } else { '/O2' }

$command = 'call "{0}" >nul && cl /nologo /utf-8 /W4 /EHsc /MT /DUNICODE /D_UNICODE {1} /LD "{2}" /Fo:"{3}\\" /Fe:"{4}" /link /INCREMENTAL:NO' -f $vcvars, $optimize, $source, $objects, $dll
& $env:ComSpec /c $command
if ($LASTEXITCODE -ne 0) {
    throw "cl.exe failed with exit code $LASTEXITCODE"
}

Remove-Item (Join-Path $OutputDir 'JapaneseIME.KanjiNative.lib') -ErrorAction SilentlyContinue
Remove-Item (Join-Path $OutputDir 'JapaneseIME.KanjiNative.exp') -ErrorAction SilentlyContinue

Write-Host "JapaneseIME: built $dll"
