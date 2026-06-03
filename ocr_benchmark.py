from __future__ import annotations

import argparse
import base64
import json
import time
import urllib.request
from pathlib import Path


def post_ocr(endpoint: str, image_path: Path, language: str) -> tuple[float, dict]:
    payload = {
        "Image_Base64": base64.b64encode(image_path.read_bytes()).decode("ascii"),
        "Language": language,
        "FileName": image_path.name,
    }
    body = json.dumps(payload).encode("utf-8")
    request = urllib.request.Request(
        endpoint,
        data=body,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    start = time.perf_counter()
    with urllib.request.urlopen(request, timeout=240) as response:
        data = json.loads(response.read().decode("utf-8"))
    return time.perf_counter() - start, data


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--endpoint", required=True)
    parser.add_argument("--image", required=True)
    parser.add_argument("--language", default="en")
    parser.add_argument("--repeat", type=int, default=1)
    args = parser.parse_args()

    image_path = Path(args.image)
    for index in range(args.repeat):
        elapsed, data = post_ocr(args.endpoint, image_path, args.language)
        lines = data.get("lines") or []
        first = lines[0].get("text", "") if lines else ""
        print(
            json.dumps(
                {
                    "run": index + 1,
                    "elapsed_seconds": round(elapsed, 3),
                    "line_count": len(lines),
                    "first_text": first[:120],
                },
                ensure_ascii=False,
            )
        )


if __name__ == "__main__":
    main()
