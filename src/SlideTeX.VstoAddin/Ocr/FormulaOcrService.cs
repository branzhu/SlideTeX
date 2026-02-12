// SlideTeX Note: Host-side ONNX Runtime inference pipeline for formula image OCR.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SlideTeX.VstoAddin.Ocr
{
    internal sealed class FormulaOcrService : IDisposable
    {
        private readonly object _sync = new object();
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private InferenceSession _encoderSession;
        private InferenceSession _decoderSession;
        private string _encoderInputName;
        private string _encoderOutputName;
        private string _decoderOutputName;
        private string[] _decoderInputNames;
        private string[] _idToToken;
        private HashSet<long> _specialTokenIds;
        private bool _useByteLevelDecoder;
        private Dictionary<char, byte> _byteDecoderMap;
        private ModelManifest _manifest;
        private bool _initialized;

        public FormulaOcrResult Recognize(string imageBase64, FormulaOcrOptions options)
        {
            if (string.IsNullOrWhiteSpace(imageBase64))
            {
                throw new FormulaOcrException(OcrErrorCode.BadImage, "OCR input image is empty.");
            }

            var effectiveOptions = options ?? FormulaOcrOptions.Default;
            if (effectiveOptions.MaxTokens <= 0)
            {
                effectiveOptions.MaxTokens = FormulaOcrOptions.Default.MaxTokens;
            }

            EnsureInitialized();

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var pixelTensor = BuildPixelTensor(imageBase64, _manifest.ImageSize, _manifest.PixelMean, _manifest.PixelStd);
                var encoderHiddenStates = RunEncoder(pixelTensor);
                var tokenIds = GenerateTokenIds(encoderHiddenStates, effectiveOptions.MaxTokens, _manifest.EosTokenId);
                var latex = DecodeTokenIds(tokenIds);

                stopwatch.Stop();
                return new FormulaOcrResult
                {
                    Latex = latex,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Engine = "onnxruntime-cpu"
                };
            }
            catch (FormulaOcrException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new FormulaOcrException(OcrErrorCode.InferenceFailed, "Formula OCR inference failed: " + ex.Message, ex);
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_encoderSession != null)
                {
                    _encoderSession.Dispose();
                    _encoderSession = null;
                }

                if (_decoderSession != null)
                {
                    _decoderSession.Dispose();
                    _decoderSession = null;
                }

                _initialized = false;
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            lock (_sync)
            {
                if (_initialized)
                {
                    return;
                }

                try
                {
                    var modelDirectory = ResolveModelDirectory();
                    _manifest = LoadManifest(modelDirectory);

                    var encoderPath = Path.Combine(modelDirectory, _manifest.EncoderFile);
                    var decoderPath = ResolveDecoderModelPath(modelDirectory, _manifest.DecoderFile);
                    var tokenizerPath = Path.Combine(modelDirectory, _manifest.TokenizerFile);

                    EnsureFileExists(encoderPath, "encoder");
                    EnsureFileExists(tokenizerPath, "tokenizer");

                    var sessionOptions = new SessionOptions();
                    _encoderSession = new InferenceSession(encoderPath, sessionOptions);
                    _decoderSession = new InferenceSession(decoderPath, sessionOptions);

                    _encoderInputName = ResolveEncoderInputName(_encoderSession);
                    _encoderOutputName = ResolveEncoderOutputName(_encoderSession);
                    _decoderOutputName = ResolveDecoderOutputName(_decoderSession);
                    _decoderInputNames = _decoderSession.InputMetadata.Keys.ToArray();

                    BuildTokenizerMappings(tokenizerPath, _manifest, out _idToToken, out _specialTokenIds);
                    _specialTokenIds.Add(_manifest.BosTokenId);
                    _specialTokenIds.Add(_manifest.PadTokenId);
                    _specialTokenIds.Add(_manifest.DecoderStartTokenId);

                    _initialized = true;
                }
                catch (FormulaOcrException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    throw new FormulaOcrException(OcrErrorCode.ModelInitFailed, "Formula OCR model initialization failed: " + ex.Message, ex);
                }
            }
        }

        private static void EnsureFileExists(string path, string role)
        {
            if (!File.Exists(path))
            {
                throw new FormulaOcrException(
                    OcrErrorCode.ModelNotFound,
                    "Formula OCR " + role + " model file was not found: " + path);
            }
        }

        private static string ResolveDecoderModelPath(string modelDirectory, string configuredDecoderFile)
        {
            var decoderCandidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(configuredDecoderFile))
            {
                decoderCandidates.Add(configuredDecoderFile);
            }

            AddFileCandidateIfMissing(decoderCandidates, "decoder_model.onnx");
            AddFileCandidateIfMissing(decoderCandidates, "decoder_model_merged_quantized.onnx");

            for (var i = 0; i < decoderCandidates.Count; i++)
            {
                var candidatePath = Path.Combine(modelDirectory, decoderCandidates[i]);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            throw new FormulaOcrException(
                OcrErrorCode.ModelNotFound,
                "Formula OCR decoder model file was not found. Tried: " + string.Join(", ", decoderCandidates.ToArray()));
        }

        private static void AddFileCandidateIfMissing(ICollection<string> candidates, string fileName)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            foreach (var existing in candidates)
            {
                if (string.Equals(existing, fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            candidates.Add(fileName);
        }

        private static string ResolveModelDirectory()
        {
            var envPath = Environment.GetEnvironmentVariable("SLIDETEX_OCR_MODEL_DIR");
            if (!string.IsNullOrWhiteSpace(envPath) && Directory.Exists(envPath))
            {
                return envPath;
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(baseDir, "OcrModels", "pix2text-mfr")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "OcrModels", "pix2text-mfr")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "SlideTeX.VstoAddin", "Assets", "OcrModels", "pix2text-mfr"))
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FormulaOcrException(
                OcrErrorCode.ModelNotFound,
                "Formula OCR model directory was not found. Set SLIDETEX_OCR_MODEL_DIR or copy models to OcrModels/pix2text-mfr.");
        }

        private ModelManifest LoadManifest(string modelDirectory)
        {
            var manifest = ModelManifest.CreateDefault();
            var manifestPath = Path.Combine(modelDirectory, "MODEL_MANIFEST.json");
            if (!File.Exists(manifestPath))
            {
                var generationConfigPath = Path.Combine(modelDirectory, manifest.GenerationConfigFile);
                if (File.Exists(generationConfigPath))
                {
                    ApplyGenerationConfig(generationConfigPath, manifest);
                }
                return manifest;
            }

            var raw = File.ReadAllText(manifestPath, Encoding.UTF8);
            var root = _serializer.DeserializeObject(raw) as Dictionary<string, object>;
            if (root == null)
            {
                return manifest;
            }

            manifest.EncoderFile = GetString(root, "encoderFile", manifest.EncoderFile);
            manifest.DecoderFile = GetString(root, "decoderFile", manifest.DecoderFile);
            manifest.TokenizerFile = GetString(root, "tokenizerFile", manifest.TokenizerFile);
            manifest.GenerationConfigFile = GetString(root, "generationConfigFile", manifest.GenerationConfigFile);
            manifest.ImageSize = GetInt(root, "imageSize", manifest.ImageSize);
            manifest.BosTokenId = GetLong(root, "bosTokenId", manifest.BosTokenId);
            manifest.EosTokenId = GetLong(root, "eosTokenId", manifest.EosTokenId);
            manifest.PadTokenId = GetLong(root, "padTokenId", manifest.PadTokenId);
            manifest.DecoderStartTokenId = GetLong(root, "decoderStartTokenId", manifest.DecoderStartTokenId);

            var pixelMean = GetFloatArray(root, "pixelMean");
            if (pixelMean != null && pixelMean.Length == 3)
            {
                manifest.PixelMean = pixelMean;
            }

            var pixelStd = GetFloatArray(root, "pixelStd");
            if (pixelStd != null && pixelStd.Length == 3)
            {
                manifest.PixelStd = pixelStd;
            }

            var generationConfigPathFromManifest = Path.Combine(modelDirectory, manifest.GenerationConfigFile);
            if (File.Exists(generationConfigPathFromManifest))
            {
                ApplyGenerationConfig(generationConfigPathFromManifest, manifest);
            }

            return manifest;
        }

        private void ApplyGenerationConfig(string generationConfigPath, ModelManifest manifest)
        {
            var raw = File.ReadAllText(generationConfigPath, Encoding.UTF8);
            var root = _serializer.DeserializeObject(raw) as Dictionary<string, object>;
            if (root == null)
            {
                return;
            }

            manifest.BosTokenId = GetLong(root, "bos_token_id", manifest.BosTokenId);
            manifest.EosTokenId = GetLong(root, "eos_token_id", manifest.EosTokenId);
            manifest.PadTokenId = GetLong(root, "pad_token_id", manifest.PadTokenId);
            manifest.DecoderStartTokenId = GetLong(root, "decoder_start_token_id", manifest.DecoderStartTokenId);
        }

        private static string ResolveEncoderInputName(InferenceSession session)
        {
            foreach (var key in session.InputMetadata.Keys)
            {
                if (string.Equals(key, "pixel_values", StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }
            }

            foreach (var key in session.InputMetadata.Keys)
            {
                return key;
            }

            throw new FormulaOcrException(OcrErrorCode.ModelInitFailed, "Encoder ONNX input metadata is empty.");
        }

        private static string ResolveEncoderOutputName(InferenceSession session)
        {
            foreach (var pair in session.OutputMetadata)
            {
                if (pair.Value != null && pair.Value.ElementType == typeof(float))
                {
                    return pair.Key;
                }
            }

            foreach (var key in session.OutputMetadata.Keys)
            {
                return key;
            }

            throw new FormulaOcrException(OcrErrorCode.ModelInitFailed, "Encoder ONNX output metadata is empty.");
        }

        private static string ResolveDecoderOutputName(InferenceSession session)
        {
            foreach (var key in session.OutputMetadata.Keys)
            {
                if (string.Equals(key, "logits", StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }
            }

            foreach (var pair in session.OutputMetadata)
            {
                if (pair.Value != null && pair.Value.ElementType == typeof(float))
                {
                    return pair.Key;
                }
            }

            foreach (var key in session.OutputMetadata.Keys)
            {
                return key;
            }

            throw new FormulaOcrException(OcrErrorCode.ModelInitFailed, "Decoder ONNX output metadata is empty.");
        }

        private DenseTensor<float> BuildPixelTensor(string imageBase64, int imageSize, float[] mean, float[] std)
        {
            byte[] imageBytes;
            try
            {
                imageBytes = DecodeBase64Image(imageBase64);
            }
            catch (Exception ex)
            {
                throw new FormulaOcrException(OcrErrorCode.BadImage, "Failed to decode OCR input image: " + ex.Message, ex);
            }

            try
            {
                using (var ms = new MemoryStream(imageBytes))
                using (var source = new Bitmap(ms))
                using (var resized = new Bitmap(imageSize, imageSize, PixelFormat.Format24bppRgb))
                using (var graphics = Graphics.FromImage(resized))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.SmoothingMode = SmoothingMode.HighQuality;
                    graphics.Clear(Color.White);
                    graphics.DrawImage(source, new Rectangle(0, 0, imageSize, imageSize));

                    var tensor = new DenseTensor<float>(new[] { 1, 3, imageSize, imageSize });
                    for (var y = 0; y < imageSize; y++)
                    {
                        for (var x = 0; x < imageSize; x++)
                        {
                            var pixel = resized.GetPixel(x, y);
                            var r = ((pixel.R / 255f) - mean[0]) / std[0];
                            var g = ((pixel.G / 255f) - mean[1]) / std[1];
                            var b = ((pixel.B / 255f) - mean[2]) / std[2];
                            tensor[0, 0, y, x] = r;
                            tensor[0, 1, y, x] = g;
                            tensor[0, 2, y, x] = b;
                        }
                    }

                    return tensor;
                }
            }
            catch (FormulaOcrException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new FormulaOcrException(OcrErrorCode.BadImage, "Failed to preprocess OCR input image: " + ex.Message, ex);
            }
        }

        private static byte[] DecodeBase64Image(string imageBase64)
        {
            var trimmed = imageBase64 == null ? string.Empty : imageBase64.Trim();
            var commaIndex = trimmed.IndexOf(',');
            if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && commaIndex >= 0)
            {
                trimmed = trimmed.Substring(commaIndex + 1);
            }

            return Convert.FromBase64String(trimmed);
        }

        private DenseTensor<float> RunEncoder(DenseTensor<float> pixelTensor)
        {
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_encoderInputName, pixelTensor)
            };

            using (var outputs = _encoderSession.Run(inputs))
            {
                var output = outputs.FirstOrDefault(x => string.Equals(x.Name, _encoderOutputName, StringComparison.OrdinalIgnoreCase));
                if (output == null)
                {
                    throw new FormulaOcrException(OcrErrorCode.InferenceFailed, "Encoder output tensor was not found.");
                }

                var tensor = output.AsTensor<float>();
                return CloneTensor(tensor);
            }
        }

        private List<long> GenerateTokenIds(DenseTensor<float> encoderHiddenStates, int maxTokens, long eosTokenId)
        {
            var tokenIds = new List<long> { _manifest.DecoderStartTokenId };
            for (var step = 0; step < maxTokens; step++)
            {
                var logits = RunDecoder(tokenIds, encoderHiddenStates);
                var nextId = ArgMaxFromLastLogits(logits);
                tokenIds.Add(nextId);
                if (nextId == eosTokenId)
                {
                    break;
                }
            }

            return tokenIds;
        }

        private DenseTensor<float> RunDecoder(List<long> tokenIds, DenseTensor<float> encoderHiddenStates)
        {
            var inputIdTensor = new DenseTensor<long>(new[] { 1, tokenIds.Count });
            for (var i = 0; i < tokenIds.Count; i++)
            {
                inputIdTensor[0, i] = tokenIds[i];
            }

            var encoderLength = encoderHiddenStates.Dimensions.Length > 1 ? encoderHiddenStates.Dimensions[1] : 1;
            var decoderAttentionMask = CreateOnesTensor(tokenIds.Count);
            var encoderAttentionMask = CreateOnesTensor(encoderLength);
            var positionIds = CreateRangeTensor(tokenIds.Count);
            var useCacheBranch = new DenseTensor<bool>(new[] { 1 });
            useCacheBranch[0] = false;

            var inputs = new List<NamedOnnxValue>();
            foreach (var inputName in _decoderInputNames)
            {
                if (string.Equals(inputName, "input_ids", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(inputName, "decoder_input_ids", StringComparison.OrdinalIgnoreCase))
                {
                    inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, inputIdTensor));
                    continue;
                }

                if (string.Equals(inputName, "encoder_hidden_states", StringComparison.OrdinalIgnoreCase))
                {
                    inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, encoderHiddenStates));
                    continue;
                }

                if (string.Equals(inputName, "attention_mask", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(inputName, "decoder_attention_mask", StringComparison.OrdinalIgnoreCase))
                {
                    inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, decoderAttentionMask));
                    continue;
                }

                if (string.Equals(inputName, "encoder_attention_mask", StringComparison.OrdinalIgnoreCase))
                {
                    inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, encoderAttentionMask));
                    continue;
                }

                if (string.Equals(inputName, "position_ids", StringComparison.OrdinalIgnoreCase))
                {
                    inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, positionIds));
                    continue;
                }

                if (string.Equals(inputName, "use_cache_branch", StringComparison.OrdinalIgnoreCase))
                {
                    inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, useCacheBranch));
                    continue;
                }

                throw new FormulaOcrException(
                    OcrErrorCode.ModelInitFailed,
                    "Decoder ONNX input is not supported by current OCR integration: " + inputName);
            }

            using (var outputs = _decoderSession.Run(inputs))
            {
                var logits = outputs.FirstOrDefault(x => string.Equals(x.Name, _decoderOutputName, StringComparison.OrdinalIgnoreCase));
                if (logits == null)
                {
                    throw new FormulaOcrException(OcrErrorCode.InferenceFailed, "Decoder logits tensor was not found.");
                }

                return CloneTensor(logits.AsTensor<float>());
            }
        }

        private static DenseTensor<long> CreateOnesTensor(int length)
        {
            var tensor = new DenseTensor<long>(new[] { 1, Math.Max(1, length) });
            for (var i = 0; i < tensor.Dimensions[1]; i++)
            {
                tensor[0, i] = 1L;
            }

            return tensor;
        }

        private static DenseTensor<long> CreateRangeTensor(int length)
        {
            var tensor = new DenseTensor<long>(new[] { 1, Math.Max(1, length) });
            for (var i = 0; i < tensor.Dimensions[1]; i++)
            {
                tensor[0, i] = i;
            }

            return tensor;
        }

        private static DenseTensor<float> CloneTensor(Tensor<float> source)
        {
            var dimensions = source.Dimensions.ToArray();
            var values = source.ToArray();
            return new DenseTensor<float>(values, dimensions);
        }

        private static long ArgMaxFromLastLogits(DenseTensor<float> logits)
        {
            if (logits == null || logits.Dimensions.Length < 2)
            {
                throw new FormulaOcrException(OcrErrorCode.InferenceFailed, "Decoder logits rank is invalid.");
            }

            var dims = logits.Dimensions.ToArray();
            var values = logits.ToArray();
            int vocabSize;
            int offset;

            if (dims.Length == 3)
            {
                var sequenceLength = dims[1];
                vocabSize = dims[2];
                offset = (sequenceLength - 1) * vocabSize;
            }
            else
            {
                var sequenceLength = dims[dims.Length - 2];
                vocabSize = dims[dims.Length - 1];
                offset = (sequenceLength - 1) * vocabSize;
            }

            if (vocabSize <= 0 || offset < 0 || offset + vocabSize > values.Length)
            {
                throw new FormulaOcrException(OcrErrorCode.InferenceFailed, "Decoder logits shape is invalid.");
            }

            var maxValue = float.MinValue;
            var maxIndex = 0;
            for (var i = 0; i < vocabSize; i++)
            {
                var value = values[offset + i];
                if (value > maxValue)
                {
                    maxValue = value;
                    maxIndex = i;
                }
            }

            return maxIndex;
        }

        private string DecodeTokenIds(IList<long> tokenIds)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < tokenIds.Count; i++)
            {
                var id = tokenIds[i];
                if (_specialTokenIds.Contains(id))
                {
                    continue;
                }

                if (id < 0 || id >= _idToToken.Length)
                {
                    continue;
                }

                var token = _idToToken[id];
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                builder.Append(token);
            }

            var encoded = builder.ToString();
            if (string.IsNullOrEmpty(encoded))
            {
                return string.Empty;
            }

            var decoded = _useByteLevelDecoder
                ? DecodeByteLevelText(encoded)
                : encoded;

            decoded = decoded.Replace("\r\n", "\n").Replace("\r", "\n");
            return decoded.Trim();
        }

        private void BuildTokenizerMappings(
            string tokenizerPath,
            ModelManifest manifest,
            out string[] idToToken,
            out HashSet<long> specialTokenIds)
        {
            var root = _serializer.DeserializeObject(File.ReadAllText(tokenizerPath, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null)
            {
                throw new FormulaOcrException(OcrErrorCode.ModelInitFailed, "Tokenizer JSON is invalid.");
            }

            var tokenById = new Dictionary<int, string>();
            var model = root.ContainsKey("model") ? root["model"] as Dictionary<string, object> : null;
            if (model == null || !model.ContainsKey("vocab"))
            {
                throw new FormulaOcrException(OcrErrorCode.ModelInitFailed, "Tokenizer vocabulary is missing.");
            }

            var vocabObject = model["vocab"];
            var vocabDict = vocabObject as Dictionary<string, object>;
            if (vocabDict != null)
            {
                foreach (var pair in vocabDict)
                {
                    var id = SafeConvertToInt(pair.Value, -1);
                    if (id >= 0)
                    {
                        tokenById[id] = pair.Key;
                    }
                }
            }
            else
            {
                var vocabArray = vocabObject as object[];
                if (vocabArray == null)
                {
                    throw new FormulaOcrException(OcrErrorCode.ModelInitFailed, "Tokenizer vocabulary format is not supported.");
                }

                for (var i = 0; i < vocabArray.Length; i++)
                {
                    var tuple = vocabArray[i] as object[];
                    if (tuple == null || tuple.Length == 0)
                    {
                        continue;
                    }

                    var token = tuple[0] == null ? string.Empty : tuple[0].ToString();
                    tokenById[i] = token;
                }
            }

            var maxId = tokenById.Keys.Count == 0 ? 0 : tokenById.Keys.Max();
            idToToken = new string[maxId + 1];
            foreach (var pair in tokenById)
            {
                if (pair.Key >= 0 && pair.Key < idToToken.Length)
                {
                    idToToken[pair.Key] = pair.Value;
                }
            }

            specialTokenIds = new HashSet<long>();
            var addedTokens = root.ContainsKey("added_tokens") ? root["added_tokens"] as object[] : null;
            if (addedTokens != null)
            {
                for (var i = 0; i < addedTokens.Length; i++)
                {
                    var addedToken = addedTokens[i] as Dictionary<string, object>;
                    if (addedToken == null)
                    {
                        continue;
                    }

                    var isSpecial = SafeConvertToBool(
                        addedToken.ContainsKey("special") ? addedToken["special"] : null,
                        false);
                    if (!isSpecial)
                    {
                        continue;
                    }

                    var id = SafeConvertToLong(
                        addedToken.ContainsKey("id") ? addedToken["id"] : null,
                        -1L);
                    if (id >= 0)
                    {
                        specialTokenIds.Add(id);
                    }
                }
            }

            specialTokenIds.Add(manifest.BosTokenId);
            specialTokenIds.Add(manifest.EosTokenId);
            specialTokenIds.Add(manifest.PadTokenId);

            _useByteLevelDecoder = DetectByteLevelDecoder(root);
            _byteDecoderMap = _useByteLevelDecoder ? CreateByteDecoderMap() : null;
        }

        private static bool DetectByteLevelDecoder(Dictionary<string, object> root)
        {
            if (root == null || !root.ContainsKey("decoder"))
            {
                return false;
            }

            var decoder = root["decoder"] as Dictionary<string, object>;
            if (decoder == null || !decoder.ContainsKey("type") || decoder["type"] == null)
            {
                return false;
            }

            return string.Equals(
                decoder["type"].ToString(),
                "ByteLevel",
                StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<char, byte> CreateByteDecoderMap()
        {
            var bs = new List<int>();
            for (var i = 33; i <= 126; i++)
            {
                bs.Add(i);
            }
            for (var i = 161; i <= 172; i++)
            {
                bs.Add(i);
            }
            for (var i = 174; i <= 255; i++)
            {
                bs.Add(i);
            }

            var cs = new List<int>(bs);
            var baseSet = new HashSet<int>(bs);
            var n = 0;
            for (var b = 0; b < 256; b++)
            {
                if (baseSet.Contains(b))
                {
                    continue;
                }

                bs.Add(b);
                cs.Add(256 + n);
                n++;
            }

            var map = new Dictionary<char, byte>(cs.Count);
            for (var i = 0; i < bs.Count; i++)
            {
                map[(char)cs[i]] = (byte)bs[i];
            }

            return map;
        }

        private string DecodeByteLevelText(string encoded)
        {
            if (string.IsNullOrEmpty(encoded))
            {
                return string.Empty;
            }

            var byteValues = new List<byte>(encoded.Length);
            for (var i = 0; i < encoded.Length; i++)
            {
                byte value;
                if (_byteDecoderMap != null && _byteDecoderMap.TryGetValue(encoded[i], out value))
                {
                    byteValues.Add(value);
                    continue;
                }

                var fallbackBytes = Encoding.UTF8.GetBytes(new[] { encoded[i] });
                byteValues.AddRange(fallbackBytes);
            }

            return Encoding.UTF8.GetString(byteValues.ToArray());
        }

        private static string GetString(Dictionary<string, object> root, string key, string fallback)
        {
            if (root == null || !root.ContainsKey(key) || root[key] == null)
            {
                return fallback;
            }

            var value = root[key].ToString();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static int GetInt(Dictionary<string, object> root, string key, int fallback)
        {
            if (root == null || !root.ContainsKey(key))
            {
                return fallback;
            }

            return SafeConvertToInt(root[key], fallback);
        }

        private static long GetLong(Dictionary<string, object> root, string key, long fallback)
        {
            if (root == null || !root.ContainsKey(key))
            {
                return fallback;
            }

            return SafeConvertToLong(root[key], fallback);
        }

        private static float[] GetFloatArray(Dictionary<string, object> root, string key)
        {
            if (root == null || !root.ContainsKey(key))
            {
                return null;
            }

            var arr = root[key] as object[];
            if (arr == null || arr.Length == 0)
            {
                return null;
            }

            var result = new float[arr.Length];
            for (var i = 0; i < arr.Length; i++)
            {
                result[i] = SafeConvertToFloat(arr[i], 0f);
            }

            return result;
        }

        private static int SafeConvertToInt(object value, int fallback)
        {
            if (value == null)
            {
                return fallback;
            }

            int parsedInt;
            if (value is int)
            {
                return (int)value;
            }

            if (value is long)
            {
                return (int)(long)value;
            }

            if (value is double)
            {
                return (int)(double)value;
            }

            if (int.TryParse(value.ToString(), out parsedInt))
            {
                return parsedInt;
            }

            return fallback;
        }

        private static long SafeConvertToLong(object value, long fallback)
        {
            if (value == null)
            {
                return fallback;
            }

            long parsedLong;
            if (value is long)
            {
                return (long)value;
            }

            if (value is int)
            {
                return (int)value;
            }

            if (value is double)
            {
                return (long)(double)value;
            }

            if (long.TryParse(value.ToString(), out parsedLong))
            {
                return parsedLong;
            }

            return fallback;
        }

        private static float SafeConvertToFloat(object value, float fallback)
        {
            if (value == null)
            {
                return fallback;
            }

            float parsed;
            if (value is float)
            {
                return (float)value;
            }

            if (value is double)
            {
                return (float)(double)value;
            }

            if (float.TryParse(value.ToString(), out parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static bool SafeConvertToBool(object value, bool fallback)
        {
            if (value == null)
            {
                return fallback;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            bool parsed;
            if (bool.TryParse(value.ToString(), out parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private sealed class ModelManifest
        {
            public string EncoderFile { get; set; }

            public string DecoderFile { get; set; }

            public string TokenizerFile { get; set; }

            public string GenerationConfigFile { get; set; }

            public int ImageSize { get; set; }

            public float[] PixelMean { get; set; }

            public float[] PixelStd { get; set; }

            public long BosTokenId { get; set; }

            public long EosTokenId { get; set; }

            public long PadTokenId { get; set; }

            public long DecoderStartTokenId { get; set; }

            public static ModelManifest CreateDefault()
            {
                return new ModelManifest
                {
                    EncoderFile = "encoder_model.onnx",
                    DecoderFile = "decoder_model.onnx",
                    TokenizerFile = "tokenizer.json",
                    GenerationConfigFile = "generation_config.json",
                    ImageSize = 384,
                    PixelMean = new[] { 0.5f, 0.5f, 0.5f },
                    PixelStd = new[] { 0.5f, 0.5f, 0.5f },
                    BosTokenId = 0,
                    EosTokenId = 2,
                    PadTokenId = 1,
                    DecoderStartTokenId = 2
                };
            }
        }
    }
}
