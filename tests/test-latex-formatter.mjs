// SlideTeX Note: Unit tests for LaTeX formatter (whitespace-only formatting).
import assert from "node:assert/strict";
import path from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "..");
const require = createRequire(import.meta.url);
const formatter = require(path.join(
  repoRoot,
  "src",
  "SlideTeX.WebUI",
  "assets",
  "js",
  "latex-formatter.js"
));

if (!formatter || typeof formatter.formatLatex !== "function") {
  throw new Error("Failed to load formatLatex from latex-formatter module.");
}

const { formatLatex } = formatter;

const cases = [
  {
    name: "empty_input",
    input: "",
    expected: ""
  },
  {
    name: "short_formula_unchanged",
    input: "\\frac{a}{b}",
    expected: "\\frac{a}{b}"
  },
  {
    name: "multiline_no_env",
    input: "a + b\nc + d",
    expected: "a + b\nc + d"
  },
  {
    name: "simple_cases_env",
    input: "\\begin{cases} x & y=1 \\\\ z & y=2 \\end{cases}",
    expected:
      "\\begin{cases}\n" +
      "  x & y=1 \\\\\n" +
      "  z & y=2\n" +
      "\\end{cases}"
  },
  {
    name: "nested_env",
    input: "\\begin{aligned} \\begin{cases} a & b \\\\ c & d \\end{cases} \\end{aligned}",
    expected:
      "\\begin{aligned}\n" +
      "  \\begin{cases}\n" +
      "    a & b \\\\\n" +
      "    c & d\n" +
      "  \\end{cases}\n" +
      "\\end{aligned}"
  },
  {
    name: "linebreak_newline",
    input: "\\begin{aligned} a \\\\ b \\\\ c \\end{aligned}",
    expected:
      "\\begin{aligned}\n" +
      "  a \\\\\n" +
      "  b \\\\\n" +
      "  c\n" +
      "\\end{aligned}"
  },
  {
    name: "ampersand_spacing",
    input: "\\begin{aligned} x&=y \\end{aligned}",
    expected:
      "\\begin{aligned}\n" +
      "  x & =y\n" +
      "\\end{aligned}"
  },
  {
    name: "matrix_env",
    input: "\\begin{pmatrix} 1 & 0 \\\\ 0 & 1 \\end{pmatrix}",
    expected:
      "\\begin{pmatrix}\n" +
      "  1 & 0 \\\\\n" +
      "  0 & 1\n" +
      "\\end{pmatrix}"
  },
  {
    name: "already_formatted",
    input:
      "\\begin{cases}\n" +
      "  x & y=1 \\\\\n" +
      "  z & y=2\n" +
      "\\end{cases}",
    expected:
      "\\begin{cases}\n" +
      "  x & y=1 \\\\\n" +
      "  z & y=2\n" +
      "\\end{cases}"
  }
];

let passed = 0;
for (const tc of cases) {
  const actual = formatLatex(tc.input);
  assert.equal(
    actual,
    tc.expected,
    `[${tc.name}]\n  expected: ${JSON.stringify(tc.expected)}\n  actual:   ${JSON.stringify(actual)}`
  );
  passed++;
}

console.log(`LaTeX formatter tests passed. cases=${passed}`);