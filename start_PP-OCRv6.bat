@echo off
setlocal
cd /d "%~dp0"

set LUNA_OCR_MODEL_DIR=%~dp0ocr_models\PP-OCRv6_medium
set LUNA_OCR_PORT=8871
set LUNA_OCR_GPU=1
set LUNA_OCR_THREADS=4

if not exist "%~dp0files\DLL64\CVUtils.dll" (
    echo Error: Local files\DLL64\CVUtils.dll not found.
    goto failed
)

if not exist "%~dp0files\runtime31264\PyQt5" (
    echo Error: Local files\runtime31264\PyQt5 not found.
    goto failed
)

if not exist "%LUNA_OCR_MODEL_DIR%\det.onnx" (
    echo Error: Model files not found in %LUNA_OCR_MODEL_DIR%
    goto failed
)

set PYTHONPATH=%~dp0files\runtime31264;%PYTHONPATH%
set PATH=%~dp0files\DLL64;%~dp0files\runtime31264;%PATH%

echo Starting Portable LunaOCR service on http://127.0.0.1:8871/ocr
echo Model: %LUNA_OCR_MODEL_DIR%
python PP-OCRv6_server.py
goto end

:failed
echo.
echo Portable LunaOCR setup failed.
pause

:end
endlocal
