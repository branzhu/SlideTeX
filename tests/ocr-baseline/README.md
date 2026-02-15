# OCR Baseline (Known Pairs)

This folder stores known image-LaTeX pairs for OCR baseline testing.

## Files

- `ocr-baseline-v1.json`: OCR baseline fixture.
- Image assets are referenced from `tests/render-regression/baseline-images/*.png`.

## Rebuild Fixture

```powershell
node ./scripts/build-ocr-baseline-fixture.mjs
```

## Run Baseline

```powershell
pwsh ./scripts/Test-OcrBaseline.ps1 -Configuration Debug -Suite smoke
pwsh ./scripts/Test-OcrBaseline.ps1 -Configuration Debug -Suite full
```

## Model Requirement

Set model directory by one of the following:

- Pass `-ModelDir <path>` to `Test-OcrBaseline.ps1`
- Or set environment variable `SLIDETEX_OCR_MODEL_DIR`
- Or sync into default directory with:

```powershell
pwsh ./scripts/Sync-MathJax.ps1 -Component pix2text-mfr -Pix2TextModelId "breezedeus/pix2text-mfr-1.5"
```

Model directory must contain:

- `encoder_model.onnx`
- `decoder_model.onnx` (or legacy `decoder_model_merged_quantized.onnx`)
- `tokenizer.json`
