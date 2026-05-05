param(
    [ValidateSet("start", "shutdown", "setup", "help")]
    [string]$Command = "start"
)

$ErrorActionPreference = "Stop"

$ScriptPath = $PSCommandPath
$ScriptDir = Split-Path -Parent $ScriptPath
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
$StateDir = Join-Path $HOME ".omninode\cli"
$PidFile = Join-Path $StateDir "middleware.pid"
$CorePidsFile = Join-Path $StateDir "core.pids"
$LogFile = Join-Path $StateDir "middleware.log"
$ErrLogFile = Join-Path $StateDir "middleware.err.log"
$MetaFile = Join-Path $StateDir "last-start.env"
$SetupMarkerFile = Join-Path $StateDir "setup-complete"
$MiddlewareProject = Join-Path $RepoRoot "apps\omninode-middleware\OmniNode.Middleware.csproj"
$CoreBinary = Join-Path $RepoRoot "apps\omninode-core\omninode_core.exe"
$DefaultPort = if ($env:OMNINODE_WS_PORT) { $env:OMNINODE_WS_PORT } else { "8080" }
$DefaultBaseUrl = "http://127.0.0.1:$DefaultPort"

Set-Location $RepoRoot

function Write-Info {
    param([string]$Message)
    Write-Host "[Omni-node] $Message"
}

function Stop-WithError {
    param([string]$Message)
    Write-Error "[Omni-node] $Message"
    exit 1
}

function Ensure-StateDir {
    New-Item -ItemType Directory -Force -Path $StateDir | Out-Null
}

function Test-CommandExists {
    param([string]$Name)
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Resolve-PowerShellCommand {
    if (Test-CommandExists "pwsh") {
        return "pwsh"
    }

    return "powershell"
}

function Resolve-PythonCommand {
    if (Test-CommandExists "python") {
        $result = & python --version 2>$null
        if ($LASTEXITCODE -eq 0 -and "$result" -match "Python 3") {
            return @{ File = "python"; Args = @() }
        }
    }

    if (Test-CommandExists "py") {
        & py -3 --version *> $null
        if ($LASTEXITCODE -eq 0) {
            return @{ File = "py"; Args = @("-3") }
        }
    }

    Stop-WithError "Python 3 실행 파일을 찾지 못했습니다. python 또는 py 런처를 PATH에 추가하세요."
}

function Invoke-Python {
    param(
        [hashtable]$Python,
        [string[]]$Arguments
    )

    & $Python.File @($Python.Args + $Arguments)
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

function Test-ProcessAlive {
    param([string]$PidText)
    if ([string]::IsNullOrWhiteSpace($PidText)) {
        return $false
    }

    try {
        Get-Process -Id ([int]$PidText) -ErrorAction Stop | Out-Null
        return $true
    } catch {
        return $false
    }
}

function Read-PidFile {
    if (Test-Path $PidFile) {
        return (Get-Content -Raw $PidFile).Trim()
    }
    return ""
}

function Test-HttpOk {
    param([string]$Path)
    try {
        $response = Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 -Uri "$DefaultBaseUrl$Path"
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 300
    } catch {
        return $false
    }
}

function Cleanup-StaleState {
    $pidText = Read-PidFile
    if ($pidText -and -not (Test-ProcessAlive $pidText)) {
        Remove-Item -Force -ErrorAction SilentlyContinue $PidFile, $CorePidsFile
    }
}

function Ensure-RequiredCommands {
    $missing = @()
    foreach ($tool in @("dotnet", "node", "npm")) {
        if (-not (Test-CommandExists $tool)) {
            $missing += $tool
        }
    }

    Resolve-PythonCommand | Out-Null

    if ($missing.Count -gt 0) {
        Stop-WithError "필수 도구가 없습니다: $($missing -join ', '). 먼저 scripts\Omni-node.ps1 setup을 실행하세요."
    }
}

function Test-SetupComplete {
    return (Test-Path $SetupMarkerFile) -and (Test-Path $CoreBinary) -and (Test-Path (Join-Path $RepoRoot "node_modules"))
}

function Write-SetupMarker {
    Ensure-StateDir
    @(
        "repo=$RepoRoot",
        "script=$ScriptPath",
        "completed_at=$(Get-Date -Format o)"
    ) | Set-Content -Encoding UTF8 -Path $SetupMarkerFile
}

function Install-CliWrapper {
    $binDir = Join-Path $HOME ".omninode\bin"
    New-Item -ItemType Directory -Force -Path $binDir | Out-Null
    $cmdPath = Join-Path $binDir "Omni-node.cmd"
    @"
@echo off
pwsh -NoProfile -ExecutionPolicy Bypass -File "$ScriptPath" %*
if %ERRORLEVEL% EQU 9009 powershell -NoProfile -ExecutionPolicy Bypass -File "$ScriptPath" %*
"@ | Set-Content -Encoding ASCII -Path $cmdPath
    Write-Info "명령 래퍼 생성 완료: $cmdPath"
    Write-Info "전역 실행이 필요하면 PATH에 추가하세요: $binDir"
}

function Invoke-Step {
    param(
        [string]$Label,
        [string]$FileName,
        [string[]]$Arguments
    )

    Write-Info $Label
    & $FileName @Arguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

function Run-Setup {
    Ensure-StateDir
    Ensure-RequiredCommands

    $python = Resolve-PythonCommand
    $powerShell = Resolve-PowerShellCommand
    Install-CliWrapper

    Invoke-Step "Node 의존성 설치" "npm" @("ci")
    Invoke-Step "코어 빌드" $powerShell @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $RepoRoot "apps\omninode-core\build.ps1"))
    Invoke-Step ".NET 빌드" "dotnet" @("build", "apps\omninode-middleware\OmniNode.Middleware.csproj")

    Write-Info "샌드박스 smoke"
    Invoke-Python $python @("apps\omninode-sandbox\executor.py", "--code", "print('ok')")

    Invoke-Step "통합 테스트" "npm" @("test")
    Write-SetupMarker
    Write-Info "setup 완료"
}

function Ensure-SetupIfNeeded {
    if (Test-SetupComplete) {
        return
    }

    Write-Info "첫 실행이거나 setup 상태가 없어 자동 setup을 시작합니다."
    Run-Setup
}

function Wait-ForReady {
    param([int]$Pid)
    for ($i = 0; $i -lt 60; $i++) {
        if (-not (Test-ProcessAlive "$Pid")) {
            Get-Content -Tail 40 -ErrorAction SilentlyContinue $LogFile, $ErrLogFile
            Stop-WithError "서버 프로세스가 준비 전에 종료됐습니다. 로그를 확인하세요: $LogFile"
        }

        if ((Test-HttpOk "/healthz") -and (Test-HttpOk "/readyz")) {
            return
        }

        Start-Sleep -Seconds 1
    }

    Get-Content -Tail 40 -ErrorAction SilentlyContinue $LogFile, $ErrLogFile
    Stop-WithError "서버가 $DefaultBaseUrl 에서 준비 상태가 되지 않았습니다. 로그: $LogFile"
}

function Write-StartMetadata {
    param([int]$Pid)
    @(
        "pid=$Pid",
        "port=$DefaultPort",
        "url=$DefaultBaseUrl/",
        "repo=$RepoRoot",
        "log=$LogFile",
        "stderr_log=$ErrLogFile",
        "started_at=$(Get-Date -Format o)"
    ) | Set-Content -Encoding UTF8 -Path $MetaFile
}

function Start-Server {
    Ensure-StateDir
    Cleanup-StaleState
    Ensure-SetupIfNeeded
    Ensure-RequiredCommands

    $currentPid = Read-PidFile
    if ((Test-ProcessAlive $currentPid) -and (Test-HttpOk "/healthz")) {
        Write-Info "이미 실행 중입니다. pid=$currentPid url=$DefaultBaseUrl/"
        return
    }

    if (-not (Test-Path $CoreBinary)) {
        Invoke-Step "코어 빌드" (Resolve-PowerShellCommand) @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $RepoRoot "apps\omninode-core\build.ps1"))
    }

    "`n[$(Get-Date -Format o)] Omni-node start" | Add-Content -Encoding UTF8 -Path $LogFile
    $env:OMNINODE_WORKSPACE_ROOT = Join-Path $RepoRoot "workspace\coding"
    $env:OMNINODE_ENABLE_LOCAL_OTP_FALLBACK = "1"
    $env:OMNINODE_GATEWAY_STARTUP_PROBE = "1"

    $process = Start-Process -FilePath "dotnet" `
        -ArgumentList @("run", "--project", $MiddlewareProject) `
        -WorkingDirectory $RepoRoot `
        -RedirectStandardOutput $LogFile `
        -RedirectStandardError $ErrLogFile `
        -WindowStyle Hidden `
        -PassThru

    Set-Content -Encoding ASCII -Path $PidFile -Value $process.Id
    Write-StartMetadata $process.Id

    Write-Info "서버 시작 중입니다. pid=$($process.Id)"
    Wait-ForReady $process.Id
    Write-Info "서버가 준비됐습니다."
    Write-Info "대시보드: $DefaultBaseUrl/"
    Write-Info "로그: $LogFile"
}

function Stop-RepoCoreProcesses {
    if (-not (Test-Path $CoreBinary)) {
        return
    }

    $corePath = (Resolve-Path $CoreBinary).Path
    $coreProcesses = Get-CimInstance Win32_Process -Filter "name = 'omninode_core.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.ExecutablePath -eq $corePath }

    foreach ($proc in $coreProcesses) {
        Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
        Write-Info "코어 종료 요청 완료 (pid=$($proc.ProcessId))"
    }
}

function Shutdown-Server {
    Ensure-StateDir
    Cleanup-StaleState

    $pidText = Read-PidFile
    if (-not $pidText) {
        Write-Info "종료할 서버 PID를 찾지 못했습니다."
        Stop-RepoCoreProcesses
        return
    }

    if (Test-ProcessAlive $pidText) {
        Stop-Process -Id ([int]$pidText) -Force -ErrorAction SilentlyContinue
        Write-Info "미들웨어 종료 완료 (pid=$pidText)"
    }

    Stop-RepoCoreProcesses
    Remove-Item -Force -ErrorAction SilentlyContinue $PidFile, $CorePidsFile
    Write-Info "Omni-node 종료 완료"
}

switch ($Command) {
    "start" { Start-Server }
    "shutdown" { Shutdown-Server }
    "setup" { Run-Setup }
    "help" {
        @"
사용법:
  scripts\Omni-node.ps1           서버 시작 (첫 실행이면 setup 자동 실행)
  scripts\Omni-node.ps1 shutdown  서버 종료
  scripts\Omni-node.ps1 setup     의존성 확인, 빌드, 검증
"@ | Write-Host
    }
}
