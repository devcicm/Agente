# ============================================================================
# Script PowerShell para iniciar el servidor VibeVoice TTS (Windows)
# Usa run-vibevoice-server.py con pyshim para compatibilidad torch.xpu
# ============================================================================

[CmdletBinding()]
param(
    [string]$Model = $env:VIBEVOICE_MODEL,
    [int]$Port = 3000,
    [ValidateSet('cuda', 'cpu', 'auto')]
    [string]$Device = 'cpu',
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

Script para iniciar el servidor VibeVoice TTS

USO:
  .\start-vibevoice-server.ps1 [OPTIONS]

OPCIONES:
  -Model <string>       Modelo a usar (default: microsoft/VibeVoice-Realtime-0.5B)
  -Port <int>           Puerto del servidor (default: 3000)
  -Device <string>      Dispositivo: cuda, cpu, auto (default: cpu)
  -AutoClone            Clonar VibeVoice automáticamente si no existe
  -Help                 Mostrar esta ayuda

VARIABLES DE ENTORNO:
  VIBEVOICE_MODEL       Modelo a usar
  VIBEVOICE_PORT        Puerto del servidor
  VIBEVOICE_DEVICE      Dispositivo (cuda/cpu)

EJEMPLOS:
  # Básico (CPU)
  .\start-vibevoice-server.ps1

  # Con CUDA
  .\start-vibevoice-server.ps1 -Device cuda

  # Puerto personalizado
  .\start-vibevoice-server.ps1 -Port 3001

  # Con variables de entorno
  `$env:VIBEVOICE_DEVICE = "cuda"
  .\start-vibevoice-server.ps1

"@
    exit 0
}

if ($Help) {
    Show-Help
}

# Banner
Write-Host ""
Write-ColorOutput "====================================" -Color $InfoColor
Write-ColorOutput " Iniciando Servidor VibeVoice TTS" -Color $InfoColor
Write-ColorOutput "====================================" -Color $InfoColor
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

# Auto-detectar CUDA
if ($Device -eq 'auto') {
    Write-ColorOutput "Detectando capacidades CUDA..." -Color $InfoColor
    try {
        $cudaCheck = python -c "import torch; print('cuda' if torch.cuda.is_available() else 'cpu')" 2>&1
        $Device = $cudaCheck.Trim()
        Write-ColorOutput "[OK]" -Color $SuccessColor -Prefix "[OK]"
        Write-Host "Detectado: $Device"
    } catch {
        $Device = 'cpu'
        Write-ColorOutput "[WARN]" -Color $WarnColor -Prefix "[WARN]"
        Write-Host "No se pudo detectar CUDA, usando CPU"
    }
}

Write-ColorOutput "Configuración:" -Color $InfoColor
Write-Host "  - Modelo:  $Model"
Write-Host "  - Puerto:  $Port"
Write-Host "  - Device:  $Device"
Write-Host ""

# Cambiar al directorio demo
$demoPath = Join-Path $vibevoicePath "demo"
Push-Location $demoPath

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

# Configurar PYTHONPATH para pyshim
$pyshimPath = Join-Path $PSScriptRoot "pyshim"
$env:PYTHONPATH = "$pyshimPath;$env:PYTHONPATH"
Write-ColorOutput "[OK]" -Color $SuccessColor -Prefix "[OK]"
Write-Host "PYTHONPATH configurado con pyshim para compatibilidad torch.xpu"
Write-Host ""

# Configurar variables de entorno para el servidor
$env:VIBEVOICE_MODEL = $Model
$env:VIBEVOICE_PORT = $Port
$env:VIBEVOICE_DEVICE = $Device

# Información del servidor
Write-ColorOutput "Iniciando servidor..." -Color $InfoColor
Write-Host "  URL:       http://localhost:$Port"
Write-Host "  WebSocket: ws://localhost:$Port/stream"
Write-Host "  Health:    http://localhost:$Port/config"
Write-Host ""
Write-ColorOutput "Nota:" -Color $WarnColor
Write-Host "  Los siguientes warnings son normales y no afectan la funcionalidad:"
Write-Host "  - 'APEX FusedRMSNorm not available' - usa implementación nativa"
Write-Host "  - 'tokenizer class not the same type' - corregido automáticamente"
Write-Host ""
Write-Host "Presiona Ctrl+C para detener el servidor"
Write-ColorOutput "====================================" -Color $InfoColor
Write-Host ""

# Iniciar servidor
$serverScript = Join-Path $PSScriptRoot "run-vibevoice-server.py"
try {
    python $serverScript
} catch {
    Pop-Location
    Write-Host ""
    Write-ColorOutput "====================================" -Color $ErrorColor
    Write-ColorOutput "[ERROR] El servidor falló al iniciar" -Color $ErrorColor
    Write-ColorOutput "====================================" -Color $ErrorColor
    Write-Host ""
    Write-Host "Posibles causas:"
    Write-Host "  1. Puerto $Port ya en uso"
    Write-Host "     Solución: .\start-vibevoice-server.ps1 -Port 3001"
    Write-Host ""
    Write-Host "  2. GPU CUDA no disponible"
    Write-Host "     Solución: .\start-vibevoice-server.ps1 -Device cpu"
    Write-Host ""
    Write-Host "  3. Modelo no descargado"
    Write-Host "     Se descarga automáticamente - requiere internet"
    Write-Host ""
    Write-Host "  4. Dependencias faltantes"
    Write-Host "     Ejecuta: pip install -e . (en VibeVoice/)"
    Write-Host ""
    Write-Host "  5. torch.xpu incompatibilidad"
    Write-Host "     El pyshim debería solucionarlo automáticamente"
    Write-Host ""
    exit 1
}

Pop-Location
