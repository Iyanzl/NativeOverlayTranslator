@echo off
setlocal
cd /d "%~dp0"
set PADDLE_PDX_MODEL_SOURCE=bos
set PADDLE_PDX_ENABLE_MKLDNN_BYDEFAULT=False
set FLAGS_use_mkldnn=0

if not exist ".venv-paddle\Scripts\python.exe" (
    echo Creating PaddleOCR virtual environment...
    python -m venv .venv-paddle
    if errorlevel 1 goto failed
)

call ".venv-paddle\Scripts\activate.bat"

python -c "import paddleocr, paddle" >nul 2>nul
if errorlevel 1 (
    echo Installing PaddleOCR dependencies. This can take a while...
    python -m pip install --upgrade pip
    if errorlevel 1 goto failed
    python -m pip install -r requirements-paddleocr.txt
    if errorlevel 1 goto failed
)

echo Starting PaddleOCR service on http://127.0.0.1:8868/ocr
python paddle_ocr_server.py
goto end

:failed
echo.
echo PaddleOCR service setup failed.
pause

:end
endlocal
