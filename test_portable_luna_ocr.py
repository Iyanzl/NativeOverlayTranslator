from __future__ import annotations

import os
from pathlib import Path


ROOT_DIR = Path(__file__).resolve().parent
os.environ["LUNA_TRANSLATOR_HOME"] = str(ROOT_DIR / "missing-external-luna")

from portable_luna_ocr import LocalOCR, QImage, runtime_paths


runtime_dir, dll_dir = runtime_paths()
assert runtime_dir == ROOT_DIR / "files" / "runtime31264"
assert dll_dir == ROOT_DIR / "files" / "DLL64"

model_dir = ROOT_DIR / "ocr_models" / "luna-default-scale5"
ocr = LocalOCR(
    str(model_dir / "det.onnx"),
    str(model_dir / "rec.onnx"),
    str(model_dir / "dict.txt"),
    4,
    True,
    0,
    "CPU",
)
image = QImage(str(ROOT_DIR / "2.png"))
assert not image.isNull()
boxes, texts = ocr.OcrDetect(image, 0)
assert boxes
assert any("Force" in text for text in texts)
print("Portable LunaOCR test passed.")
