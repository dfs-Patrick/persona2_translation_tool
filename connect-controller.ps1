$ErrorActionPreference = "Stop"

if (-not (Get-Command usbipd -ErrorAction SilentlyContinue)) {
    Write-Error "usbipd-win nao esta instalado. Instale com: winget install --exact dorssel.usbipd-win"
    exit 1
}

$patterns = @(
    "controller", "gamepad", "joystick", "xbox", "playstation",
    "dualshock", "dualsense", "8bitdo", "switch pro", "joy-con", "steam controller"
)

$devices = @(usbipd list | Select-Object -Skip 2 | ForEach-Object {
    if ($_ -match '^\s*(\S+)\s+([0-9a-fA-F]{4}:[0-9a-fA-F]{4})\s+(.+?)\s{2,}(Not shared|Shared|Attached|Busy)\s*$') {
        $description = $Matches[3].Trim()
        if ($patterns | Where-Object { $description -match $_ }) {
            [pscustomobject]@{
                BusId = $Matches[1]
                Description = $description
                State = $Matches[4]
            }
        }
    }
})

if ($devices.Count -eq 0) {
    Write-Error "Nenhum controle USB foi encontrado. Conecte o controle por USB e tente novamente."
    exit 1
}

if ($devices.Count -eq 1) {
    $device = $devices[0]
} else {
    Write-Host "Controles encontrados:"
    for ($i = 0; $i -lt $devices.Count; $i++) {
        Write-Host "[$($i + 1)] $($devices[$i].Description) [$($devices[$i].State)]"
    }
    $choice = Read-Host "Escolha um controle"
    $index = 0
    if (-not [int]::TryParse($choice, [ref]$index) -or $index -lt 1 -or $index -gt $devices.Count) {
        Write-Error "Escolha invalida."
        exit 1
    }
    $device = $devices[$index - 1]
}

Write-Host "Controle selecionado: $($device.Description)"
if ($device.State -eq "Not shared") {
    Write-Host "Preparando o dispositivo para o WSL..."
    usbipd bind --busid $device.BusId
}

usbipd attach --wsl --busid $device.BusId
Write-Host "Controle conectado ao WSL. Abra o PPSSPP novamente."