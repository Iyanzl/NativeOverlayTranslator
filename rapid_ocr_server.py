from __future__ import annotations

import base64
import json
import os
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any


os.environ.setdefault("OMP_NUM_THREADS", "2")
os.environ.setdefault("OPENBLAS_NUM_THREADS", "2")
os.environ.setdefault("MKL_NUM_THREADS", "2")
os.environ.setdefault("NUMEXPR_NUM_THREADS", "2")

_ocr_lock = threading.Lock()
_ocr: Any | None = None


def _get_ocr() -> Any:
    global _ocr
    with _ocr_lock:
        if _ocr is None:
            from rapidocr import RapidOCR

            _ocr = RapidOCR(
                params={
                    "Global.log_level": "error",
                    "Global.use_cls": False,
                    "Global.min_height": 8,
                    "Global.max_side_len": 960,
                    "EngineConfig.onnxruntime.intra_op_num_threads": 2,
                    "EngineConfig.onnxruntime.inter_op_num_threads": 1,
                    "EngineConfig.onnxruntime.enable_cpu_mem_arena": False,
                }
            )
        return _ocr


def _bounds_from_points(points: Any) -> list[float]:
    xs = [float(point[0]) for point in points]
    ys = [float(point[1]) for point in points]
    left = min(xs)
    top = min(ys)
    right = max(xs)
    bottom = max(ys)
    return [left, top, max(1.0, right - left), max(1.0, bottom - top)]


def recognize(image_bytes: bytes) -> list[dict[str, Any]]:
    with tempfile.NamedTemporaryFile(delete=False, suffix=".png") as image_file:
        image_file.write(image_bytes)
        image_path = image_file.name

    try:
        ocr = _get_ocr()
        result = ocr(image_path)
        if not result or result.boxes is None or result.txts is None:
            return []

        lines: list[dict[str, Any]] = []
        scores = result.scores or []
        for index, text in enumerate(result.txts):
            clean_text = str(text).strip()
            if not clean_text:
                continue

            points = [[float(point[0]), float(point[1])] for point in result.boxes[index]]
            score = float(scores[index]) if index < len(scores) else 0.8
            lines.append(
                {
                    "text": clean_text,
                    "confidence": score,
                    "box": _bounds_from_points(points),
                    "points": points,
                }
            )
        return lines
    finally:
        try:
            os.remove(image_path)
        except OSError:
            pass


class RapidOcrHandler(BaseHTTPRequestHandler):
    server_version = "NativeOverlayRapidOCR/1.0"

    def do_GET(self) -> None:
        if self.path == "/health":
            self._write_json(200, {"ok": True})
            return

        self._write_json(404, {"error": "not found"})

    def do_POST(self) -> None:
        if self.path != "/ocr":
            self._write_json(404, {"error": "not found"})
            return

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

            lines = recognize(base64.b64decode(image_base64))
            self._write_json(200, {"lines": lines})
        except Exception as exc:
            self._write_json(500, {"error": str(exc)})

    def log_message(self, format: str, *args: Any) -> None:
        print(f"[RapidOCR] {self.address_string()} - {format % args}")

    def _write_json(self, status: int, payload: dict[str, Any]) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def main() -> None:
    host = os.environ.get("RAPID_OCR_HOST", "127.0.0.1")
    port = int(os.environ.get("RAPID_OCR_PORT", "8869"))
    server = ThreadingHTTPServer((host, port), RapidOcrHandler)
    print(f"RapidOCR server listening on http://{host}:{port}/ocr")
    print(f"Health check: http://{host}:{port}/health")
    server.serve_forever()


if __name__ == "__main__":
    main()
