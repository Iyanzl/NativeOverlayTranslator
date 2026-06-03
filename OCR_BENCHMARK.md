# OCR backend notes

Test machine:

- GPU: NVIDIA RTX PRO 6000 Blackwell Workstation Edition
- Shared Torch env: `D:\AI\envs\shared-torch-cu130`
- Shared ONNX GPU env: `D:\AI\envs\shared-onnx-gpu`

Verified shared environments:

- Torch: `2.10.0+cu130`, CUDA available.
- ONNX Runtime: `1.23.2`, providers include `TensorrtExecutionProvider`, `CUDAExecutionProvider`, `CPUExecutionProvider`.

Current benchmark snapshot:

| Backend | Image | Language | Run | Time | Lines | Notes |
| --- | --- | --- | ---: | ---: | ---: | --- |
| RapidOCR + shared ONNX GPU | `1.png` | English | warm | 1.079s | 48 | Fast; usable for English UI; loses some spacing, for example `UEVR[rev...]` |
| PaddleOCR CPU service | `1.png` | English | warm | 6.309s | 52 | Better spacing; too slow for hover |
| MangaOCR service | `1.png` | English | single | 500 | n/a | Server error: missing `jaconv` dependency |
| RapidOCR + shared ONNX GPU | `jp.png` | Japanese | warm | 1.314s | 15 | Fast but drops some characters; lower quality than Paddle |
| PaddleOCR CPU service | `jp.png` | Japanese | warm | 15.300s | 15 | Best Japanese quality in current tests |
| MangaOCR service | `jp.png` | Japanese | single | 500 | n/a | Server error: missing `jaconv` dependency |
| RapidOCR + shared ONNX GPU | `翻译.png` | Chinese | warm | 1.396s | 47 | Fast and partially usable; first line has noise |
| PaddleOCR CPU service | `翻译.png` | Chinese | warm | 19.581s | 49 | Very slow; first line missed leading UI text in this sample |
| MangaOCR service | `翻译.png` | Chinese | single | 500 | n/a | Server error: missing `jaconv` dependency |
| RapidOCR + shared ONNX GPU | `2.png` | English small region | warm | 0.783s | 3 | Best hover candidate; returns `ForceAutoFit` without spacing |
| PaddleOCR CPU service | `2.png` | English small region | warm | 1.967s | 3 | Better spacing, `Force AutoFit`, but slower and inconsistent |
| PaddleOCR CPU service | `1.png` | English | cold-ish | 11.743s | 52 | Good quality, too slow for hover |
| PaddleOCR CPU service | `1.png` | English | warm | 6.443s | 52 | Region/screenshot only |
| PaddleOCR CPU service | `jp.png` | Japanese | cold-ish | 16.132s | 15 | Good Japanese quality |
| PaddleOCR CPU service | `jp.png` | Japanese | warm | 15.527s | 15 | Too slow for hover |
| RapidOCR + shared ONNX GPU | `2.png` | English | cold-ish | 4.084s | 3 | First run includes model/session init |
| RapidOCR + shared ONNX GPU | `2.png` | English | warm | 0.673s | 3 | Suitable candidate for hover |
| RapidOCR + shared ONNX GPU | `1.png` | English | warm | 2.166s | 48 | Faster than Paddle, slight spacing loss |
| RapidOCR + shared ONNX GPU | `jp.png` | Japanese | warm | 2.964s | 15 | Faster, but Japanese quality lower than Paddle |
| Tesseract | `1.png` | English | single | 0.669s | n/a | Fast but noisier |
| Tesseract | `jp.png` | Japanese | single | 0.341s | n/a | Current Japanese output is unusable/garbled |

Working recommendation:

- Hover OCR: RapidOCR service, especially for English UI and small regions.
- Region/screenshot OCR: PaddleOCR service for quality.
- Japanese quality mode: PaddleOCR for now.
- Chinese OCR: RapidOCR is the practical default for speed; PaddleOCR may need more targeted samples before recommending it.
- MangaOCR: current service returns HTTP 500 with `No module named 'jaconv'`, so keep it disabled for general OCR until that dependency path is fixed.
