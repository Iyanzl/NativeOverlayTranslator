@echo off
setlocal
cd /d "%~dp0"

if not exist ".venv-paddle\Scripts\python.exe" (
    echo Missing .venv-paddle. Run start_paddleocr.bat once first.
    pause
    goto end
)

call ".venv-paddle\Scripts\activate.bat"

set PYTHONPATH=D:\AI\envs\shared-torch-cu130\Lib\site-packages;%PYTHONPATH%
set HF_HOME=D:\AI\models\huggingface
set TRANSFORMERS_CACHE=D:\AI\models\huggingface
set TORCH_HOME=D:\AI\models\torch
set MANGA_OCR_PORT=8870

python -c "import manga_ocr" >nul 2>nul
if errorlevel 1 (
    echo Installing MangaOCR wrapper and lightweight dependencies. Torch is loaded from shared-torch-cu130.
    python -m pip install manga-ocr --no-deps
    if errorlevel 1 goto failed
    python -m pip install -r requirements-mangaocr.txt
    if errorlevel 1 goto failed
)

echo Starting MangaOCR service on http://127.0.0.1:8870/ocr
python manga_ocr_server.py
goto end

:failed
echo.
echo MangaOCR service setup failed.
pause

:end
endlocal
