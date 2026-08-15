# QPRE ThetaData Engine - PowerShell Installer
# Instalación completa y automatizada para MultiCharts.NET

param(
    [string]$BaseDir = "C:\PYGEX",
    [string]$MultiChartsPath = "C:\Program Files (x86)\MC.NET\bin64\MC.NET.exe"
)

# Colores
$Colors = @{
    Success = "Green"
    Error = "Red"
    Warning = "Yellow"
    Info = "Cyan"
}

function Write-Status {
    param(
        [string]$Message,
        [string]$Status = "Info"
    )
    $Color = $Colors[$Status]
    if ($Status -eq "Info") {
        Write-Host "ℹ️  $Message" -ForegroundColor $Color
    } elseif ($Status -eq "Success") {
        Write-Host "✓ $Message" -ForegroundColor $Color
    } elseif ($Status -eq "Error") {
        Write-Host "✗ $Message" -ForegroundColor $Color
    } elseif ($Status -eq "Warning") {
        Write-Host "⚠ $Message" -ForegroundColor $Color
    }
}

function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Header
Clear-Host
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     QPRE ThetaData Engine - Installer (PowerShell)        ║" -ForegroundColor Cyan
Write-Host "║                    Versión 1.0                             ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Check Administrator
Write-Status "Verificando permisos de administrador..." "Info"
if (-not (Test-Administrator)) {
    Write-Status "Este instalador debe ejecutarse como Administrador" "Error"
    Write-Host ""
    Write-Host "Solución:" -ForegroundColor Yellow
    Write-Host "1. Abre PowerShell como Administrador"
    Write-Host "2. Ejecuta: Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser"
    Write-Host "3. Ejecuta este script nuevamente"
    Write-Host ""
    Read-Host "Presiona Enter para salir"
    exit 1
}
Write-Status "Permisos de administrador confirmados" "Success"
Write-Host ""

# Step 1: Create directories
Write-Status "STEP 1/7: Creando estructura de carpetas..." "Info"
if (-not (Test-Path $BaseDir)) {
    New-Item -ItemType Directory -Path $BaseDir -Force | Out-Null
    Write-Status "Carpeta creada: $BaseDir" "Success"
} else {
    Write-Status "Carpeta ya existe: $BaseDir" "Success"
}

$MultiChartsIndicators = "$env:APPDATA\MC.NET\PowerLanguage\Indicators"
if (-not (Test-Path $MultiChartsIndicators)) {
    New-Item -ItemType Directory -Path $MultiChartsIndicators -Force | Out-Null
    Write-Status "Carpeta MultiCharts creada: $MultiChartsIndicators" "Success"
} else {
    Write-Status "Carpeta MultiCharts existe: $MultiChartsIndicators" "Success"
}
Write-Host ""

# Step 2: Check Python
Write-Status "STEP 2/7: Verificando Python..." "Info"
$PythonVersion = python --version 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Status "Python encontrado: $PythonVersion" "Success"
} else {
    Write-Status "Python NO está instalado o no está en PATH" "Error"
    Write-Host ""
    Write-Host "Solución:" -ForegroundColor Yellow
    Write-Host "1. Descarga Python desde: https://www.python.org/downloads/"
    Write-Host "2. Durante la instalación, MARCA: 'Add Python to PATH'"
    Write-Host "3. Reinicia PowerShell y ejecuta este script de nuevo"
    Write-Host ""
    Read-Host "Presiona Enter para salir"
    exit 1
}
Write-Host ""

# Step 3: Install Python dependencies
Write-Status "STEP 3/7: Instalando dependencias Python..." "Info"
Write-Status "  • pandas" "Info"
Write-Status "  • requests" "Info"

$output = python -m pip install pandas requests --quiet 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Status "Dependencias instaladas correctamente" "Success"
} else {
    Write-Status "Error instalando dependencias" "Warning"
    Write-Host "Intentando nuevamente con pip upgrade..." -ForegroundColor Yellow
    python -m pip install --upgrade pip --quiet 2>&1
    python -m pip install pandas requests --quiet 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Status "Dependencias instaladas en segundo intento" "Success"
    }
}
Write-Host ""

# Step 4: Download from GitHub
Write-Status "STEP 4/7: Descargando archivos del repositorio..." "Info"

$RepoUrl = "https://raw.githubusercontent.com/ElektroMotion/QPRE-ThetaData-Engine/main"
$Files = @{
    "thetadata_gex_engine.py" = "$BaseDir\thetadata_gex_engine.py"
    "QPRE_ThetaData_MC.cs" = "$MultiChartsIndicators\QPRE_ThetaData_MC.cs"
}

foreach ($File in $Files.GetEnumerator()) {
    $FileName = $File.Key
    $TargetPath = $File.Value
    
    try {
        $SourceUrl = "$RepoUrl/$FileName"
        Write-Status "  Descargando $FileName..." "Info"
        
        $webClient = New-Object System.Net.WebClient
        $webClient.DownloadFile($SourceUrl, $TargetPath)
        
        Write-Status "  ✓ $FileName descargado" "Success"
    } catch {
        Write-Status "  Advertencia: No se pudo descargar $FileName" "Warning"
        Write-Host "  Deberás descargarlo manualmente desde:" -ForegroundColor Yellow
        Write-Host "  $RepoUrl/$FileName" -ForegroundColor Yellow
    }
}
Write-Host ""

# Step 5: Create batch scripts
Write-Status "STEP 5/7: Creando scripts de inicio..." "Info"

# start_engine.bat
$StartEngineContent = @"
@echo off
title QPRE ThetaData Engine - Running...
cd /d "$BaseDir"
python thetadata_gex_engine.py
pause
"@
$StartEngineContent | Out-File -FilePath "$BaseDir\start_engine.bat" -Encoding ASCII -Force
Write-Status "Creado: start_engine.bat" "Success"

# run_all.bat (Python + MultiCharts)
$RunAllContent = @"
@echo off
title QPRE ThetaData Engine - Launcher
echo.
echo ╔════════════════════════════════════════════════════════════╗
echo ║     QPRE ThetaData Engine - Starting...                   ║
echo ╚════════════════════════════════════════════════════════════╝
echo.
echo [1/2] Iniciando Python Engine...
start "ThetaData Engine" "$BaseDir\start_engine.bat"
timeout /t 3 /nobreak
echo.
echo [2/2] Abriendo MultiCharts.NET...
start "" "$MultiChartsPath"
echo.
echo ✓ Motor QPRE iniciado correctamente.
timeout /t 2
"@
$RunAllContent | Out-File -FilePath "$BaseDir\run_all.bat" -Encoding ASCII -Force
Write-Status "Creado: run_all.bat" "Success"

Write-Host ""

# Step 6: Create desktop shortcuts
Write-Status "STEP 6/7: Creando accesos directos en Escritorio..." "Info"

$DesktopPath = "$env:USERPROFILE\Desktop"

# Shortcut para run_all.bat
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut1 = $WshShell.CreateShortcut("$DesktopPath\QPRE Engine Start.lnk")
$Shortcut1.TargetPath = "$BaseDir\run_all.bat"
$Shortcut1.IconLocation = "C:\Windows\System32\cmd.exe"
$Shortcut1.WorkingDirectory = $BaseDir
$Shortcut1.Save()
Write-Status "Creado: QPRE Engine Start.lnk" "Success"

# Shortcut para carpeta de indicadores
$Shortcut2 = $WshShell.CreateShortcut("$DesktopPath\Indicadores MultiCharts.lnk")
$Shortcut2.TargetPath = $MultiChartsIndicators
$Shortcut2.Save()
Write-Status "Creado: Indicadores MultiCharts.lnk" "Success"

# Shortcut para carpeta PYGEX
$Shortcut3 = $WshShell.CreateShortcut("$DesktopPath\PYGEX Data Folder.lnk")
$Shortcut3.TargetPath = $BaseDir
$Shortcut3.Save()
Write-Status "Creado: PYGEX Data Folder.lnk" "Success"

Write-Host ""

# Step 7: Create comprehensive guide
Write-Status "STEP 7/7: Creando guía de instalación..." "Info"

$GuideContent = @"
╔════════════════════════════════════════════════════════════╗
║     QPRE ThetaData Engine - Guía de Post-Instalación      ║
╚════════════════════════════════════════════════════════════╝

✓ INSTALACIÓN COMPLETADA CORRECTAMENTE

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📁 CARPETAS CREADAS:

Base Directory:        $BaseDir
MultiCharts Indics:    $MultiChartsIndicators
Desktop Shortcuts:     $DesktopPath

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🚀 PRÓXIMOS PASOS (IMPORTANTE):

1. VERIFICAR THETADATA API
   ✓ Asegúrate que ThetaData esté corriendo en puerto 25510
   ✓ Prueba: http://127.0.0.1:25510/v2/bulk_snapshot/option/greeks?root=AAPL&exp=0

2. COMPILAR INDICADOR EN MULTICHARTS
   ✓ Abre MultiCharts.NET
   ✓ Tools → PowerEditor
   ✓ File → Open → Selecciona QPRE_ThetaData_MC.cs
   ✓ Tools → Compile (Ctrl+Shift+B)
   ✓ Verifica que no haya errores

3. USAR EN GRÁFICO
   ✓ Abre un gráfico en MultiCharts
   ✓ Click derecho → Add Study
   ✓ Busca "QPRE_ThetaData_MC"
   ✓ Click OK

4. INICIAR EL MOTOR
   ✓ Opción A (Simple): Doble click en "start_engine.bat"
   ✓ Opción B (Automatizado): Doble click en "run_all.bat"

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📊 ARCHIVOS DEL SISTEMA:

En $BaseDir:
  • thetadata_gex_engine.py .......... Script Python (motor)
  • start_engine.bat ................ Inicia Python engine
  • run_all.bat ..................... Inicia Python + MultiCharts
  • active_symbol.txt ............... Símbolo actual (auto-generado)
  • mc_levels.json .................. Datos GEX (auto-generado)

En $MultiChartsIndicators:
  • QPRE_ThetaData_MC.cs ............ Indicador C# compilado

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

🔧 TROUBLESHOOTING:

❌ Python no encontrado
   → Instala desde: https://www.python.org/downloads/
   → MARCA: "Add Python to PATH"
   → Reinicia PowerShell

❌ No se puede descargar desde GitHub
   → Descarga manualmente desde:
   → https://github.com/ElektroMotion/QPRE-ThetaData-Engine
   → Copia los archivos en las carpetas correspondientes

❌ Indicador no aparece en MultiCharts
   → Recompila: Tools → Compile
   → Verifica que esté en: $MultiChartsIndicators

❌ No se actualizan datos en gráfico
   → Verifica que Python está corriendo (consola visible)
   → Cambia símbolo en el gráfico para triggear actualización
   → Verifica que mc_levels.json se está actualizando

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📋 INFORMACIÓN DEL SISTEMA:

Python Version:        $PythonVersion
Base Directory:        $BaseDir
MultiCharts Indicators: $MultiChartsIndicators
Desktop Shortcuts:     $DesktopPath

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

💡 TIPS ÚTILES:

• En Escritorio tienes 3 accesos directos:
  1. "QPRE Engine Start.lnk" → Inicia todo automáticamente
  2. "Indicadores MultiCharts.lnk" → Abre carpeta de indicadores
  3. "PYGEX Data Folder.lnk" → Abre carpeta de datos

• Para actualizar, descarga los nuevos archivos y cópialos
  en las carpetas correspondientes

• Logs de Python se muestran en la consola en tiempo real

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✓ ¡Instalación lista! Haz doble click en el acceso directo
  del Escritorio "QPRE Engine Start.lnk" para comenzar.

"@

$GuideContent | Out-File -FilePath "$BaseDir\POST_INSTALL_GUIDE.txt" -Encoding UTF8 -Force
Write-Status "Creado: POST_INSTALL_GUIDE.txt" "Success"

Write-Host ""

# Final summary
Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║             ✓ INSTALACIÓN COMPLETADA                      ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""

Write-Host "📁 Carpetas:" -ForegroundColor Cyan
Write-Host "   Base:       $BaseDir"
Write-Host "   Indicadores: $MultiChartsIndicators"
Write-Host "   Escritorio: $DesktopPath"
Write-Host ""

Write-Host "📌 Accesos directos creados en tu Escritorio:" -ForegroundColor Cyan
Write-Host "   1. QPRE Engine Start.lnk          (Inicia todo)"
Write-Host "   2. Indicadores MultiCharts.lnk    (Carpeta de indicadores)"
Write-Host "   3. PYGEX Data Folder.lnk          (Datos)"
Write-Host ""

Write-Host "📖 Próximos pasos:" -ForegroundColor Yellow
Write-Host "   1. Abre POST_INSTALL_GUIDE.txt en $BaseDir"
Write-Host "   2. Compila el indicador en MultiCharts (Tools → Compile)"
Write-Host "   3. Haz doble click en 'QPRE Engine Start.lnk' del Escritorio"
Write-Host ""

Write-Host "📚 Documentación completa:" -ForegroundColor Cyan
Write-Host "   https://github.com/ElektroMotion/QPRE-ThetaData-Engine"
Write-Host ""

Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║           Presiona Enter para finalizar                    ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Green

Read-Host ""
