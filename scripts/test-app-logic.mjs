// SlideTeX Note: Unit tests for app-logic.js pure functions.
import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const logic = require("../src/SlideTeX.WebUI/assets/js/app-logic.js");

const {
  shouldUseDisplayMode,
  resolveDisplayMode,
  extractTagTokensFromLatex,
  normalizeTagToken,
  normalizeOptions,
  normalizeDisplayMode,
  toBoolean,
  stripAutoNumbering,
  DEFAULT_OPTIONS,
} = logic;

// --- shouldUseDisplayMode ---

describe("shouldUseDisplayMode", () => {
  it("returns true for align environment", () => {
    assert.equal(shouldUseDisplayMode("\\begin{align}x\\end{align}"), true);
  });

  it("returns true for align* environment", () => {
    assert.equal(shouldUseDisplayMode("\\begin{align*}x\\end{align*}"), true);
  });

  it("returns true for cases environment", () => {
    assert.equal(shouldUseDisplayMode("\\begin{cases}a\\\\b\\end{cases}"), true);
  });

  it("returns true for gather environment", () => {
    assert.equal(shouldUseDisplayMode("\\begin{gather}x\\end{gather}"), true);
  });

  it("returns true for equation environment", () => {
    assert.equal(shouldUseDisplayMode("\\begin{equation}E=mc^2\\end{equation}"), true);
  });

  it("returns true for split environment", () => {
    assert.equal(shouldUseDisplayMode("\\begin{split}a\\end{split}"), true);
  });

  it("returns true for matrix environment", () => {
    assert.equal(shouldUseDisplayMode("\\begin{matrix}1&2\\end{matrix}"), true);
  });

  it("returns true for pmatrix environment", () => {
    assert.equal(shouldUseDisplayMode("\\begin{pmatrix}1\\end{pmatrix}"), true);
  });

  it("returns true for bmatrix environment", () => {
    assert.equal(shouldUseDisplayMode("\\begin{bmatrix}1\\end{bmatrix}"), true);
  });

  it("returns true for Bmatrix environment", () => {
    assert.equal(shouldUseDisplayMode("\\begin{Bmatrix}1\\end{Bmatrix}"), true);
  });

  it("returns true for vmatrix environment", () => {
    assert.equal(shouldUseDisplayMode("\\begin{vmatrix}1\\end{vmatrix}"), true);
  });

  it("returns true for Vmatrix environment", () => {
    assert.equal(shouldUseDisplayMode("\\begin{Vmatrix}1\\end{Vmatrix}"), true);
  });

  it("returns true for aligned environment", () => {
    assert.equal(shouldUseDisplayMode("\\begin{aligned}x&=1\\end{aligned}"), true);
  });

  it("returns true for multi-line with \\\\", () => {
    assert.equal(shouldUseDisplayMode("a \\\\ b"), true);
  });

  it("returns false for simple inline formula", () => {
    assert.equal(shouldUseDisplayMode("x^2 + y^2 = z^2"), false);
  });

  it("returns false for non-string input (number)", () => {
    assert.equal(shouldUseDisplayMode(42), false);
  });

  it("returns false for null", () => {
    assert.equal(shouldUseDisplayMode(null), false);
  });

  it("returns false for undefined", () => {
    assert.equal(shouldUseDisplayMode(undefined), false);
  });
});

// --- resolveDisplayMode ---

describe("resolveDisplayMode", () => {
  it("returns inline when explicitly selected", () => {
    assert.equal(resolveDisplayMode("\\begin{align}x\\end{align}", "inline"), "inline");
  });

  it("returns display when explicitly selected", () => {
    assert.equal(resolveDisplayMode("x^2", "display"), "display");
  });

  it("returns display for auto with display-worthy latex", () => {
    assert.equal(resolveDisplayMode("\\begin{equation}E=mc^2\\end{equation}", "auto"), "display");
  });

  it("returns inline for auto with simple latex", () => {
    assert.equal(resolveDisplayMode("x^2", "auto"), "inline");
  });

  it("falls back to heuristic for unknown mode", () => {
    assert.equal(resolveDisplayMode("x^2", "bogus"), "inline");
  });

  it("falls back to heuristic for null mode", () => {
    assert.equal(resolveDisplayMode("\\begin{cases}a\\end{cases}", null), "display");
  });
});

// --- extractTagTokensFromLatex ---

describe("extractTagTokensFromLatex", () => {
  it("extracts single \\tag{1}", () => {
    assert.deepEqual(extractTagTokensFromLatex("E=mc^2 \\tag{1}"), ["(1)"]);
  });

  it("extracts \\tag*{a}", () => {
    assert.deepEqual(extractTagTokensFromLatex("x \\tag*{a}"), ["(a)"]);
  });

  it("extracts multiple tags", () => {
    assert.deepEqual(
      extractTagTokensFromLatex("a \\tag{1} b \\tag{2}"),
      ["(1)", "(2)"]
    );
  });

  it("returns empty array when no tags", () => {
    assert.deepEqual(extractTagTokensFromLatex("x^2 + y^2"), []);
  });

  it("handles null input", () => {
    assert.deepEqual(extractTagTokensFromLatex(null), []);
  });

  it("handles undefined input", () => {
    assert.deepEqual(extractTagTokensFromLatex(undefined), []);
  });

  it("skips empty tag content", () => {
    assert.deepEqual(extractTagTokensFromLatex("\\tag{}"), []);
  });

  it("preserves already-parenthesized tokens", () => {
    assert.deepEqual(extractTagTokensFromLatex("\\tag{(i)}"), ["(i)"]);
  });
});

// --- normalizeTagToken ---

describe("normalizeTagToken", () => {
  it("wraps plain text in parentheses", () => {
    assert.equal(normalizeTagToken("1"), "(1)");
  });

  it("preserves already-parenthesized text", () => {
    assert.equal(normalizeTagToken("(a)"), "(a)");
  });

  it("returns empty string for empty input", () => {
    assert.equal(normalizeTagToken(""), "");
  });

  it("returns empty string for null", () => {
    assert.equal(normalizeTagToken(null), "");
  });

  it("trims whitespace", () => {
    assert.equal(normalizeTagToken("  x  "), "(x)");
  });
});

// --- normalizeOptions ---

describe("normalizeOptions", () => {
  it("returns defaults for null input", () => {
    const result = normalizeOptions(null);
    assert.equal(result.fontPt, DEFAULT_OPTIONS.fontPt);
    assert.equal(result.dpi, DEFAULT_OPTIONS.dpi);
    assert.equal(result.colorHex, DEFAULT_OPTIONS.colorHex);
    assert.equal(result.isTransparent, DEFAULT_OPTIONS.isTransparent);
    assert.equal(result.displayMode, DEFAULT_OPTIONS.displayMode);
  });

  it("returns defaults for undefined input", () => {
    const result = normalizeOptions(undefined);
    assert.equal(result.fontPt, DEFAULT_OPTIONS.fontPt);
  });

  it("accepts camelCase properties", () => {
    const result = normalizeOptions({ fontPt: 24, dpi: 600, colorHex: "#ff0000", isTransparent: false, displayMode: "inline" });
    assert.equal(result.fontPt, 24);
    assert.equal(result.dpi, 600);
    assert.equal(result.colorHex, "#ff0000");
    assert.equal(result.isTransparent, false);
    assert.equal(result.displayMode, "inline");
  });

  it("accepts PascalCase properties", () => {
    const result = normalizeOptions({ FontPt: 36, Dpi: 150, ColorHex: "#00ff00", IsTransparent: "true", DisplayMode: "display" });
    assert.equal(result.fontPt, 36);
    assert.equal(result.dpi, 150);
    assert.equal(result.colorHex, "#00ff00");
    assert.equal(result.isTransparent, true);
    assert.equal(result.displayMode, "display");
  });

  it("camelCase takes precedence over PascalCase", () => {
    const result = normalizeOptions({ fontPt: 10, FontPt: 99 });
    assert.equal(result.fontPt, 10);
  });

  it("fills missing fields with defaults", () => {
    const result = normalizeOptions({ fontPt: 48 });
    assert.equal(result.fontPt, 48);
    assert.equal(result.dpi, DEFAULT_OPTIONS.dpi);
    assert.equal(result.colorHex, DEFAULT_OPTIONS.colorHex);
  });
});

// --- normalizeDisplayMode ---

describe("normalizeDisplayMode", () => {
  it("returns inline for 'inline'", () => {
    assert.equal(normalizeDisplayMode("inline"), "inline");
  });

  it("returns display for 'display'", () => {
    assert.equal(normalizeDisplayMode("display"), "display");
  });

  it("returns auto for 'auto'", () => {
    assert.equal(normalizeDisplayMode("auto"), "auto");
  });

  it("is case-insensitive", () => {
    assert.equal(normalizeDisplayMode("INLINE"), "inline");
    assert.equal(normalizeDisplayMode("Display"), "display");
  });

  it("returns auto for unknown value", () => {
    assert.equal(normalizeDisplayMode("bogus"), "auto");
  });

  it("returns auto for null", () => {
    assert.equal(normalizeDisplayMode(null), "auto");
  });

  it("returns auto for undefined", () => {
    assert.equal(normalizeDisplayMode(undefined), "auto");
  });
});

// --- toBoolean ---

describe("toBoolean", () => {
  it("returns true for boolean true", () => {
    assert.equal(toBoolean(true), true);
  });

  it("returns false for boolean false", () => {
    assert.equal(toBoolean(false), false);
  });

  it("returns true for string 'true'", () => {
    assert.equal(toBoolean("true"), true);
  });

  it("returns true for string 'True'", () => {
    assert.equal(toBoolean("True"), true);
  });

  it("returns false for string 'false'", () => {
    assert.equal(toBoolean("false"), false);
  });

  it("returns false for empty string", () => {
    assert.equal(toBoolean(""), false);
  });

  it("coerces truthy number to true", () => {
    assert.equal(toBoolean(1), true);
  });

  it("coerces 0 to false", () => {
    assert.equal(toBoolean(0), false);
  });

  it("coerces null to false", () => {
    assert.equal(toBoolean(null), false);
  });
});

// --- stripAutoNumbering ---

describe("stripAutoNumbering", () => {
  it("adds star to equation environment", () => {
    assert.equal(
      stripAutoNumbering("\\begin{equation}E=mc^2\\end{equation}"),
      "\\begin{equation*}E=mc^2\\end{equation*}"
    );
  });

  it("adds star to align environment", () => {
    assert.equal(
      stripAutoNumbering("\\begin{align}a\\\\b\\end{align}"),
      "\\begin{align*}a\\\\b\\end{align*}"
    );
  });

  it("adds star to gather environment", () => {
    assert.equal(
      stripAutoNumbering("\\begin{gather}x\\end{gather}"),
      "\\begin{gather*}x\\end{gather*}"
    );
  });

  it("does not double-star already starred environments", () => {
    const input = "\\begin{equation*}E=mc^2\\end{equation*}";
    assert.equal(stripAutoNumbering(input), input);
  });

  it("leaves non-matching environments untouched", () => {
    const input = "\\begin{split}a\\end{split}";
    assert.equal(stripAutoNumbering(input), input);
  });

  it("handles multiple environments in one string", () => {
    const input = "\\begin{equation}a\\end{equation} \\begin{align}b\\end{align}";
    assert.equal(
      stripAutoNumbering(input),
      "\\begin{equation*}a\\end{equation*} \\begin{align*}b\\end{align*}"
    );
  });
});
