@echo off
setlocal
cd /d "%~dp0"

set LUNA_TRANSLATOR_HOME=D:\Program Files\LunaTranslator
set LUNA_OCR_MODEL_DIR=%~dp0ocr_models\luna-default-scale5
set LUNA_OCR_PORT=8871
set LUNA_OCR_GPU=1
set LUNA_OCR_THREADS=4

if not exist "%LUNA_TRANSLATOR_HOME%\files\DLL64\CVUtils.dll" (
    echo LunaTranslator CVUtils.dll was not found:
    echo %LUNA_TRANSLATOR_HOME%\files\DLL64\CVUtils.dll
    goto failed
)

if not exist "%LUNA_TRANSLATOR_HOME%\files\runtime31264\PyQt5" (
    echo LunaTranslator Python 3.12 Qt runtime was not found:
    echo %LUNA_TRANSLATOR_HOME%\files\runtime31264
    goto failed
)

if not exist "%LUNA_OCR_MODEL_DIR%\det.onnx" (
    echo LunaOCR model was not found:
    echo %LUNA_OCR_MODEL_DIR%
    goto failed
)

set PYTHONPATH=%LUNA_TRANSLATOR_HOME%\LunaTranslator;%LUNA_TRANSLATOR_HOME%\files\runtime31264;%PYTHONPATH%
set PATH=%LUNA_TRANSLATOR_HOME%\files\DLL64;%LUNA_TRANSLATOR_HOME%\files\runtime31264;%PATH%

echo Starting LunaOCR service on http://127.0.0.1:8871/ocr
echo Model: %LUNA_OCR_MODEL_DIR%
python luna_ocr_server.py
goto end

:failed
echo.
echo LunaOCR service setup failed.
pause

:end
endlocal
