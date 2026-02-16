// SlideTeX Note: Unit tests for i18n.js pure functions.
import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";

const require = createRequire(import.meta.url);
const i18n = require("../src/SlideTeX.WebUI/assets/js/i18n.js");

const { normalizeLocale, deepMerge, formatMessage, getByPath, isPlainObject } = i18n;

// --- normalizeLocale ---

describe("normalizeLocale", () => {
  it("maps 'zh' to zh-CN", () => {
    assert.equal(normalizeLocale("zh"), "zh-CN");
  });

  it("maps 'zh-TW' to zh-CN", () => {
    assert.equal(normalizeLocale("zh-TW"), "zh-CN");
  });

  it("maps 'zh-Hans' to zh-CN", () => {
    assert.equal(normalizeLocale("zh-Hans"), "zh-CN");
  });

  it("maps 'en' to en-US", () => {
    assert.equal(normalizeLocale("en"), "en-US");
  });

  it("maps 'en-GB' to en-US", () => {
    assert.equal(normalizeLocale("en-GB"), "en-US");
  });

  it("returns en-US for empty string", () => {
    assert.equal(normalizeLocale(""), "en-US");
  });

  it("returns en-US for null", () => {
    assert.equal(normalizeLocale(null), "en-US");
  });

  it("normalizes unknown two-part locale", () => {
    assert.equal(normalizeLocale("fr-FR"), "fr-FR");
  });

  it("lowercases single-segment locale", () => {
    assert.equal(normalizeLocale("FR"), "fr");
  });
});

// --- deepMerge ---

describe("deepMerge", () => {
  it("merges nested objects", () => {
    const base = { a: { b: 1, c: 2 } };
    const override = { a: { c: 3, d: 4 } };
    assert.deepEqual(deepMerge(base, override), { a: { b: 1, c: 3, d: 4 } });
  });

  it("override leaf replaces base leaf", () => {
    assert.deepEqual(deepMerge({ x: 1 }, { x: 2 }), { x: 2 });
  });

  it("preserves base keys not in override", () => {
    assert.deepEqual(deepMerge({ a: 1, b: 2 }, { b: 3 }), { a: 1, b: 3 });
  });

  it("returns copy of override when base is not an object", () => {
    assert.deepEqual(deepMerge(null, { a: 1 }), { a: 1 });
  });

  it("returns copy of base when override is not an object", () => {
    assert.deepEqual(deepMerge({ a: 1 }, null), { a: 1 });
  });

  it("returns override directly for non-object base and non-object override", () => {
    assert.equal(deepMerge("hello", "world"), "world");
  });
});

// --- formatMessage ---

describe("formatMessage", () => {
  it("replaces single parameter", () => {
    assert.equal(formatMessage("Hello {name}", { name: "World" }), "Hello World");
  });

  it("replaces multiple parameters", () => {
    assert.equal(
      formatMessage("{w}x{h}px @ {dpi} DPI", { w: 100, h: 50, dpi: 300 }),
      "100x50px @ 300 DPI"
    );
  });

  it("preserves placeholder when param is missing", () => {
    assert.equal(formatMessage("Hello {name}", { other: "x" }), "Hello {name}");
  });

  it("returns template unchanged when params is null", () => {
    assert.equal(formatMessage("Hello {name}", null), "Hello {name}");
  });

  it("returns template unchanged when no placeholders", () => {
    assert.equal(formatMessage("No params here", { a: 1 }), "No params here");
  });
});

// --- getByPath ---

describe("getByPath", () => {
  it("resolves nested path", () => {
    assert.equal(getByPath({ a: { b: { c: "found" } } }, "a.b.c"), "found");
  });

  it("returns null for missing key", () => {
    assert.equal(getByPath({ a: 1 }, "b"), null);
  });

  it("returns null for deeply missing path", () => {
    assert.equal(getByPath({ a: { b: 1 } }, "a.c.d"), null);
  });

  it("returns null for empty path", () => {
    // empty path splits to [""] which won't match
    assert.equal(getByPath({ a: 1 }, ""), null);
  });

  it("returns the root value for single-segment path", () => {
    assert.equal(getByPath({ key: "val" }, "key"), "val");
  });
});

// --- isPlainObject ---

describe("isPlainObject", () => {
  it("returns true for plain object", () => {
    assert.equal(isPlainObject({}), true);
  });

  it("returns false for array", () => {
    assert.equal(isPlainObject([]), false);
  });

  it("returns false for null", () => {
    assert.equal(isPlainObject(null), false);
  });

  it("returns false for string", () => {
    assert.equal(isPlainObject("hello"), false);
  });

  it("returns false for number", () => {
    assert.equal(isPlainObject(42), false);
  });
});
