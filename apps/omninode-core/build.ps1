param(
    [string]$Compiler = $env:CC
)

$ErrorActionPreference = "Stop"

$Root = $PSScriptRoot
$Source = Join-Path $Root "src/main.c"
$IsWindowsHost = $PSVersionTable.Platform -eq "Win32NT" -or [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
$OutputName = if ($IsWindowsHost) { "omninode_core.exe" } else { "omninode_core" }
$Output = Join-Path $Root $OutputName

function Test-Command {
    param([string]$Name)
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

if ([string]::IsNullOrWhiteSpace($Compiler)) {
    if ($IsWindowsHost -and (Test-Command "cl")) {
        $Compiler = "cl"
    } elseif (Test-Command "cc") {
        $Compiler = "cc"
    } elseif (Test-Command "gcc") {
        $Compiler = "gcc"
    } else {
        throw "C 컴파일러를 찾지 못했습니다. Windows에서는 Visual Studio Build Tools(cl) 또는 gcc를 설치하세요."
    }
}

Write-Host "[Omni-node] core build compiler=$Compiler output=$OutputName"

if ($Compiler -ieq "cl") {
    if (-not $IsWindowsHost) {
        throw "cl 빌드는 Windows에서만 지원합니다."
    }

    & $Compiler /nologo /O2 /W3 $Source "/Fe:$Output" ws2_32.lib
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    exit 0
}

$CompilerArgs = @("-std=c11", "-Wall", "-Wextra", "-O2", "-o", $Output, $Source)
if ($IsWindowsHost) {
    $CompilerArgs += "-lws2_32"
}

& $Compiler @CompilerArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
