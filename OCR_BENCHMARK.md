# OCR backend notes

Test machine:

- GPU: NVIDIA RTX PRO 6000 Blackwell Workstation Edition
- Shared Torch env: `D:\AI\envs\shared-torch-cu130`
- Shared ONNX GPU env: `D:\AI\envs\shared-onnx-gpu`
- LunaTranslator runtime: `D:\Program Files\LunaTranslator`

Current retained OCR backends:

| Backend | Role | Notes |
| --- | --- | --- |
| LunaOCR default scale 5 + DML GPU | Primary local OCR | Best practical default for hover, region, screenshot, Japanese, and Chinese in current samples. Requires LunaTranslator runtime files. |
| Tesseract OCR | Lightweight fallback | Fast and independent, but Japanese is unusable and small UI text is less reliable. |
| PaddleOCR CPU service | Slow quality fallback | Kept for difficult cases; too slow for hover and normal interactive use. |

Current benchmark snapshot:

| Backend | Image | Language | Run | Time | Lines | Notes |
| --- | --- | --- | ---: | ---: | ---: | --- |
| LunaOCR default scale 5 + DML GPU | `1.png` | English | warm | 1.123s | 47 | Good English UI spacing; uses copied LunaTranslator default model |
| LunaOCR default scale 5 + DML GPU | `2.png` | English small region | warm | 0.085-0.102s | 3 | Very fast; returns `Force AutoFit` with correct spacing |
| LunaOCR default scale 5 + DML GPU | `jp.png` | Japanese | warm | 0.439-0.483s | 15 | Fast and practical for Japanese UI/news screenshots |
| LunaOCR default scale 5 + DML GPU | Chinese UI sample | Chinese | warm | 1.085s | 46 | Good practical default; preserves English/UI spacing well |
| Tesseract OCR | `1.png` | English | single | 0.669s | n/a | Fast but noisier |
| Tesseract OCR | `2.png` | English small region | single | 0.09s | n/a | Fast, but small UI text quality is weaker |
| Tesseract OCR | `jp.png` | Japanese | single | 0.341s | n/a | Current Japanese output is unusable/garbled |
| PaddleOCR CPU service | `1.png` | English | warm | 6.309s | 52 | Better spacing than older OCR options; too slow for hover |
| PaddleOCR CPU service | `jp.png` | Japanese | warm | 15.300s | 15 | Good quality, but too slow for interactive use |
| PaddleOCR CPU service | Chinese UI sample | Chinese | warm | 19.581s | 49 | Very slow |

Working recommendation:

- Hover OCR: LunaOCR default scale 5.
- Region/screenshot OCR: LunaOCR default scale 5.
- Fallback when LunaTranslator runtime is unavailable: Tesseract OCR.
- Difficult offline quality checks only: PaddleOCR.
