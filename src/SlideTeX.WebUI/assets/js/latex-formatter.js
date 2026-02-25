// SlideTeX Note: Pure whitespace formatter for LaTeX — adds indentation and line breaks without changing semantics.
(function (globalScope) {
  "use strict";

  var INDENT = "  ";

  function formatLatex(source) {
    var text = String(source == null ? "" : source).trim();
    if (!text) {
      return "";
    }

    // Short formula without environments — return as-is.
    if (!/\\begin\{/.test(text)) {
      return text;
    }

    var tokens = tokenize(text);
    var lines = [];
    var depth = 0;
    var currentLine = "";

    for (var i = 0; i < tokens.length; i++) {
      var token = tokens[i];

      if (token.type === "begin") {
        pushLine(lines, currentLine, depth);
        currentLine = "";
        pushLine(lines, token.raw, depth);
        depth++;
      } else if (token.type === "end") {
        pushLine(lines, currentLine, depth);
        currentLine = "";
        depth = Math.max(0, depth - 1);
        pushLine(lines, token.raw, depth);
      } else if (token.type === "linebreak") {
        if (currentLine.length > 0 && currentLine[currentLine.length - 1] !== " ") {
          currentLine += " ";
        }
        currentLine += token.raw;
        pushLine(lines, currentLine, depth);
        currentLine = "";
      } else if (token.type === "ampersand") {
        currentLine += " & ";
      } else {
        currentLine += token.raw;
      }
    }

    pushLine(lines, currentLine, depth);

    var result = lines.join("\n").replace(/\n{3,}/g, "\n\n");
    return result.trim();
  }

  function pushLine(lines, content, depth) {
    var trimmed = content.trim();
    if (!trimmed) {
      return;
    }
    var prefix = "";
    for (var d = 0; d < depth; d++) {
      prefix += INDENT;
    }
    lines.push(prefix + trimmed);
  }

  function tokenize(text) {
    var tokens = [];
    var i = 0;
    var buf = "";

    while (i < text.length) {
      var beginMatch = matchAt(text, i, /^\\begin\{([^}]*)\}/);
      if (beginMatch) {
        flushBuf(tokens, buf);
        buf = "";
        tokens.push({ type: "begin", raw: beginMatch[0], env: beginMatch[1] });
        i += beginMatch[0].length;
        continue;
      }

      var endMatch = matchAt(text, i, /^\\end\{([^}]*)\}/);
      if (endMatch) {
        flushBuf(tokens, buf);
        buf = "";
        tokens.push({ type: "end", raw: endMatch[0], env: endMatch[1] });
        i += endMatch[0].length;
        continue;
      }

      // \\ line break — may have optional [...]
      var lbMatch = matchAt(text, i, /^\\\\(\[[^\]]*\])?/);
      if (lbMatch) {
        flushBuf(tokens, buf);
        buf = "";
        tokens.push({ type: "linebreak", raw: lbMatch[0] });
        i += lbMatch[0].length;
        continue;
      }

      if (text[i] === "&") {
        flushBuf(tokens, buf);
        buf = "";
        tokens.push({ type: "ampersand", raw: "&" });
        i++;
        continue;
      }

      // Normalize whitespace to single space
      if (/\s/.test(text[i])) {
        if (buf.length > 0 && buf[buf.length - 1] !== " ") {
          buf += " ";
        }
        i++;
        continue;
      }

      buf += text[i];
      i++;
    }

    flushBuf(tokens, buf);
    return tokens;
  }

  function flushBuf(tokens, buf) {
    var trimmed = buf.trim();
    if (trimmed) {
      tokens.push({ type: "text", raw: trimmed });
    }
  }

  function matchAt(text, index, regex) {
    return regex.exec(text.slice(index));
  }

  var api = {
    formatLatex: formatLatex
  };

  if (typeof module !== "undefined" && module.exports) {
    module.exports = api;
  }
  if (globalScope) {
    globalScope.SlideTeXFormatter = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : null);