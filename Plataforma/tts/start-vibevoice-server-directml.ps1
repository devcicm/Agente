# ============================================================================
# Script PowerShell para iniciar VibeVoice TTS con DirectML Multi-GPU
# Soporta: DirectML (AMD/Intel/NVIDIA), CUDA (solo NVIDIA), CPU
# ============================================================================

[CmdletBinding()]
param(
    [string]$Model = $env:VIBEVOICE_MODEL,
    [int]$Port = 3000,
    [ValidateSet('directml', 'cuda', 'cpu', 'auto')]
    [string]$Device = 'auto',
    [int]$GpuIndex = -1,
    [switch]$ListGpus,
    [switch]$AutoClone,
    [switch]$Help
)

# Configurar encoding UTF-8 para PowerShell
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

# Colores para output
$ErrorColor = "Red"
$SuccessColor = "Green"
$InfoColor = "Cyan"
$WarnColor = "Yellow"

function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White",
        [string]$Prefix = ""
    )

    if ($Prefix) {
        Write-Host $Prefix -NoNewline -ForegroundColor $Color
        Write-Host " $Message"
    } else {
        Write-Host $Message -ForegroundColor $Color
    }
}

function Show-Help {
    Write-Host @"

Script para iniciar el servidor VibeVoice TTS con soporte Multi-GPU

USO:
  .\start-vibevoice-server-directml.ps1 [OPTIONS]

OPCIONES:
  -Model <string>       Modelo a usar (default: microsoft/VibeVoice-Realtime-0.5B)
  -Port <int>           Puerto del servidor (default: 3000)
  -Device <string>      Dispositivo: directml, cuda, cpu, auto (default: auto)
  -GpuIndex <int>       Índice de GPU a usar (0=integrada, 1=dedicada)
  -ListGpus             Listar GPUs disponibles y salir
  -AutoClone            Clonar VibeVoice automáticamente si no existe
  -Help                 Mostrar esta ayuda

VARIABLES DE ENTORNO:
  VIBEVOICE_MODEL       Modelo a usar
  VIBEVOICE_PORT        Puerto del servidor
  VIBEVOICE_DEVICE      Dispositivo (directml/cuda/cpu/auto)
  DIRECTML_DEVICE       Índice de GPU para DirectML (0, 1, etc.)

EJEMPLOS:
  # Auto-detectar mejor dispositivo
  .\start-vibevoice-server-directml.ps1

  # Listar GPUs disponibles
  .\start-vibevoice-server-directml.ps1 -ListGpus

  # Usar DirectML con GPU dedicada (AMD Radeon)
  .\start-vibevoice-server-directml.ps1 -Device directml -GpuIndex 1

  # Usar DirectML con GPU integrada (Intel UHD)
  .\start-vibevoice-server-directml.ps1 -Device directml -GpuIndex 0

  # Usar CUDA (solo NVIDIA)
  .\start-vibevoice-server-directml.ps1 -Device cuda

  # Usar CPU
  .\start-vibevoice-server-directml.ps1 -Device cpu

  # Puerto personalizado
  .\start-vibevoice-server-directml.ps1 -Port 3001

  # Con variables de entorno
  `$env:VIBEVOICE_DEVICE = "directml"
  `$env:DIRECTML_DEVICE = "1"
  .\start-vibevoice-server-directml.ps1

NOTAS:
  - DirectML funciona con cualquier marca de GPU (AMD, Intel, NVIDIA)
  - CUDA solo funciona con GPUs NVIDIA
  - En modo 'auto', se selecciona automáticamente el mejor dispositivo
  - GpuIndex 0 suele ser GPU integrada, 1 suele ser GPU dedicada

"@
    exit 0
}

if ($Help) {
    Show-Help
}

# Banner
Write-Host ""
Write-ColorOutput "============================================" -Color $InfoColor
Write-ColorOutput " VibeVoice TTS Server - DirectML Multi-GPU" -Color $InfoColor
Write-ColorOutput "============================================" -Color $InfoColor
Write-Host ""

# Verificar Python
Write-ColorOutput "Verificando Python..." -Color $InfoColor
try {
    $pythonVersion = python --version 2>&1
    Write-ColorOutput "[OK]" -Color $SuccessColor -Prefix "[OK]"
    Write-Host "     $pythonVersion"
} catch {
    Write-ColorOutput "[ERROR]" -Color $ErrorColor -Prefix "[ERROR]"
    Write-Host "Python no está instalado o no está en PATH"
    Write-Host ""
    Write-Host "Instala Python 3.9+ desde: https://www.python.org/downloads/"
    exit 1
}
Write-Host ""

# Verificar VibeVoice
$vibevoicePath = Join-Path $PSScriptRoot "..\..\repo\VibeVoice"
if (-not (Test-Path $vibevoicePath)) {
    Write-ColorOutput "[WARN]" -Color $WarnColor -Prefix "[WARN]"
    Write-Host "VibeVoice no encontrado en: $vibevoicePath"
    Write-Host ""

    if ($AutoClone) {
        Write-ColorOutput "Clonando VibeVoice automáticamente..." -Color $InfoColor

        # Verificar Git
        try {
            $gitVersion = git --version 2>&1
            Write-ColorOutput "[OK]" -Color $SuccessColor -Prefix "[OK]"
            Write-Host "     $gitVersion"
        } catch {
            Write-ColorOutput "[ERROR]" -Color $ErrorColor -Prefix "[ERROR]"
            Write-Host "Git no está instalado. Clona manualmente:"
            Write-Host "  cd ..\..\repo"
            Write-Host "  git clone https://github.com/microsoft/VibeVoice.git"
            exit 1
        }

        # Crear directorio repo
        $repoPath = Join-Path $PSScriptRoot "..\..\repo"
        if (-not (Test-Path $repoPath)) {
            New-Item -ItemType Directory -Path $repoPath | Out-Null
        }

        # Clonar
        Push-Location $repoPath
        try {
            git clone https://github.com/microsoft/VibeVoice.git
            Write-ColorOutput "[OK]" -Color $SuccessColor -Prefix "[OK]"
            Write-Host "VibeVoice clonado exitosamente"
        } catch {
            Write-ColorOutput "[ERROR]" -Color $ErrorColor -Prefix "[ERROR]"
            Write-Host "Falló al clonar VibeVoice"
            Pop-Location
            exit 1
        }
        Pop-Location
    } else {
        Write-Host "Clona el repositorio con:"
        Write-Host "  cd ..\..\repo"
        Write-Host "  git clone https://github.com/microsoft/VibeVoice.git"
        Write-Host ""
        Write-Host "O ejecuta con -AutoClone para clonar automáticamente"
        exit 1
    }
} else {
    Write-ColorOutput "[OK]" -Color $SuccessColor -Prefix "[OK]"
    Write-Host "VibeVoice encontrado"
}
Write-Host ""

# Listar GPUs si se solicita
if ($ListGpus) {
    Write-ColorOutput "Detectando GPUs disponibles..." -Color $InfoColor
    Write-Host ""
    python (Join-Path $PSScriptRoot "detect-gpus.py")
    exit 0
}

# Configuración
if (-not $Model) {
    $Model = "microsoft/VibeVoice-Realtime-0.5B"
}

if ($env:VIBEVOICE_PORT) {
    $Port = [int]$env:VIBEVOICE_PORT
}

if ($env:VIBEVOICE_DEVICE) {
    $Device = $env:VIBEVOICE_DEVICE
}

Write-ColorOutput "Configuración:" -Color $InfoColor
Write-Host "  - Modelo:  $Model"
Write-Host "  - Puerto:  $Port"
Write-Host "  - Device:  $Device"
if ($GpuIndex -ge 0) {
    Write-Host "  - GPU:     $GpuIndex"
}
Write-Host ""

# Instalar dependencias en primera ejecución
$venvPath = Join-Path $PSScriptRoot "..\..\venv"
if (-not (Test-Path $venvPath)) {
    Write-ColorOutput "[INFO]" -Color $InfoColor -Prefix "[INFO]"
    Write-Host "Primera ejecución - Instalando dependencias VibeVoice..."
    Write-Host "Esto puede tomar 5-10 minutos..."
    Write-Host ""

    Push-Location $vibevoicePath
    try {
        pip install -e . 2>&1 | Where-Object { $_ -notmatch "WARNING" }
        Write-ColorOutput "[OK]" -Color $SuccessColor -Prefix "[OK]"
        Write-Host "Dependencias instaladas correctamente"
    } catch {
        Write-ColorOutput "[WARN]" -Color $WarnColor -Prefix "[WARN]"
        Write-Host "Algunas advertencias durante instalación - continuando..."
    }
    Pop-Location
    Write-Host ""
}

# Verificar dependencias del servidor
Write-ColorOutput "[INFO]" -Color $InfoColor -Prefix "[INFO]"
Write-Host "Verificando dependencias del servidor..."
try {
    pip show fastapi | Out-Null
} catch {
    Write-ColorOutput "[INFO]" -Color $InfoColor -Prefix "[INFO]"
    Write-Host "Instalando fastapi y uvicorn..."
    pip install fastapi "uvicorn[standard]" websockets
}
Write-Host ""

# Verificar DirectML si se solicita
if ($Device -eq 'directml' -or $Device -eq 'auto') {
    Write-ColorOutput "[INFO]" -Color $InfoColor -Prefix "[INFO]"
    Write-Host "Verificando torch-directml..."

    try {
        python -c "import torch_directml" 2>&1 | Out-Null
        Write-ColorOutput "[OK]" -Color $SuccessColor -Prefix "[OK]"
        Write-Host "torch-directml instalado"
    } catch {
        Write-ColorOutput "[WARN]" -Color $WarnColor -Prefix "[WARN]"
        Write-Host "torch-directml no está instalado"
        Write-Host ""
        Write-Host "Para usar DirectML con tu GPU AMD/Intel, ejecuta:"
        Write-Host "  pip uninstall torch torchvision torchaudio"
        Write-Host "  pip install torch-directml"
        Write-Host ""

        if ($Device -eq 'directml') {
            Write-ColorOutput "[ERROR]" -Color $ErrorColor -Prefix "[ERROR]"
            Write-Host "DirectML solicitado pero no está instalado. Saliendo."
            exit 1
        } else {
            Write-ColorOutput "[INFO]" -Color $InfoColor -Prefix "[INFO]"
            Write-Host "Continuando con CPU..."
            $Device = 'cpu'
        }
    }
    Write-Host ""
}

# Cambiar al directorio demo
$demoPath = Join-Path $vibevoicePath "demo"
Push-Location $demoPath

# Configurar variables de entorno para el servidor
$env:VIBEVOICE_MODEL = $Model
$env:VIBEVOICE_PORT = $Port
$env:VIBEVOICE_DEVICE = $Device

if ($GpuIndex -ge 0) {
    $env:DIRECTML_DEVICE = $GpuIndex
}

# Información del servidor
Write-ColorOutput "Iniciando servidor..." -Color $InfoColor
Write-Host "  URL:       http://localhost:$Port"
Write-Host "  WebSocket: ws://localhost:$Port/stream"
Write-Host "  Health:    http://localhost:$Port/config"
Write-Host ""

if ($Device -eq 'directml' -or $Device -eq 'auto') {
    Write-ColorOutput "DirectML Multi-GPU:" -Color $InfoColor
    Write-Host "  - Soporta GPUs AMD, Intel y NVIDIA"
    if ($GpuIndex -ge 0) {
        Write-Host "  - GPU seleccionada: $GpuIndex"
    } else {
        Write-Host "  - Auto-selección de GPU activada"
    }
    Write-Host ""
}

Write-ColorOutput "Nota:" -Color $WarnColor
Write-Host "  Algunos warnings son normales y no afectan la funcionalidad"
Write-Host ""
Write-Host "Presiona Ctrl+C para detener el servidor"
Write-ColorOutput "============================================" -Color $InfoColor
Write-Host ""

# Iniciar servidor con script DirectML
$serverScript = Join-Path $PSScriptRoot "run-vibevoice-server-directml.py"
try {
    python $serverScript
} catch {
    Pop-Location
    Write-Host ""
    Write-ColorOutput "============================================" -Color $ErrorColor
    Write-ColorOutput "[ERROR] El servidor falló al iniciar" -Color $ErrorColor
    Write-ColorOutput "============================================" -Color $ErrorColor
    Write-Host ""
    Write-Host "Posibles causas:"
    Write-Host "  1. Puerto $Port ya en uso"
    Write-Host "     Solución: .\start-vibevoice-server-directml.ps1 -Port 3001"
    Write-Host ""
    Write-Host "  2. DirectML no instalado correctamente"
    Write-Host "     Solución: pip install torch-directml"
    Write-Host ""
    Write-Host "  3. Modelo no descargado"
    Write-Host "     Se descarga automáticamente - requiere internet"
    Write-Host ""
    Write-Host "  4. GPU no soportada"
    Write-Host "     Solución: .\start-vibevoice-server-directml.ps1 -Device cpu"
    Write-Host ""
    exit 1
}

Pop-Location
