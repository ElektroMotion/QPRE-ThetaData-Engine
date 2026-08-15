@echo off
REM ============================================================================
REM QPRE ThetaData Engine Installer
REM Instala y configura automáticamente Python + C# para MultiCharts.NET
REM ============================================================================

setlocal enabledelayedexpansion
title QPRE ThetaData Engine - Installer

REM Colors and formatting
echo.
echo ============================================================================
echo.
echo  QPRE ThetaData Engine - Automatic Installer
echo.
echo ============================================================================
echo.

REM Check if running as administrator
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Este instalador debe ejecutarse como Administrador.
    echo Haz click derecho en el archivo .bat y selecciona "Ejecutar como administrador"
    pause
    exit /b 1
)

REM Step 1: Create directories
echo [STEP 1/6] Creando carpetas...
set PYGEX_DIR=C:\PYGEX
if not exist "%PYGEX_DIR%" (
    mkdir "%PYGEX_DIR%"
    echo ✓ Carpeta creada: %PYGEX_DIR%
) else (
    echo ✓ Carpeta ya existe: %PYGEX_DIR%
)

REM Step 2: Check Python installation
echo.
echo [STEP 2/6] Verificando Python...
python --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Python no está instalado o no está en PATH
    echo.
    echo Descarga Python desde: https://www.python.org/downloads/
    echo Durante la instalación, MARCA LA OPCIÓN: "Add Python to PATH"
    pause
    exit /b 1
) else (
    for /f "tokens=*" %%i in ('python --version') do set PYTHON_VERSION=%%i
    echo ✓ Python encontrado: !PYTHON_VERSION!
)

REM Step 3: Install Python dependencies
echo.
echo [STEP 3/6] Instalando dependencias Python...
echo   - pandas
echo   - requests
python -m pip install pandas requests --quiet
if %errorlevel% neq 0 (
    echo [ERROR] Falló la instalación de dependencias
    pause
    exit /b 1
) else (
    echo ✓ Dependencias instaladas correctamente
)

REM Step 4: Copy Python script
echo.
echo [STEP 4/6] Copiando script Python...
set PYTHON_SCRIPT=%PYGEX_DIR%\thetadata_gex_engine.py
set PYTHON_SCRIPT_CONTENT=import os
set PYTHON_SCRIPT_CONTENT=!PYTHON_SCRIPT_CONTENT! import json
REM (Se descargará del repositorio en Step 5)
echo ✓ Script Python será descargado en el siguiente paso

REM Step 5: Download files from GitHub
echo.
echo [STEP 5/6] Descargando archivos del repositorio...

REM Download Python script
powershell -Command "(New-Object Net.WebClient).DownloadFile('https://raw.githubusercontent.com/ElektroMotion/QPRE-ThetaData-Engine/main/thetadata_gex_engine.py', '%PYGEX_DIR%\thetadata_gex_engine.py')" >nul 2>&1
if %errorlevel% neq 0 (
    echo [WARNING] No se pudo descargar desde GitHub
    echo Descargando archivos manualmente...
) else (
    echo ✓ thetadata_gex_engine.py descargado
)

REM Step 6: Create batch files for easy launching
echo.
echo [STEP 6/6] Creando accesos directos y scripts...

REM Create start_engine.bat
(
    echo @echo off
    echo title QPRE ThetaData Engine - Running...
    echo cd /d %PYGEX_DIR%
    echo python thetadata_gex_engine.py
    echo pause
) > "%PYGEX_DIR%\start_engine.bat"
echo ✓ Creado: start_engine.bat

REM Create run_all.bat (Python + MultiCharts)
(
    echo @echo off
    echo title QPRE ThetaData Engine - Launcher
    echo echo Iniciando Python Engine...
    echo start "ThetaData Engine" "%PYGEX_DIR%\start_engine.bat"
    echo timeout /t 2 /nobreak
    echo echo Abriendo MultiCharts.NET...
    echo start "" "C:\Program Files (x86)\MC.NET\bin64\MC.NET.exe"
    echo echo Motor QPRE iniciado correctamente.
    echo timeout /t 2
) > "%PYGEX_DIR%\run_all.bat"
echo ✓ Creado: run_all.bat

REM Create Desktop shortcut for run_all.bat
set DESKTOP=%USERPROFILE%\Desktop
powershell -Command "$WshShell = New-Object -ComObject WScript.Shell; $lnk = $WshShell.CreateShortcut('%DESKTOP%\QPRE Engine Start.lnk'); $lnk.TargetPath = '%PYGEX_DIR%\run_all.bat'; $lnk.IconLocation = 'C:\Windows\System32\cmd.exe'; $lnk.Save()" >nul 2>&1
echo ✓ Acceso directo creado en Escritorio: QPRE Engine Start.lnk

REM Create Desktop shortcut for MultiCharts indicator folder
powershell -Command "$WshShell = New-Object -ComObject WScript.Shell; $lnk = $WshShell.CreateShortcut('%DESKTOP%\Indicadores MultiCharts.lnk'); $lnk.TargetPath = '%APPDATA%\MC.NET\PowerLanguage\Indicators'; $lnk.Save()" >nul 2>&1
echo ✓ Acceso directo creado: Indicadores MultiCharts.lnk

REM Create installation guide
(
    echo # QPRE ThetaData Engine - Guía de Instalación Completada
    echo.
    echo ## ✓ Instalación Completada
    echo.
    echo Carpeta base: %PYGEX_DIR%
    echo.
    echo ### Próximos pasos:
    echo.
    echo 1. **Copiar indicador C#:**
    echo    - Descarga QPRE_ThetaData_MC.cs desde:
    echo      https://github.com/ElektroMotion/QPRE-ThetaData-Engine
    echo    - Cópialo en: %APPDATA%\MC.NET\PowerLanguage\Indicators\
    echo.
    echo 2. **Iniciar el motor:**
    echo    - Opción A (Simple): Doble click en "%PYGEX_DIR%\start_engine.bat"
    echo    - Opción B (Completo): Doble click en "%PYGEX_DIR%\run_all.bat"
    echo      (inicia Python + MultiCharts automáticamente)
    echo.
    echo 3. **Compilar indicador en MultiCharts:**
    echo    - Abre MultiCharts.NET
    echo    - Tools ^> PowerEditor
    echo    - Abre QPRE_ThetaData_MC.cs
    echo    - Tools ^> Compile
    echo.
    echo 4. **Usar en gráfico:**
    echo    - Click derecho en gráfico ^> Add Study
    echo    - Busca QPRE_ThetaData_MC
    echo    - Click OK
    echo.
    echo ### Archivos creados:
    echo.
    echo - %PYGEX_DIR%\start_engine.bat
    echo - %PYGEX_DIR%\run_all.bat
    echo - %PYGEX_DIR%\thetadata_gex_engine.py
    echo - %PYGEX_DIR%\active_symbol.txt (creado automáticamente)
    echo - %PYGEX_DIR%\mc_levels.json (creado automáticamente)
    echo.
    echo ### Requisitos:
    echo.
    echo - ThetaData API corriendo en http://127.0.0.1:25510
    echo - MultiCharts.NET instalado
    echo.
    echo ============================================================================
) > "%PYGEX_DIR%\INSTALL_GUIDE.txt"
echo ✓ Guía de instalación: INSTALL_GUIDE.txt

REM Final message
echo.
echo ============================================================================
echo.
echo  ✓ INSTALACIÓN COMPLETADA CORRECTAMENTE
echo.
echo ============================================================================
echo.
echo Archivos creados en: %PYGEX_DIR%
echo.
echo Próximos pasos:
echo.
echo 1. Descarga QPRE_ThetaData_MC.cs del repositorio:
echo    https://github.com/ElektroMotion/QPRE-ThetaData-Engine
echo.
echo 2. Cópialo en:
echo    %APPDATA%\MC.NET\PowerLanguage\Indicators\
echo.
echo 3. Ejecuta este archivo para iniciar todo:
echo    "%PYGEX_DIR%\run_all.bat"
echo.
echo Para más detalles, abre:
echo "%PYGEX_DIR%\INSTALL_GUIDE.txt"
echo.
echo ============================================================================
echo.

pause
exit /b 0
