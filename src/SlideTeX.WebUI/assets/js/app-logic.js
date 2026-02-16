// SlideTeX Note: Pure logic functions extracted from app.js for testability.
(function (globalScope) {
  "use strict";

  var DEFAULT_OPTIONS = Object.freeze({
    fontPt: 18,
    dpi: 300,
    colorHex: "#000000",
    isTransparent: true,
    displayMode: "auto",
  });

  function toBoolean(value) {
    if (typeof value === "boolean") {
      return value;
    }

    if (typeof value === "string") {
      return value.toLowerCase() === "true";
    }

    return Boolean(value);
  }

  function normalizeDisplayMode(value) {
    var normalized = String(value == null ? "auto" : value).trim().toLowerCase();
    if (normalized === "inline" || normalized === "display" || normalized === "auto") {
      return normalized;
    }

    return "auto";
  }

  // Heuristic for selecting display mode when user picks "auto".
  function shouldUseDisplayMode(latex) {
    if (typeof latex !== "string") {
      return false;
    }

    var displayOnlyEnvironment = /\\begin\{(?:align\*?|aligned|gather\*?|equation\*?|split|cases|matrix|pmatrix|bmatrix|Bmatrix|vmatrix|Vmatrix)\}/;
    if (displayOnlyEnvironment.test(latex)) {
      return true;
    }

    // Multi-line formulas usually require block rendering.
    return /\\\\/.test(latex);
  }

  // Resolves final display mode based on explicit selection or LaTeX heuristics.
  function resolveDisplayMode(latex, selectedMode) {
    if (selectedMode === "inline") {
      return "inline";
    }

    if (selectedMode === "display") {
      return "display";
    }

    return shouldUseDisplayMode(latex) ? "display" : "inline";
  }

  function extractTagTokensFromLatex(latex) {
    var tags = [];
    var pattern = /\\tag\*?\s*\{([^{}]*)\}/g;
    var match = null;
    while ((match = pattern.exec(String(latex == null ? "" : latex))) !== null) {
      var token = normalizeTagToken(match[1]);
      if (token.length > 0) {
        tags.push(token);
      }
    }
    return tags;
  }

  function normalizeTagToken(raw) {
    var text = String(raw == null ? "" : raw).trim();
    if (!text) {
      return "";
    }
    if (text.startsWith("(") && text.endsWith(")")) {
      return text;
    }
    return "(" + text + ")";
  }

  function normalizeOptions(raw) {
    var source = raw == null ? {} : raw;

    return {
      fontPt: Number(source.fontPt != null ? source.fontPt : (source.FontPt != null ? source.FontPt : DEFAULT_OPTIONS.fontPt)),
      dpi: Number(source.dpi != null ? source.dpi : (source.Dpi != null ? source.Dpi : DEFAULT_OPTIONS.dpi)),
      colorHex: String(source.colorHex != null ? source.colorHex : (source.ColorHex != null ? source.ColorHex : DEFAULT_OPTIONS.colorHex)),
      isTransparent: toBoolean(source.isTransparent != null ? source.isTransparent : (source.IsTransparent != null ? source.IsTransparent : DEFAULT_OPTIONS.isTransparent)),
      displayMode: normalizeDisplayMode(source.displayMode != null ? source.displayMode : (source.DisplayMode != null ? source.DisplayMode : DEFAULT_OPTIONS.displayMode)),
    };
  }

  function stripAutoNumbering(latex) {
    return latex
      .replace(/\\begin\{(equation|align|gather)\}/g, '\\begin{$1*}')
      .replace(/\\end\{(equation|align|gather)\}/g, '\\end{$1*}');
  }

  var api = {
    shouldUseDisplayMode: shouldUseDisplayMode,
    resolveDisplayMode: resolveDisplayMode,
    extractTagTokensFromLatex: extractTagTokensFromLatex,
    normalizeTagToken: normalizeTagToken,
    normalizeOptions: normalizeOptions,
    normalizeDisplayMode: normalizeDisplayMode,
    toBoolean: toBoolean,
    stripAutoNumbering: stripAutoNumbering,
    DEFAULT_OPTIONS: DEFAULT_OPTIONS,
  };

  if (typeof module !== "undefined" && module.exports) {
    module.exports = api;
  }
  if (globalScope) {
    globalScope.SlideTeXAppLogic = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : null);
