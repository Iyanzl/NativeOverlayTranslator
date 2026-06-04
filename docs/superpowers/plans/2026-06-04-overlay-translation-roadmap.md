# Overlay Translation Roadmap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve interactive translation from fast hover response through visual screenshot matching and dynamic UI-aware overlays.

**Architecture:** Work in phases that are independently testable by the user. Start with the existing OCR overlay pipeline, then improve image styling, then add dynamic OCR refresh, and only then add UI Automation or hook-based text sources where they are justified.

**Tech Stack:** WPF/.NET 9, LunaOCR HTTP service, existing overlay windows, OCR benchmark scripts, Windows UI Automation for later text-source work.

---

### Phase 1: Hover Translation Speed

**Files:**
- Create: `Services/HoverPerformancePolicy.cs`
- Modify: `MainWindow.xaml.cs`
- Modify: `NativeOverlayTranslator.Tests/Program.cs`
- Modify: `NativeOverlayTranslator.Tests/NativeOverlayTranslator.Tests.csproj`

- [x] **Step 1: Add policy tests**

Add checks that word/phrase/sentence hover modes use shorter stable waits, smaller capture regions, and fewer OCR confirmation ticks than the current implementation.

- [x] **Step 2: Move hover timing constants into `HoverPerformancePolicy`**

Replace hard-coded timer, pointer-stable, input-quiet, OCR-delay, capture-region, and stable-count constants in `MainWindow.xaml.cs` with calls to `HoverPerformancePolicy`.

- [x] **Step 3: Keep behavior scoped**

Do not change screenshot OCR, region OCR, image translation styling, or overlay persistence in this phase.

- [x] **Step 4: Verify**

Run:

```powershell
dotnet run --project NativeOverlayTranslator.Tests\NativeOverlayTranslator.Tests.csproj
dotnet build NativeOverlayTranslator.csproj -o build-check -p:UseAppHost=false
```

Expected: tests pass, build has 0 errors.

- [ ] **Step 5: User validation**

User starts `start_lunaocr.bat`, enables hover translate, and confirms hover response is faster on English UI text before Phase 2 starts.

---

### Phase 2: Screenshot Translation Visual Matching

**Files:**
- Modify: `ImageTranslationWindow.xaml.cs`
- Optionally create: `Services/ImageOverlayStyleSampler.cs`

- [x] **Step 1: Improve background sampling**

Sample border pixels and inner low-text pixels separately, then use the border/background estimate for overlay fill.

- [x] **Step 2: Improve foreground color estimation**

Estimate original text color from high-contrast pixels inside OCR bounds instead of choosing only black or white from background luminance.

- [x] **Step 3: Improve font matching**

Remove forced semibold by default, size text from OCR box height, and only bold when source pixels suggest thick strokes.

- [ ] **Step 4: Verify on screenshots**

Use bundled images and user screenshots. Accept improvement on flat and simple gradient backgrounds; complex background matching is deferred to Phase 2.5.

---

### Phase 2.5: Complex Background Repair

**Files:**
- Modify: `ImageTranslationWindow.xaml.cs`
- Optionally create: `Services/ImageInpaintService.cs`

- [ ] **Step 1: Add local patch-fill background repair**

Before drawing translated text, fill the original text area with sampled nearby background. Use simple directional sampling first.

- [ ] **Step 2: Defer model inpainting**

Do not add a heavy image model unless local patch-fill is insufficient and the user approves the dependency.

---

### Phase 3: Dynamic OCR Overlay Refresh

**Files:**
- Modify: `MainWindow.xaml.cs`
- Optionally create: `Services/DynamicOcrOverlayService.cs`

- [ ] **Step 1: Add refresh loop only while full/region translation is active**

Periodically capture the target window, detect changed regions, OCR only those regions, and reconcile current overlays.

- [ ] **Step 2: Reconcile overlays**

Match by normalized source text and nearby bounds. Add new overlays, update moved overlays, and close overlays whose source text disappears.

- [ ] **Step 3: User validation**

User opens an English app menu, verifies new menu items get overlays, then closes the menu and verifies those overlays disappear.

---

### Phase 4: Windows UI Automation Text Source

**Files:**
- Create: `Services/UiAutomationTextSource.cs`
- Modify: `IHookTextSource.cs` or introduce a clearer `ITextSourceService`
- Modify: `MainWindow.xaml.cs`

- [ ] **Step 1: Read visible UIA elements**

Collect `Name`, `BoundingRectangle`, control type, enabled/visible state from the selected foreground window.

- [ ] **Step 2: Translate and overlay UIA elements**

Use UIA bounds instead of OCR bounds when available. Fall back to LunaOCR for elements that UIA cannot read.

- [ ] **Step 3: User validation**

Test with Notepad, Explorer, settings dialogs, and one user-selected English desktop app.

---

### Phase 5: Specific Hook Integrations

**Files:**
- Create only after a specific target software class is selected.

- [ ] **Step 1: Choose target class**

Pick one: Win32 menu hooks, Electron/Chromium text extraction, game text hook, or DirectX/OpenGL overlay.

- [ ] **Step 2: Build a narrow proof of concept**

Do not attempt a universal hook. Prove text extraction and bounds for one app class first.

- [ ] **Step 3: Decide whether replacement is worth it**

Prefer high-quality overlay unless direct text replacement is stable and reversible for the chosen app.
