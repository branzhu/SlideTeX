// SlideTeX Note: Lightweight OCR LaTeX postprocess pipeline for readability improvements.
(function (globalScope) {
  "use strict";

  const TEXTLIKE_COMMANDS = new Set([
    "mathrm",
    "text",
    "textrm",
    "mathbf",
    "mathit",
    "mathsf",
    "mathtt",
    "operatorname"
  ]);

  function sanitizeOcrLatex(source) {
    let result = String(source || "");
    if (!result) {
      return "";
    }

    result = preNormalizeWhitespace(result);
    if (!result) {
      return "";
    }

    const protectedState = protectVerbatimSegments(result);
    result = protectedState.maskedText;

    result = normalizeStructuralSafeZone(result);
    result = compactTextlikeCommandArgs(result);
    result = compactBraceTokenSequences(result);
    result = normalizePunctuationAndRange(result);

    result = restoreVerbatimSegments(result, protectedState.segments);
    return result.trim();
  }

  function preNormalizeWhitespace(text) {
    let result = String(text || "");
    result = result.replace(/\r\n?/g, "\n");
    result = result.replace(/[\u0009\u000b\u000c\u00a0\u1680\u2000-\u200a\u202f\u205f\u3000]/g, " ");

    const lines = result.split("\n");
    for (let i = 0; i < lines.length; i++) {
      lines[i] = lines[i].replace(/ {2,}/g, " ").trim();
    }

    result = lines.join("\n");
    result = result.replace(/\n{3,}/g, "\n\n");
    return result.trim();
  }

  function protectVerbatimSegments(text) {
    let maskedText = "";
    const segments = [];

    for (let i = 0; i < text.length;) {
      if (text.startsWith("\\verb", i)) {
        let cursor = i + 5;
        if (cursor < text.length && text[cursor] === "*") {
          cursor++;
        }

        if (cursor < text.length) {
          const delimiter = text[cursor];
          if (!/[A-Za-z0-9\s]/.test(delimiter)) {
            let end = cursor + 1;
            while (end < text.length && text[end] !== delimiter) {
              end++;
            }

            if (end < text.length) {
              const placeholder = `@@OCR_VERBATIM_${segments.length}@@`;
              segments.push(text.slice(i, end + 1));
              maskedText += placeholder;
              i = end + 1;
              continue;
            }
          }
        }
      }

      maskedText += text[i];
      i++;
    }

    return {
      maskedText,
      segments
    };
  }

  function restoreVerbatimSegments(text, segments) {
    if (!segments || segments.length === 0) {
      return text;
    }

    return text.replace(/@@OCR_VERBATIM_(\d+)@@/g, (match, indexText) => {
      const index = Number(indexText);
      if (!Number.isInteger(index) || index < 0 || index >= segments.length) {
        return match;
      }

      return segments[index];
    });
  }

  function normalizeStructuralSafeZone(text) {
    let result = text;
    result = result.replace(/([_^])\s+\{/g, "$1{");
    result = result.replace(/\s+([_^]\{)/g, "$1");
    result = result.replace(/\{\s+/g, "{");
    result = result.replace(/\s+\}/g, "}");
    result = result.replace(/\[\s+/g, "[");
    result = result.replace(/\s+\]/g, "]");
    result = normalizeEnvironmentDeclarations(result);
    result = result.replace(/[ \t]*\\\\(?!\[)[ \t]*/g, "\\\\");
    result = result.replace(/\\\\\s+\[/g, "\\\\[");
    return result;
  }

  function normalizeEnvironmentDeclarations(text) {
    return text.replace(/\\(begin|end)\s*\{\s*([^{}]+?)\s*\}/g, (match, kind, envNameRaw) => {
      const compact = String(envNameRaw || "").replace(/\s+/g, "");
      if (/^[A-Za-z*]+$/.test(compact)) {
        return `\\${kind}{${compact}}`;
      }

      return `\\${kind}{${String(envNameRaw || "").trim()}}`;
    });
  }

  function compactTextlikeCommandArgs(text) {
    let result = "";
    let i = 0;

    while (i < text.length) {
      if (text[i] !== "\\") {
        result += text[i];
        i++;
        continue;
      }

      let commandEnd = i + 1;
      while (commandEnd < text.length && /[A-Za-z]/.test(text[commandEnd])) {
        commandEnd++;
      }

      if (commandEnd <= i + 1) {
        result += text[i];
        i++;
        continue;
      }

      const commandName = text.slice(i + 1, commandEnd);
      if (!TEXTLIKE_COMMANDS.has(commandName)) {
        result += text.slice(i, commandEnd);
        i = commandEnd;
        continue;
      }

      result += `\\${commandName}`;
      i = commandEnd;

      const whitespaceStart = i;
      while (i < text.length && /[ \t]/.test(text[i])) {
        i++;
      }

      if (i >= text.length || text[i] !== "{") {
        result += text.slice(whitespaceStart, i);
        continue;
      }

      const closeIndex = findMatchingBrace(text, i);
      if (closeIndex < 0) {
        result += text.slice(whitespaceStart, i + 1);
        i++;
        continue;
      }

      const argument = text.slice(i + 1, closeIndex);
      const compacted = compactTextlikeArgument(argument);
      result += `{${compacted}}`;
      i = closeIndex + 1;
    }

    return result;
  }

  function findMatchingBrace(text, openIndex) {
    if (openIndex < 0 || openIndex >= text.length || text[openIndex] !== "{") {
      return -1;
    }

    let depth = 0;
    for (let i = openIndex; i < text.length; i++) {
      const ch = text[i];
      if (ch === "\\") {
        i++;
        continue;
      }

      if (ch === "{") {
        depth++;
        continue;
      }

      if (ch === "}") {
        depth--;
        if (depth === 0) {
          return i;
        }
      }
    }

    return -1;
  }

  function compactTextlikeArgument(argument) {
    let result = String(argument || "").trim();
    if (!result) {
      return "";
    }

    result = result.replace(/\s+/g, " ");
    if (/[\\+\-=,.:;<>]/.test(result)) {
      return result;
    }

    const tokens = result.split(" ").filter(Boolean);
    if (tokens.length < 2) {
      return result;
    }

    const allSingleChars = tokens.every((token) => /^[A-Za-z0-9']$/.test(token));
    if (!allSingleChars) {
      return result;
    }

    return tokens.join("");
  }

  function compactBraceTokenSequences(text) {
    let result = text;

    result = result.replace(/([_^])\{([^{}]+)\}/g, (match, marker, content) => {
      const compacted = tryCompactSimpleTokenSequence(content);
      if (compacted === null) {
        return `${marker}{${content}}`;
      }

      return `${marker}{${compacted}}`;
    });

    result = result.replace(/\{([^{}]+)\}/g, (match, content) => {
      const compacted = tryCompactSimpleTokenSequence(content);
      if (compacted === null) {
        return `{${content}}`;
      }

      return `{${compacted}}`;
    });

    return result;
  }

  function tryCompactSimpleTokenSequence(content) {
    const normalized = String(content || "").replace(/[ \t]+/g, " ").trim();
    if (!normalized) {
      return null;
    }

    if (/[\\+\-*/=<>:;,.]/.test(normalized)) {
      return null;
    }

    const tokens = normalized.split(" ").filter(Boolean);
    if (tokens.length < 2) {
      return null;
    }

    if (!tokens.every((token) => /^[A-Za-z0-9]$/.test(token))) {
      return null;
    }

    return tokens.join("");
  }

  function normalizePunctuationAndRange(text) {
    let result = text.replace(/(\S)\s+([,.:;])/g, (match, left, punctuation, offset, source) => {
      if (left === "," && offset > 0 && source[offset - 1] === "\\") {
        return match;
      }

      return `${left}${punctuation}`;
    });

    result = result.replace(/\{([^{}]+)\}/g, (match, content) => {
      return `{${normalizeColonRange(content)}}`;
    });

    return result;
  }

  function normalizeColonRange(content) {
    return String(content || "").replace(
      /([A-Za-z0-9])\s*:\s*([A-Za-z0-9])\s*(?=(\\(?:sim|neq|leq|geq|approx|in)\b|[<>=]))/g,
      "$1:$2"
    );
  }

  const api = {
    sanitizeOcrLatex
  };

  if (typeof module !== "undefined" && module.exports) {
    module.exports = api;
  }
  if (globalScope) {
    globalScope.SlideTeXOcrPostprocess = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : null);
