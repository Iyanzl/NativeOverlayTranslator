# Native Overlay Translator Final Development Roadmap

**Final goal:** Translate visible text in ordinary Windows applications with responsive hover translation, visually matched overlays, and dynamic overlays that follow source controls and disappear when the source disappears.

**Delivery rule:** Complete one phase at a time. Each phase must pass automated tests and a user acceptance check before work starts on the next phase. Keep every phase in a separate commit so it can be reverted independently.

## Current baseline

- OCR backends are reduced to Windows OCR, Tesseract OCR, and local LunaOCR.
- LunaOCR V5 and V6 run from project-local models and DLLs without a LunaTranslator installation.
- Hover capture latency has been reduced.
- Screenshot overlays sample source background, foreground, font size, and font weight, but color stability and complex backgrounds still need work.
- OCR failure is reported instead of silently falling back to another OCR engine.

## Phase 1: Restore interaction reliability

### 1.1 Hover selection modes

- Word mode selects only the token under the pointer from a line-level LunaOCR result.
- Phrase mode selects the punctuation-delimited clause under the pointer and limits long clauses to a nearby word window.
- Sentence mode keeps the full OCR line.
- Reject isolated OCR noise such as stray single letters unless the token is a valid English word such as `A` or `I`.

**Automated acceptance:** selector tests cover word, phrase, sentence, punctuation, long clauses, and isolated OCR noise.

**User acceptance:** hover the same English UI line in all three modes and confirm that the translated scope changes correctly.

### 1.2 Clipboard reliability

- Retry `OpenClipboard` failures with short bounded backoff.
- Serialize clipboard update handling so two rapid `Ctrl+C` events are both retained.
- Cancel pending reads cleanly when the application closes.

**Automated acceptance:** simulated `0x800401D0` failures recover and return trimmed clipboard text.

**User acceptance:** repeatedly double-copy text from a browser, editor, and one target application without an unhandled clipboard error.

## Phase 2: Deterministic visual matching

### 2.1 Stable source style estimation

- Cluster sampled colors instead of taking a channel-by-channel median.
- Select foreground from coherent high-contrast stroke colors and reject isolated red, blue, or gray pixels.
- Add temporal smoothing so repeated captures of the same source bounds do not change color without a meaningful source-image change.
- Keep font weight conservative and derive font size from OCR box height.

**User acceptance:** repeat screenshot translation at least ten times on the same image; background and text colors remain stable and visually close to the source.

### 2.2 Flat and gradient background repair

- Erase the original text using border-guided directional fill before drawing translated text.
- Support flat fills and simple horizontal or vertical gradients first.
- Do not add a large inpainting model unless these local methods fail on the user's representative screenshots.

**User acceptance:** translated text no longer shows the original glyphs underneath on flat and simple gradient backgrounds.

## Phase 3: Overlay lifecycle engine

- Introduce a source-independent snapshot model containing source identity, text, bounds, confidence, and visibility.
- Reconcile snapshots by stable identity first, then normalized text and nearby bounds.
- Add overlays for new sources, move existing overlays with their sources, and remove overlays after the source disappears for a short confirmation window.
- Pause capture while overlay pixels would contaminate OCR screenshots.

**Automated acceptance:** deterministic tests cover add, move, text change, temporary miss, and removal.

**User acceptance:** a synthetic test window moves, changes, and removes labels while overlays follow without duplicates or flicker.

## Phase 4: Windows UI Automation source

- Read visible UI Automation elements from the selected process: name, control type, bounds, enabled state, and off-screen state.
- Include popup menu windows owned by the target process.
- Feed UIA snapshots into the Phase 3 lifecycle engine.
- Use OCR only for visible regions that UIA cannot expose.

**User acceptance:** test Notepad, Explorer, Windows dialogs, and one chosen English application; opening and closing menus adds and removes translations correctly.

## Phase 5: Dynamic OCR source

- Detect changed regions of the selected window instead of OCRing the whole screen continuously.
- OCR only changed regions with LunaOCR and feed results into the same lifecycle engine.
- Adapt refresh frequency to activity: fast after UI changes, slow while the window is stable.
- Bound CPU/GPU work and stop immediately when dynamic translation is disabled.

**User acceptance:** custom-drawn or non-UIA software updates overlays when pages and menus change, without noticeable input lag.

## Phase 6: Target-specific hooks

- Select one concrete application class only after UIA and dynamic OCR limitations are measured.
- Implement a narrow adapter for that class, such as Win32 menus, Electron accessibility, game text extraction, or a graphics overlay.
- Prefer reversible translated overlays; direct in-process text replacement is allowed only when it is stable, application-specific, and recoverable.
- Do not attempt a universal injection hook.

**User acceptance:** the selected target application exposes text and bounds more accurately or faster than OCR, with no instability after repeated open/close cycles.

## Priority order

1. Finish Phase 1 and wait for user confirmation.
2. Stabilize screenshot colors and background repair.
3. Build and validate the source-independent overlay lifecycle engine.
4. Add UI Automation for standard Windows software.
5. Add dynamic OCR for custom interfaces.
6. Add hooks only for a clearly identified target that still needs them.
