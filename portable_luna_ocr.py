from __future__ import annotations

import locale
import os
import sys
from ctypes import (
    CDLL,
    CFUNCTYPE,
    c_bool,
    c_char_p,
    c_float,
    c_int,
    c_int32,
    c_uint64,
    c_void_p,
    c_wchar_p,
)
from pathlib import Path
from typing import Any


ROOT_DIR = Path(__file__).resolve().parent
_DLL_DIRECTORY_HANDLES: list[Any] = []


def runtime_paths() -> tuple[Path, Path]:
    return ROOT_DIR / "files" / "runtime31264", ROOT_DIR / "files" / "DLL64"


def _configure_runtime() -> Path:
    runtime_dir, dll_dir = runtime_paths()
    cvutils_path = dll_dir / "CVUtils.dll"
    missing = [path for path in (runtime_dir, dll_dir, cvutils_path) if not path.exists()]
    if missing:
        raise FileNotFoundError(
            "Portable LunaOCR runtime files were not found: " + ", ".join(str(path) for path in missing)
        )

    runtime_text = str(runtime_dir)
    if runtime_text not in sys.path:
        sys.path.insert(0, runtime_text)

    os.environ["PATH"] = str(dll_dir) + os.pathsep + runtime_text + os.pathsep + os.environ.get("PATH", "")
    if hasattr(os, "add_dll_directory"):
        _DLL_DIRECTORY_HANDLES.append(os.add_dll_directory(str(dll_dir)))
        _DLL_DIRECTORY_HANDLES.append(os.add_dll_directory(runtime_text))

    return cvutils_path


_CVUTILS_PATH = _configure_runtime()

from PyQt5.QtGui import QImage  # noqa: E402


_CVUTILS = CDLL(str(_CVUTILS_PATH))
_OCR_CALLBACK = CFUNCTYPE(
    None,
    c_float,
    c_float,
    c_float,
    c_float,
    c_float,
    c_float,
    c_float,
    c_float,
    c_char_p,
)
_ERROR_CALLBACK = CFUNCTYPE(None, c_char_p)


class LocalOCR:
    def __init__(
        self,
        det: str,
        rec: str,
        key: str,
        thread: int,
        gpu: bool,
        luid: int,
        device_type: str,
    ) -> None:
        load_runtime = _CVUTILS.OcrLoadRuntime
        load_runtime.argtypes = ()
        load_runtime.restype = c_bool
        if not load_runtime():
            raise RuntimeError("CVUtils could not load the local ONNX Runtime.")

        self._ocr_init = _CVUTILS.OcrInit
        self._ocr_init.argtypes = (
            c_wchar_p,
            c_wchar_p,
            c_wchar_p,
            c_int32,
            c_bool,
            c_uint64,
            c_char_p,
            _ERROR_CALLBACK,
        )
        self._ocr_init.restype = c_void_p

        self._ocr_detect = _CVUTILS.OcrDetect
        self._ocr_detect.argtypes = (c_void_p, c_void_p, c_int32, _OCR_CALLBACK, _ERROR_CALLBACK)

        self._ocr_destroy = _CVUTILS.OcrDestroy
        self._ocr_destroy.argtypes = (c_void_p,)

        errors: list[bytes] = []
        error_callback = _ERROR_CALLBACK(errors.append)
        self._handle = self._ocr_init(
            det,
            rec,
            key,
            thread,
            gpu,
            luid,
            device_type.encode("utf-8"),
            error_callback,
        )
        if errors:
            raise RuntimeError(_decode_error(errors[0]))
        if not self._handle:
            raise RuntimeError("CVUtils failed to initialize the OCR model.")

    def OcrDetect(self, image: QImage, mode: int) -> tuple[list[tuple[float, ...]], list[str]]:
        if image.isNull():
            raise ValueError("Cannot run OCR on an empty image.")

        rgb_image = image
        if image.format() != QImage.Format_RGB888:
            rgb_image = image.convertToFormat(QImage.Format_RGB888)

        from_rgb = _CVUTILS.cvMatFromRGB888
        from_rgb.argtypes = (c_void_p, c_int, c_int, c_int)
        from_rgb.restype = c_void_p
        mat = from_rgb(
            int(rgb_image.bits()),
            rgb_image.width(),
            rgb_image.height(),
            rgb_image.bytesPerLine(),
        )
        if not mat:
            raise RuntimeError("CVUtils could not create an image matrix.")

        destroy_mat = _CVUTILS.cvMatDestroy
        destroy_mat.argtypes = (c_void_p,)
        texts: list[str] = []
        boxes: list[tuple[float, ...]] = []

        def on_result(
            x1: float,
            y1: float,
            x2: float,
            y2: float,
            x3: float,
            y3: float,
            x4: float,
            y4: float,
            text: bytes,
        ) -> None:
            boxes.append((x1, y1, x2, y2, x3, y3, x4, y4))
            texts.append(text.decode("utf-8"))

        errors: list[bytes] = []
        result_callback = _OCR_CALLBACK(on_result)
        error_callback = _ERROR_CALLBACK(errors.append)
        try:
            self._ocr_detect(self._handle, mat, mode, result_callback, error_callback)
        finally:
            destroy_mat(mat)

        if errors:
            raise RuntimeError(_decode_error(errors[0]))
        return boxes, texts

    def close(self) -> None:
        handle = getattr(self, "_handle", None)
        if handle:
            self._ocr_destroy(handle)
            self._handle = None

    def __del__(self) -> None:
        try:
            self.close()
        except Exception:
            pass


def _decode_error(error: bytes) -> str:
    return error.decode(locale.getpreferredencoding(False), errors="replace")
