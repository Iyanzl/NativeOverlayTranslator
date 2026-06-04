from __future__ import annotations

import base64
import json
import os
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any
from PIL import Image


_ocr_lock = threading.Lock()
_ocr: Any | None = None
CLIENT_DISCONNECTED_ERRORS = (BrokenPipeError, ConnectionAbortedError, ConnectionResetError)


def _get_ocr() -> Any:
    global _ocr
    with _ocr_lock:
        if _ocr is None:
            from manga_ocr import MangaOcr

            _ocr = MangaOcr()
        return _ocr


def recognize(image_bytes: bytes) -> list[dict[str, Any]]:
    with tempfile.NamedTemporaryFile(delete=False, suffix=".png") as image_file:
        image_file.write(image_bytes)
        image_path = image_file.name

    try:
        text = str(_get_ocr()(image_path)).strip()
        if not text:
            return []

        with Image.open(image_path) as image:
            width, height = image.size

        return [
            {
                "text": text,
                "confidence": 0.9,
                "box": [0.0, 0.0, float(width), float(height)],
            }
        ]
    finally:
        try:
            os.remove(image_path)
        except OSError:
            pass


class MangaOcrHandler(BaseHTTPRequestHandler):
    server_version = "NativeOverlayMangaOCR/1.0"

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
        except CLIENT_DISCONNECTED_ERRORS as exc:
            print(f"[MangaOCR] client disconnected before response was sent: {exc}")
        except Exception as exc:
            try:
                self._write_json(500, {"error": str(exc)})
            except CLIENT_DISCONNECTED_ERRORS as write_exc:
                print(f"[MangaOCR] client disconnected before error response was sent: {write_exc}")

    def log_message(self, format: str, *args: Any) -> None:
        print(f"[MangaOCR] {self.address_string()} - {format % args}")

    def _write_json(self, status: int, payload: dict[str, Any]) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def main() -> None:
    host = os.environ.get("MANGA_OCR_HOST", "127.0.0.1")
    port = int(os.environ.get("MANGA_OCR_PORT", "8870"))
    server = ThreadingHTTPServer((host, port), MangaOcrHandler)
    print(f"MangaOCR server listening on http://{host}:{port}/ocr")
    print(f"Health check: http://{host}:{port}/health")
    server.serve_forever()


if __name__ == "__main__":
    main()
