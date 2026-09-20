# Windows host launcher. Docker runs the tool; PPSSPP runs on Windows.
[CmdletBinding()]
param(
    [ValidateSet("setup", "extract", "rebuild", "run", "rebuild-run")]
    [string]$Action = "rebuild",
    [string]$Font = "",
    [string]$PpssppPath = $env:P2_PPSSPP
)

$ErrorActionPreference = "Stop"
$projectDir = Split-Path -Parent $PSScriptRoot
$labDir = Join-Path $projectDir "lab"
$isoPath = Join-Path $labDir "p2is-translated.iso"
$statePath = Join-Path $labDir ".ppsspp-session.json"
$composePath = Join-Path $projectDir "compose.yaml"
$workflowLock = $null

function Invoke-ToolDocker {
    param([string[]]$DockerArguments)
    & docker compose --project-directory $projectDir -f $composePath @DockerArguments
    if ($LASTEXITCODE -ne 0) { throw "Docker terminou com codigo $LASTEXITCODE. Consulte a saida acima." }
}

function Stop-OwnedEmulator {
    if (-not (Test-Path -LiteralPath $statePath)) { return }
    $session = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $emulator = Get-Process -Id $session.id -ErrorAction SilentlyContinue
    if ($emulator -and $emulator.StartTime.ToUniversalTime().Ticks.ToString() -eq $session.started -and
        $emulator.Path -eq $session.executable) {
        # Close only the instance previously started by this launcher.
        $null = $emulator.CloseMainWindow()
        if (-not $emulator.WaitForExit(5000)) {
            throw "Feche a instancia do PPSSPP iniciada pela ferramenta e tente novamente."
        }
    }
    Remove-Item -LiteralPath $statePath
}

function Start-Emulator {
    if (-not (Test-Path -LiteralPath $isoPath -PathType Leaf)) { throw "ISO nao encontrada: $isoPath" }
    $emulator = Start-Process -FilePath $script:PpssppPath -ArgumentList "`"$isoPath`"" `
        -WorkingDirectory (Split-Path -Parent $script:PpssppPath) -PassThru
    # A launcher that delegates to an already running instance may exit at once.
    if (-not $emulator.HasExited) {
        @{
            id = $emulator.Id
            started = $emulator.StartTime.ToUniversalTime().Ticks.ToString()
            executable = $script:PpssppPath
        } | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
    }
}

try {
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $labDir "iso")
    # Separate invocations (including an extension) must never rebuild in parallel.
    try {
        $workflowLock = [System.IO.File]::Open((Join-Path $labDir ".tool-workflow.lock"),
            [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    } catch { throw "Outra operacao esta usando este projeto. Aguarde o termino antes de tentar novamente." }

    if ($Action -in @("run", "rebuild-run")) {
        if (-not $PpssppPath -or -not (Test-Path -LiteralPath $PpssppPath -PathType Leaf)) {
            throw "Informe -PpssppPath com o caminho de PPSSPPWindows64.exe, ou configure P2_PPSSPP."
        }
        $script:PpssppPath = (Resolve-Path -LiteralPath $PpssppPath).Path
    }
    if ($Action -ne "run") {
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
            throw "Instale e inicie o Docker Desktop com containers Linux antes de continuar."
        }
        $engine = & docker info --format '{{.OSType}}'
        if ($LASTEXITCODE -ne 0 -or $engine -ne "linux") {
            throw "O Docker Desktop precisa estar iniciado e usando containers Linux."
        }
    }

    if ($Action -eq "setup") {
        Invoke-ToolDocker -DockerArguments @("build", "tool")
        Write-Host "Ferramenta pronta. Coloque sua ISO em lab/iso/p2is.iso e execute -Action extract."
        Write-Host "Pasta para adicionar a ISO pelo Explorador de Arquivos: $(Join-Path $labDir 'iso')"
    } elseif ($Action -eq "extract") {
        if (Test-Path -LiteralPath (Join-Path $labDir "translation/en/new/messages")) {
            throw "Ja existe uma traducao neste projeto. Use outro workspace para extrair sem sobrescrever seus TBFs."
        }
        Invoke-ToolDocker -DockerArguments @("run", "--rm", "--no-deps", "-T", "tool", "extractAll",
            "lab/iso/p2is.iso", "-o", "lab/dump", "--translation-output", "lab/translation/en",
            "--game", "is", "--variant", "us", "--locale", "en")
    } else {
        # Release the ISO file before rebuilding, including on Windows.
        Stop-OwnedEmulator
        if ($Action -ne "run") {
            $arguments = @("run", "--rm", "--no-deps", "-T", "tool", "rebuildTbf",
                "lab/iso/p2is.iso", "lab/translation/en", "--game", "is", "--variant", "us", "--locale", "en")
            if ($Font) { $arguments += @("--font", $Font) }
            Invoke-ToolDocker -DockerArguments $arguments
            Write-Host "ISO criada: $isoPath"
        }
        if ($Action -in @("run", "rebuild-run")) { Start-Emulator }
    }
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
} finally {
    if ($workflowLock) { $workflowLock.Dispose() }
}
