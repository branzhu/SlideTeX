// SlideTeX Note: CodeMirror adapter and editor behavior customizations for LaTeX input.

import { EditorState, Prec } from "@codemirror/state";
import {
  EditorView,
  keymap,
  drawSelection,
  highlightActiveLine
} from "@codemirror/view";
import {
  history,
  historyKeymap,
  defaultKeymap,
  indentWithTab
} from "@codemirror/commands";
import {
  bracketMatching,
  syntaxHighlighting,
  defaultHighlightStyle,
  indentOnInput,
  StreamLanguage
} from "@codemirror/language";
import {
  autocompletion,
  completionKeymap,
  completionStatus,
  acceptCompletion,
  closeCompletion
} from "@codemirror/autocomplete";
import { searchKeymap } from "@codemirror/search";
import { stex } from "@codemirror/legacy-modes/mode/stex";

(function () {
  "use strict";

  var PLACEHOLDER_CHAR = "⬚";

  // Resolves a desc value through i18n if available, falling back to raw string.
  function resolveDesc(desc) {
    var raw = desc || "";
    if (typeof window !== "undefined" && window.SlideTeXI18n && typeof window.SlideTeXI18n.t === "function") {
      return window.SlideTeXI18n.t(raw);
    }
    return raw;
  }

  // Builds completion provider for both LaTeX commands and begin-environment names.
  function buildCommandCompleter(commands, environments) {
    var commandOptions = (commands || []).map(function (item) {
      var cmd = String(item.cmd || "");
      var snippetText = String(item.snippet || cmd);
      var applySnippet = buildSnippetApply(snippetText, cmd);
      return {
        label: cmd,
        detail: resolveDesc(item.desc),
        type: "keyword",
        boost: 1000 - Number(item.priority || 0),
        apply: applySnippet
      };
    });

    var envOptions = (environments || []).map(function (item) {
      return {
        label: String(item.name || ""),
        detail: resolveDesc(item.desc),
        type: "type"
      };
    });

    return function (context) {
      var pos = context.pos;
      var before = context.state.sliceDoc(0, pos);

      var envMatch = /\\begin\{([a-zA-Z*]*)$/.exec(before);
      if (envMatch) {
        var envPrefix = envMatch[1];
        var envFrom = pos - envPrefix.length;
        var envLower = envPrefix.toLowerCase();
        var filteredEnv = envOptions.filter(function (opt) {
          return opt.label.toLowerCase().indexOf(envLower) === 0;
        }).slice(0, 20);

        if (filteredEnv.length === 0) {
          return null;
        }

        return {
          from: envFrom,
          options: filteredEnv,
          validFor: /^[a-zA-Z*]*$/
        };
      }

      var cmdMatch = /\\[a-zA-Z]*$/.exec(before);
      if (!cmdMatch) {
        return null;
      }

      var prefix = cmdMatch[0];
      var from = pos - prefix.length;
      var lowerPrefix = prefix.toLowerCase();
      var filteredCmd = commandOptions.filter(function (opt) {
        return opt.label.toLowerCase().indexOf(lowerPrefix) === 0;
      }).slice(0, 30);

      if (filteredCmd.length === 0) {
        return null;
      }

      return {
        from: from,
        options: filteredCmd,
        validFor: /^\\[a-zA-Z]*$/
      };
    };
  }

  // Creates snippet-apply callback and selects first placeholder after insertion.
  function buildSnippetApply(rawSnippet, fallbackText) {
    var template = toPlaceholderTemplate(rawSnippet, fallbackText);
    return function (view, completion, from, to) {
      var insertText = template.text;
      var firstOffset = template.firstPlaceholderOffset;
      var selection;

      if (firstOffset >= 0) {
        selection = {
          anchor: from + firstOffset,
          head: from + firstOffset + PLACEHOLDER_CHAR.length
        };
      } else {
        var end = from + insertText.length;
        selection = { anchor: end, head: end };
      }

      view.dispatch({
        changes: { from: from, to: to, insert: insertText },
        selection: selection
      });
    };
  }

  function toPlaceholderTemplate(snippetText, fallbackText) {
    var text = String(snippetText || fallbackText || "");
    text = text.replace(/\$(\d+)/g, PLACEHOLDER_CHAR);
    return {
      text: text,
      firstPlaceholderOffset: text.indexOf(PLACEHOLDER_CHAR)
    };
  }

  function selectNextPlaceholder(view) {
    var selection = view.state.selection.main;
    var from = selection.to;
    var text = view.state.doc.toString();
    var idx = text.indexOf(PLACEHOLDER_CHAR, from);
    if (idx < 0) {
      return false;
    }

    view.dispatch({
      selection: { anchor: idx, head: idx + PLACEHOLDER_CHAR.length }
    });
    return true;
  }

  function selectPrevPlaceholder(view) {
    var selection = view.state.selection.main;
    var from = selection.from - 1;
    if (from < 0) {
      return false;
    }

    var text = view.state.doc.toString();
    var idx = text.lastIndexOf(PLACEHOLDER_CHAR, from);
    if (idx < 0) {
      return false;
    }

    view.dispatch({
      selection: { anchor: idx, head: idx + PLACEHOLDER_CHAR.length }
    });
    return true;
  }

  // Collects begin/end tokens to support environment-name synchronization.
  function collectBeginEndTokens(text) {
    var tokens = [];
    var re = /\\(begin|end)\{([^}]*)\}/g;
    var match;
    while ((match = re.exec(text)) !== null) {
      var kind = match[1];
      var prefixLen = kind === "begin" ? 7 : 5;
      var argStart = match.index + prefixLen;
      var argEnd = argStart + match[2].length;
      tokens.push({
        kind: kind,
        index: match.index,
        argStart: argStart,
        argEnd: argEnd
      });
    }

    return tokens;
  }

  function findBeginTokenAtCaret(text, caret) {
    var tokens = collectBeginEndTokens(text);
    for (var i = tokens.length - 1; i >= 0; i--) {
      var token = tokens[i];
      if (token.kind !== "begin") {
        continue;
      }

      if (caret >= token.argStart && caret <= token.argEnd) {
        return token;
      }
    }

    return null;
  }

  function findMatchingEndToken(tokens, beginStart) {
    var beginIdx = -1;
    for (var i = 0; i < tokens.length; i++) {
      if (tokens[i].kind === "begin" && tokens[i].index === beginStart) {
        beginIdx = i;
        break;
      }
    }

    if (beginIdx < 0) {
      return null;
    }

    var depth = 0;
    for (var j = beginIdx + 1; j < tokens.length; j++) {
      var token = tokens[j];
      if (token.kind === "begin") {
        depth++;
        continue;
      }

      if (depth === 0) {
        return token;
      }

      depth--;
    }

    return null;
  }

  // Keeps \begin{...} and matching \end{...} environment names synchronized while typing.
  function createBeginEndSyncExtension() {
    var syncing = false;

    return EditorView.updateListener.of(function (update) {
      if (syncing) {
        return;
      }

      if (!update.docChanged && !update.selectionSet) {
        return;
      }

      var selection = update.state.selection.main;
      if (selection.from !== selection.to) {
        return;
      }

      var text = update.state.doc.toString();
      var beginToken = findBeginTokenAtCaret(text, selection.from);
      if (!beginToken) {
        return;
      }

      var envName = text.slice(beginToken.argStart, beginToken.argEnd);
      if (!/^[a-zA-Z*]+$/.test(envName)) {
        return;
      }

      var tokens = collectBeginEndTokens(text);
      var endToken = findMatchingEndToken(tokens, beginToken.index);
      if (!endToken) {
        return;
      }

      var current = text.slice(endToken.argStart, endToken.argEnd);
      if (current === envName) {
        return;
      }

      syncing = true;
      try {
        update.view.dispatch({
          changes: {
            from: endToken.argStart,
            to: endToken.argEnd,
            insert: envName
          }
        });
      } finally {
        syncing = false;
      }
    });
  }

  // Creates CodeMirror instance and exposes textarea-compatible adapter methods.
  function create(textarea, options) {
    if (!textarea) {
      throw new Error("textarea is required");
    }

    options = options || {};
    var data = options.commandData || window.LATEX_COMMANDS || {};
    var commands = data.commands || [];
    var environments = data.environments || [];

    var container = document.createElement("div");
    container.className = "stex-cm-container";
    textarea.parentNode.insertBefore(container, textarea);

    var listeners = {
      change: [],
      selectionChange: [],
      keydown: []
    };

    var dispatchingMirrorInput = false;

    var completionSource = buildCommandCompleter(commands, environments);

    var state = EditorState.create({
      doc: textarea.value || "",
      extensions: [
        drawSelection(),
        history(),
        indentOnInput(),
        bracketMatching(),
        highlightActiveLine(),
        syntaxHighlighting(defaultHighlightStyle, { fallback: true }),
        StreamLanguage.define(stex),
        EditorView.lineWrapping,
        EditorView.theme({
          "&": {
            fontFamily: '"Cascadia Code", "Consolas", "Segoe UI", monospace',
            fontSize: "14px",
            border: "1px solid var(--line)",
            borderRadius: "8px",
            backgroundColor: "#fff"
          },
          ".cm-scroller": {
            minHeight: "130px"
          },
          ".cm-content": {
            padding: "8px"
          },
          ".cm-activeLine": {
            backgroundColor: "rgba(59,130,246,0.06)"
          },
          ".cm-matchingBracket": {
            backgroundColor: "rgba(245,158,11,0.2)",
            outline: "1px solid rgba(245,158,11,0.55)",
            fontWeight: "700"
          },
          ".cm-nonmatchingBracket": {
            backgroundColor: "rgba(239,68,68,0.2)",
            outline: "1px solid rgba(239,68,68,0.55)",
            fontWeight: "700"
          }
        }),
        autocompletion({
          override: [completionSource],
          activateOnTyping: true,
          maxRenderedOptions: 12,
          defaultKeymap: false
        }),
        Prec.highest(keymap.of([
          {
            key: "Tab",
            run: function (view) {
              var status = completionStatus(view.state);
              // Keep completion higher priority than placeholder jumps.
              if (status === "active" || status === "pending") {
                if (acceptCompletion(view)) {
                  return true;
                }

                return true;
              }

              if (selectNextPlaceholder(view)) {
                return true;
              }

              return false;
            }
          },
          {
            key: "Shift-Tab",
            run: function (view) {
              if (selectPrevPlaceholder(view)) {
                return true;
              }

              return false;
            }
          },
          {
            key: "Escape",
            run: function (view) {
              if (closeCompletion(view)) {
                return true;
              }

              return false;
            }
          },
          indentWithTab,
          ...completionKeymap,
          ...historyKeymap,
          ...defaultKeymap,
          ...searchKeymap
        ])),
        createBeginEndSyncExtension(),
        EditorView.domEventHandlers({
          keydown: function (_event, view) {
            for (var i = 0; i < listeners.keydown.length; i++) {
              listeners.keydown[i](view);
            }
            return false;
          }
        }),
        EditorView.updateListener.of(function (update) {
          if (update.docChanged) {
            textarea.value = update.state.doc.toString();
            dispatchingMirrorInput = true;
            try {
              textarea.dispatchEvent(new Event("input", { bubbles: true }));
            } finally {
              dispatchingMirrorInput = false;
            }

            for (var i = 0; i < listeners.change.length; i++) {
              listeners.change[i](textarea.value);
            }
          }

          if (update.selectionSet) {
            for (var j = 0; j < listeners.selectionChange.length; j++) {
              listeners.selectionChange[j](update.state.selection.main);
            }
          }
        })
      ]
    });

    var view = new EditorView({
      state: state,
      parent: container
    });

    textarea.style.display = "none";

    var adapter = {
      getValue: function () {
        return view.state.doc.toString();
      },
      setValue: function (value) {
        var text = String(value == null ? "" : value);
        view.dispatch({
          changes: { from: 0, to: view.state.doc.length, insert: text },
          selection: { anchor: text.length }
        });
      },
      getSelection: function () {
        var sel = view.state.selection.main;
        return { from: sel.from, to: sel.to };
      },
      setSelection: function (from, to) {
        var anchor = Number(from || 0);
        var head = Number(to == null ? anchor : to);
        view.dispatch({ selection: { anchor: anchor, head: head } });
      },
      focus: function () {
        view.focus();
      },
      onChange: function (cb) {
        listeners.change.push(cb);
      },
      onSelectionChange: function (cb) {
        listeners.selectionChange.push(cb);
      },
      onKeydown: function (cb) {
        listeners.keydown.push(cb);
      },
      isMirroringInput: function () {
        return dispatchingMirrorInput;
      },
      destroy: function () {
        view.destroy();
        container.remove();
        textarea.style.display = "";
      }
    };

    return adapter;
  }

  window.SlideTeXEditor = {
    create: create
  };
})();


