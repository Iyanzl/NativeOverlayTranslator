from __future__ import annotations

import base64
import json
import os
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any


_ocr_lock = threading.Lock()
_ocr_cache: dict[str, Any] = {}

os.environ.setdefault("PADDLE_PDX_MODEL_SOURCE", "bos")
os.environ.setdefault("PADDLE_PDX_ENABLE_MKLDNN_BYDEFAULT", "False")
os.environ.setdefault("FLAGS_use_mkldnn", "0")


def _normalize_lang(language: str | None) -> str:
    value = (language or "auto").lower()
    if value in {"ja", "jpn", "japan", "japanese"}:
        return "japan"
    if value in {"zh", "ch", "chi_sim", "chinese"}:
        return "ch"
    if value in {"en", "eng", "english"}:
        return "en"
    return "ch"


def _get_ocr(language: str) -> Any:
    lang = _normalize_lang(language)
    with _ocr_lock:
        if lang not in _ocr_cache:
            from paddleocr import PaddleOCR

            recognition_model = "en_PP-OCRv5_mobile_rec" if lang == "en" else "PP-OCRv5_server_rec"
            try:
                _ocr_cache[lang] = PaddleOCR(
                    use_doc_orientation_classify=False,
                    use_doc_unwarping=False,
                    use_textline_orientation=False,
                    text_detection_model_name="PP-OCRv5_mobile_det",
                    text_recognition_model_name=recognition_model,
                    lang=lang,
                )
            except ValueError:
                _ocr_cache[lang] = PaddleOCR(use_angle_cls=False, lang=lang)
        return _ocr_cache[lang]


def _bounds_from_points(points: Any) -> list[float]:
    xs = [float(point[0]) for point in points]
    ys = [float(point[1]) for point in points]
    left = min(xs)
    top = min(ys)
    right = max(xs)
    bottom = max(ys)
    return [left, top, max(1.0, right - left), max(1.0, bottom - top)]


def _bounds_from_box(box: Any) -> list[float]:
    values = [float(value) for value in box]
    if len(values) >= 4:
        return [values[0], values[1], max(1.0, values[2] - values[0]), max(1.0, values[3] - values[1])]
    return [0.0, 0.0, 1.0, 1.0]


def _to_plain_points(points: Any) -> list[list[float]]:
    return [[float(point[0]), float(point[1])] for point in points]


def _flatten_ocr_result(result: Any) -> list[dict[str, Any]]:
    lines: list[dict[str, Any]] = []

    def visit(node: Any) -> None:
        if isinstance(node, dict):
            texts = node.get("rec_texts") or []
            scores = node.get("rec_scores") or []
            polys = node.get("rec_polys")
            if polys is None:
                polys = node.get("dt_polys")
            if polys is None:
                polys = []
            boxes = node.get("rec_boxes")
            if boxes is None:
                boxes = []
            for index, raw_text in enumerate(texts):
                text = str(raw_text).strip()
                if not text:
                    continue

                score = float(scores[index]) if index < len(scores) else 0.8
                if index < len(polys):
                    points = _to_plain_points(polys[index])
                    bounds = _bounds_from_points(points)
                elif index < len(boxes):
                    points = []
                    bounds = _bounds_from_box(boxes[index])
                else:
                    points = []
                    bounds = [0.0, 0.0, 1.0, 1.0]

                lines.append(
                    {
                        "text": text,
                        "confidence": score,
                        "box": bounds,
                        "points": points,
                    }
                )
            return

        if not isinstance(node, list):
            return

        if len(node) >= 2 and isinstance(node[0], list) and isinstance(node[1], (list, tuple)):
            text_info = node[1]
            if len(text_info) >= 2:
                text = str(text_info[0]).strip()
                if text:
                    lines.append(
                        {
                            "text": text,
                            "confidence": float(text_info[1]),
                            "box": _bounds_from_points(node[0]),
                            "points": node[0],
                        }
                    )
                    return

        for item in node:
            visit(item)

    visit(result)
    return lines


def recognize(image_bytes: bytes, language: str) -> list[dict[str, Any]]:
    suffix = ".png"
    with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as image_file:
        image_file.write(image_bytes)
        image_path = image_file.name

    try:
        ocr = _get_ocr(language)
        if hasattr(ocr, "ocr"):
            try:
                result = ocr.ocr(image_path, cls=True)
            except TypeError:
                result = ocr.ocr(image_path)
            return _flatten_ocr_result(result)

        if hasattr(ocr, "predict"):
            result = ocr.predict(image_path)
            return _flatten_ocr_result(result)

        raise RuntimeError("Unsupported PaddleOCR API shape.")
    finally:
        try:
            os.remove(image_path)
        except OSError:
            pass


class PaddleOcrHandler(BaseHTTPRequestHandler):
    server_version = "NativeOverlayPaddleOCR/1.0"

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

            image_bytes = base64.b64decode(image_base64)
            language = payload.get("Language") or payload.get("language") or "auto"
            lines = recognize(image_bytes, language)
            self._write_json(200, {"lines": lines})
        except Exception as exc:
            self._write_json(500, {"error": str(exc)})

    def log_message(self, format: str, *args: Any) -> None:
        print(f"[PaddleOCR] {self.address_string()} - {format % args}")

    def _write_json(self, status: int, payload: dict[str, Any]) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)


def main() -> None:
    host = os.environ.get("PADDLE_OCR_HOST", "127.0.0.1")
    port = int(os.environ.get("PADDLE_OCR_PORT", "8868"))
    server = ThreadingHTTPServer((host, port), PaddleOcrHandler)
    print(f"PaddleOCR server listening on http://{host}:{port}/ocr")
    print("Health check: http://127.0.0.1:8868/health")
    server.serve_forever()


if __name__ == "__main__":
    main()
