# Regression Testing Guide (Numbering + Rendering + OCR)

## Scope
This document consolidates regression testing for:
- Equation numbering semantics (`equation/align/gather/multline` behavior).
- Rendering quality (visual/layout checks, font loading, and tag positioning).
- OCR quality (known image-LaTeX pair baseline checks).

## Objectives
- Keep equation numbering aligned with LaTeX semantics.
- Keep rendered output stable across versions (layout + pixels + tags).
- Detect regressions early through scriptable checks.

## Test Assets
- Numbering known-good baseline:
  - `tests/equation-numbering/numbering-mathjax-v3.json`
- Rendering fixture:
  - `tests/render-regression/render-visual-katex-v1.json`
- Rendering baselines:
  - `tests/render-regression/baseline-images/*.png`
  - `tests/render-regression/baseline-dom/*.json`
- Runtime artifacts:
  - `artifacts/render-regression/`
- OCR known-pairs baseline:
  - `tests/ocr-baseline/ocr-baseline-v1.json`
  - `tests/render-regression/baseline-images/*.png` (referenced by OCR fixture)
- OCR runtime artifacts:
  - `artifacts/ocr-baseline/`

## Prerequisites
- Build prerequisites from `README.md`.
- Node dependencies installed (`npm install`) for render regression tooling.

## Core Commands
- Numbering transform regression:
```powershell
pwsh ./scripts/Test-EquationNumberingTransform.ps1 -Configuration Debug
```

- Numbering known-good comparison:
```powershell
pwsh ./scripts/Test-EquationNumberingKnownGood.ps1 -Configuration Debug
```

- Render regression (daily smoke):
```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-RenderKnownGood.ps1 -Mode verify -Suite smoke
```

- Render regression (pre-release full):
```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-RenderKnownGood.ps1 -Mode verify -Suite full
```

- Render baseline update (only when intended):
```powershell
powershell -ExecutionPolicy Bypass -File scripts/Test-RenderKnownGood.ps1 -Mode update-baseline -Suite all
```

- Rebuild OCR known-pairs fixture:
```powershell
node ./scripts/build-ocr-baseline-fixture.mjs
```

- OCR baseline (smoke/full):
```powershell
pwsh ./scripts/Test-OcrBaseline.ps1 -Configuration Debug -Suite smoke -ModelDir "C:\models\pix2text-mfr"
pwsh ./scripts/Test-OcrBaseline.ps1 -Configuration Debug -Suite full -ModelDir "C:\models\pix2text-mfr"
```

## Numbering Semantic Cases
1. Single-line `align` without `\tag`:
   - Exactly one auto number, no overlap with formula body.
2. Multi-line `align` without `\tag`:
   - One number per numberable row.
3. Multi-line `align` with `\notag/\nonumber`:
   - Marked rows are skipped; remaining rows continue sequence.
4. Multi-line `align` with partial `\tag{A}`:
   - Tagged row shows `(A)` and does not consume auto sequence.
5. `equation` with `\tag{custom}`:
   - Only custom tag is displayed.
6. Multi-line `gather`:
   - Number per row, with `\notag` support.
7. `multline` (KaTeX `0.16.11` unsupported):
   - Explicit unsupported error; numbering rewrite must not run.
8. Mixed-document renumbering:
   - Continuous sequence in reading order across objects, respecting environment semantics.

## Rendering Verification Rules
- Engine: KaTeX `0.16.11` (locked baseline).
- Loading model: local static server (`http://127.0.0.1:<port>`) to avoid `file://` limits.
- Structural checks include:
  - Expected `.tag` sequence.
  - Required fonts via `document.fonts.check()`.
  - No overlap between tag and formula.
  - Right-side spacing within thresholds.
  - No preview clipping.
- Pixel checks include:
  - `diffPixels <= maxDiffPixels`
  - `diffRatio <= maxDiffRatio`

## Optional Render Script Parameters
- `-CaseId align_single_tag`
- `-FixturePath tests/render-regression/render-visual-katex-v1.json`
- `-ChromePath "C:\Program Files\Google\Chrome\Application\chrome.exe"`
- `-Suite all|smoke|full`

## Failure Triage
- Summary report:
  - `artifacts/render-regression/report.json`
- Per-case detail logs:
  - `artifacts/render-regression/logs/<case>.json`
- Actual screenshots:
  - `artifacts/render-regression/actual/<case>.png`
- Diff images:
  - `artifacts/render-regression/diff/<case>.png`
- OCR summary report:
  - `artifacts/ocr-baseline/report.json`

## Pass Criteria
- Numbering checks pass for count + row ownership + displayed tag text.
- No duplicate display of auto-number and custom tag.
- No numbering overlap in single-line or multi-line `align`.
- Rendering checks pass both structural and pixel thresholds.

## Notes
- `multline` remains an expected-failure case under KaTeX `0.16.11`.
- Update baselines only when KaTeX version or rendering strategy changes, and record the reason in commit notes.
- OCR baseline requires local ONNX model files (`encoder_model.onnx`, `decoder_model.onnx`, `tokenizer.json`), and still accepts legacy decoder file name `decoder_model_merged_quantized.onnx`.
