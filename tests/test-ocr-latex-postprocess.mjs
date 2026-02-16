// SlideTeX Note: Regression checks for OCR LaTeX postprocess rules.
import assert from "node:assert/strict";
import path from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "..");
const require = createRequire(import.meta.url);
const postprocess = require(path.join(
  repoRoot,
  "src",
  "SlideTeX.WebUI",
  "assets",
  "js",
  "ocr-latex-postprocess.js"
));

if (!postprocess || typeof postprocess.sanitizeOcrLatex !== "function") {
  throw new Error("Failed to load sanitizeOcrLatex from OCR postprocess module.");
}

const { sanitizeOcrLatex } = postprocess;

const exactCases = [
  {
    name: "command_textlike_join",
    input: "\\mathrm { i f }\\; x = y",
    expected: "\\mathrm{if}\\; x = y"
  },
  {
    name: "environment_name_compaction",
    input: "\\begin { c a s e s } x \\end { c a s e s }",
    expected: "\\begin{cases} x \\end{cases}"
  },
  {
    name: "subscript_superscript_compaction",
    input: "Y _ { i j } = x ^ { 1 2 }",
    expected: "Y_{ij} = x^{12}"
  },
  {
    name: "do_not_compact_math_expression",
    input: "{ x + y }",
    expected: "{x + y}"
  },
  {
    name: "verbatim_should_be_untouched",
    input: "\\verb| a _ { i j } | + Y _ { i j }",
    expected: "\\verb| a _ { i j } | + Y_{ij}"
  },
  {
    name: "linebreak_command_with_optional_arg",
    input: "a \\\\ [1ex] b",
    expected: "a\\\\[1ex] b"
  },
  {
    name: "punctuation_and_colon_range",
    input: "\\sum _ { k : k \\sim i } y _ { i k } ,",
    expected: "\\sum_{k:k\\sim i} y_{ik},"
  }
];

for (const testCase of exactCases) {
  const actual = sanitizeOcrLatex(testCase.input);
  assert.equal(
    actual,
    testCase.expected,
    `[${testCase.name}] expected "${testCase.expected}" but got "${actual}"`
  );
}

const longSampleInput = "Y _ { i j } = \\begin{cases} { \\, \\sum _ { k : k \\sim i } y _ { i k } , } & { \\mathrm { i f } \\; i = j } \\\\{ \\, - y _ { i j } , } & { \\mathrm { i f } \\; i \\neq j \\; \\mathrm { a n d } \\; i \\sim j } \\\\{ \\, 0 , } & { \\mathrm { o t h e r w i s e } . } \\\\\\end{cases}";
const longSampleOutput = sanitizeOcrLatex(longSampleInput);

assert.ok(longSampleOutput.includes("Y_{ij} ="), "long_sample should compact Y_{ij}");
assert.ok(longSampleOutput.includes("\\mathrm{if}"), "long_sample should compact \\mathrm{if}");
assert.ok(longSampleOutput.includes("\\mathrm{and}"), "long_sample should compact \\mathrm{and}");
assert.ok(longSampleOutput.includes("\\mathrm{otherwise}"), "long_sample should compact \\mathrm{otherwise}");
assert.ok(longSampleOutput.includes("\\sum_{k:k\\sim i}"), "long_sample should normalize k : k \\sim i");
assert.ok(!longSampleOutput.includes("\\mathrm { i f }"), "long_sample should remove split-text style");

console.log(`OCR postprocess checks passed. cases=${exactCases.length + 1}`);
