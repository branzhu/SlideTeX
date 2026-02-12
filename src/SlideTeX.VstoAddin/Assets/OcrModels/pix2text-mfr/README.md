# pix2text-mfr model placement

Place the following files in this directory for offline OCR inference:

- encoder_model.onnx
- decoder_model_merged_quantized.onnx
- tokenizer.json
- generation_config.json

Source (official):
- https://huggingface.co/breezedeus/pix2text-mfr/tree/main/onnx

Optional override:
- Set environment variable `SLIDETEX_OCR_MODEL_DIR` to use a custom model directory.
