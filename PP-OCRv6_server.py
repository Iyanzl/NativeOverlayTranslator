from __future__ import annotations

import base64
import json
import os
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any



ROOT_DIR = Path(__file__).resolve().parent
DEFAULT_MODEL_DIR = ROOT_DIR / "ocr_models" / "PP-OCRv6_medium"

_ocr_lock = threading.Lock()
_ocr: Any | None = None
_qimage_type: Any | None = None


def _get_ocr() -> Any:
    global _ocr, _qimage_type
    with _ocr_lock:
        if _ocr is not None:
            return _ocr

        from portable_luna_ocr import LocalOCR, QImage

        model_dir = Path(os.environ.get("LUNA_OCR_MODEL_DIR", str(DEFAULT_MODEL_DIR)))
        det_path = model_dir / "det.onnx"
        rec_path = model_dir / "rec.onnx"
        dict_path = model_dir / "dict.txt"
        missing = [path for path in (det_path, rec_path, dict_path) if not path.exists()]
        if missing:
            raise FileNotFoundError(
                "LunaOCR model files were not found: " + ", ".join(str(path) for path in missing)
            )

        thread_count = int(os.environ.get("LUNA_OCR_THREADS", "4"))
        use_gpu = os.environ.get("LUNA_OCR_GPU", "1").strip().lower() not in {"0", "false", "no"}
        luid = int(os.environ.get("LUNA_OCR_LUID", "0"))
        device_type = os.environ.get("LUNA_OCR_DEVICE_TYPE", "CPU")

        _qimage_type = QImage
        _ocr = LocalOCR(str(det_path), str(rec_path), str(dict_path), thread_count, use_gpu, luid, device_type)
        print(
            f"LunaOCR ready model='{model_dir}' gpu={use_gpu} threads={thread_count}",
            flush=True,
        )
        return _ocr


def _bounds_from_points(points: tuple[float, float, float, float, float, float, float, float]) -> list[float]:
    xs = [float(points[0]), float(points[2]), float(points[4]), float(points[6])]
    ys = [float(points[1]), float(points[3]), float(points[5]), float(points[7])]
    left = min(xs)
    top = min(ys)
    right = max(xs)
    bottom = max(ys)
    return [left, top, max(1.0, right - left), max(1.0, bottom - top)]


def _polygon_from_points(points: tuple[float, float, float, float, float, float, float, float]) -> list[list[float]]:
    return [
        [float(points[0]), float(points[1])],
        [float(points[2]), float(points[3])],
        [float(points[4]), float(points[5])],
        [float(points[6]), float(points[7])],
    ]


def recognize(image_bytes: bytes, file_name: str) -> list[dict[str, Any]]:
    ocr = _get_ocr()
    if _qimage_type is None:
        raise RuntimeError("LunaOCR QImage runtime was not initialized.")

    suffix = Path(file_name).suffix
    if suffix.lower() not in {".png", ".jpg", ".jpeg", ".bmp", ".webp"}:
        suffix = ".png"

    with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as image_file:
        image_file.write(image_bytes)
        image_path = image_file.name

    try:
        image = _qimage_type(image_path)
        if image.isNull():
            raise ValueError("LunaOCR could not load the request image.")

        boxes, texts = ocr.OcrDetect(image, 0)
        lines: list[dict[str, Any]] = []
        for index, raw_text in enumerate(texts):
            text = str(raw_text).strip()
            if not text:
                continue

            points = tuple(float(value) for value in boxes[index])
            lines.append(
                {
                    "text": text,
                    "confidence": 0.90,
                    "box": _bounds_from_points(points),
                    "points": _polygon_from_points(points),
                }
            )
        return lines
    finally:
        try:
            os.remove(image_path)
        except OSError:
            pass


class LunaOcrHandler(BaseHTTPRequestHandler):
    server_version = "NativeOverlayLunaOCR/1.0"

    def do_GET(self) -> None:
        if self.path == "/health":
            try:
                _get_ocr()
                self._write_json(200, {"ok": True})
            except Exception as exc:
                self._write_json(500, {"ok": False, "error": str(exc)})
            return

        self._write_json(404, {"error": "not found"})

    def do_POST(self) -> None:
        if self.path != "/ocr":
            self._write_json(404, {"error": "not found"})
            return

        started = time.perf_counter()
        try:
            length = int(self.headers.get("Content-Length", "0"))
            raw_body = self.rfile.read(length).decode("utf-8") if length > 0 else ""
            if not raw_body.strip():
                self._write_json(400, {"error": "Empty OCR request body"})
                return

            payload = json.loads(raw_body)
            image_base64 = payload.get("Image_Base64") or payload.get("image_base64") or payload.get("image")
            if not image_base64:
                self._write_json(400, {"error": "Image_Base64 is required"})
                return

            file_name = payload.get("FileName") or payload.get("file_name") or "image.png"
            lines = recognize(base64.b64decode(image_base64), str(file_name))
            elapsed = time.perf_counter() - started
            print(f"[LunaOCR] recognized lines={len(lines)} elapsed={elapsed:.3f}s", flush=True)
            self._write_json(200, {"lines": lines})
        except Exception as exc:
            self._write_json(500, {"error": str(exc)})

    def log_message(self, format: str, *args: Any) -> None:
        print(f"[LunaOCR] {self.address_string()} - {format % args}", flush=True)

    def _write_json(self, status: int, payload: dict[str, Any]) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def main() -> None:
    host = os.environ.get("LUNA_OCR_HOST", "127.0.0.1")
    port = int(os.environ.get("LUNA_OCR_PORT", "8871"))
    server = ThreadingHTTPServer((host, port), LunaOcrHandler)
    print(f"LunaOCR server listening on http://{host}:{port}/ocr", flush=True)
    print(f"Health check: http://{host}:{port}/health", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
