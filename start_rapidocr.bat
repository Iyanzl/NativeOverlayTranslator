@echo off
setlocal
cd /d "%~dp0"

if not exist ".venv-rapidocr\Scripts\python.exe" (
    echo Creating RapidOCR virtual environment...
    python -m venv .venv-rapidocr
    if errorlevel 1 goto failed
)

call ".venv-rapidocr\Scripts\activate.bat"
set PYTHONPATH=D:\AI\envs\shared-onnx-gpu\Lib\site-packages;%PYTHONPATH%

python -c "import rapidocr, onnxruntime" >nul 2>nul
if errorlevel 1 (
    echo Installing RapidOCR dependencies. ONNXRuntime GPU is loaded from shared-onnx-gpu.
    python -m pip install -r requirements-rapidocr.txt
    if errorlevel 1 goto failed
)

set OMP_NUM_THREADS=2
set OPENBLAS_NUM_THREADS=2
set MKL_NUM_THREADS=2
set NUMEXPR_NUM_THREADS=2
set RAPID_OCR_PORT=8869

echo Starting RapidOCR service on http://127.0.0.1:8869/ocr
python rapid_ocr_server.py
goto end

:failed
echo.
echo RapidOCR service setup failed.
pause

:end
endlocal
