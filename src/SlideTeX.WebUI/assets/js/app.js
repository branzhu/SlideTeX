// SlideTeX Note: Application bootstrap and event orchestration for the web editor panel.

(() => {
  const DEFAULT_OPTIONS = Object.freeze({
    fontPt: 18,
    dpi: 300,
    colorHex: "#000000",
    isTransparent: true,
    displayMode: "auto",
  });

  // localStorage persistence constants
  const STORAGE_KEY = "slidetex:user-settings";
  const STORAGE_VERSION = 1;
  const PERSISTED_KEYS = ["fontPt", "dpi", "colorHex", "isTransparent", "displayMode"];

  const elements = {
    latexInput: document.getElementById("latexInput"),
    fontPtInput: document.getElementById("fontPtInput"),
    dpiSelect: document.getElementById("dpiSelect"),
    colorInput: document.getElementById("colorInput"),
    displayModeSelect: document.getElementById("displayModeSelect"),
    transparentCheckbox: document.getElementById("transparentCheckbox"),
    previewBox: document.getElementById("previewBox"),
    previewContent: document.getElementById("previewContent"),
    errorMessage: document.getElementById("errorMessage"),
    status: document.getElementById("status"),
    insertBtn: document.getElementById("insertBtn"),
    updateBtn: document.getElementById("updateBtn"),
    renumberBtn: document.getElementById("renumberBtn")
  };
  const settingsLayout = {
    row: document.querySelector(".settings-row"),
    firstField: document.querySelector(".settings-row > label:not(.check-inline)"),
    transparentField: document.querySelector(".settings-row > .check-inline")
  };

  let debounceHandle = 0;
  let previewBoxWidth = 0; // Cache the initial width
  let editor = null;
  let suppressEditorRender = false;
  let statusState = {
    kind: "key",
    key: "webui.status.waiting",
    params: null
  };

  const host = resolveHost();
  const i18n = window.SlideTeXI18n;

  initialize().catch((error) => {
    showError(error instanceof Error ? error.message : String(error));
  });

  window.slideTex = {
    renderFromHost: async (request) => {
      await render(request?.latex, request?.options, request?.renderLatex);
    }
  };

  // Boots i18n, editor adapter, persisted settings, and first render pass.
  async function initialize() {
    wireI18nRuntimeSync();

    if (i18n?.init) {
      await i18n.init();
    }

    if (window.SlideTeXEditor && elements.latexInput) {
      editor = window.SlideTeXEditor.create(elements.latexInput, {
        commandData: window.KATEX_COMMANDS
      });
    }

    // Cache previewBox width before any content is rendered
    updatePreviewBoxWidth();

    window.addEventListener("resize", () => {
      updatePreviewBoxWidth();
      fitPreview();
      syncSettingsRowWrapState();
    });

    restoreSavedSettings();
    wireActions();
    wireSettingsRowLayoutSync();

    await render();
  }

  function updatePreviewBoxWidth() {
    // Temporarily reset content to measure true box width
    const content = elements.previewContent;
    const oldZoom = content.style.zoom;
    const oldHtml = content.innerHTML;
    content.style.zoom = "1";
    content.innerHTML = "";
    previewBoxWidth = elements.previewBox.clientWidth;
    content.innerHTML = oldHtml;
    content.style.zoom = oldZoom;
  }

  // Wires editor/input changes and host command buttons into the render workflow.
  function wireActions() {
    if (editor) {
      editor.onChange(() => {
        if (suppressEditorRender) {
          return;
        }

        clearTimeout(debounceHandle);
        debounceHandle = setTimeout(() => {
          render().catch((error) => showError(error.message));
        }, 150);
      });
    } else {
      elements.latexInput.addEventListener("input", () => {
        clearTimeout(debounceHandle);
        debounceHandle = setTimeout(() => {
          render().catch((error) => showError(error.message));
        }, 150);
      });
    }

    for (const id of [
      "fontPtInput",
      "dpiSelect",
      "colorInput",
      "displayModeSelect",
      "transparentCheckbox",
    ]) {
      elements[id].addEventListener("change", () => {
        saveSettings(getOptions());
        render().catch((error) => showError(error.message));
      });
    }

    elements.insertBtn.addEventListener("click", () => {
      if (host?.requestInsert) {
        host.requestInsert();
      }
    });

    elements.updateBtn.addEventListener("click", () => {
      if (host?.requestUpdate) {
        host.requestUpdate();
      }
    });

    elements.renumberBtn.addEventListener("click", () => {
      if (host?.requestRenumber) {
        host.requestRenumber();
      }
    });
  }

  // Tracks whether the Transparent field wrapped to a dedicated second line.
  function wireSettingsRowLayoutSync() {
    syncSettingsRowWrapState();

    if (!settingsLayout.row || typeof ResizeObserver !== "function") {
      return;
    }

    const observer = new ResizeObserver(() => {
      syncSettingsRowWrapState();
    });
    observer.observe(settingsLayout.row);
  }

  function syncSettingsRowWrapState() {
    const row = settingsLayout.row;
    const transparentField = settingsLayout.transparentField;
    if (!row || !transparentField) {
      return;
    }
    const primaryFields = Array.from(
      row.querySelectorAll("label:not(.check-inline)")
    );
    if (primaryFields.length === 0) {
      return;
    }

    const hadWrappedClass = row.classList.contains("settings-row-wrapped");
    if (hadWrappedClass) {
      // Measure natural layout without forced second-line style to avoid sticky state.
      row.classList.remove("settings-row-wrapped");
      void row.offsetWidth;
    }

    // Treat as wrapped only when transparent moved to a clearly lower row,
    // not when there is a tiny baseline offset across controls.
    const baselineTop = Math.min(...primaryFields.map((el) => el.offsetTop));
    const wrapped = transparentField.offsetTop - baselineTop > 12;
    row.classList.toggle("settings-row-wrapped", wrapped);
  }

  function stripAutoNumbering(latex) {
    return latex
      .replace(/\\begin\{(equation|align|gather)\}/g, '\\begin{$1*}')
      .replace(/\\end\{(equation|align|gather)\}/g, '\\end{$1*}');
  }

  // Runs KaTeX render, layout post-processing, PNG export, and host notification.
  async function render(latexFromHost, optionsFromHost, renderLatexOverride) {
    if (typeof latexFromHost === "string") {
      if (editor) {
        suppressEditorRender = true;
        try {
          editor.setValue(latexFromHost);
        } finally {
          suppressEditorRender = false;
        }
      } else {
        elements.latexInput.value = latexFromHost;
      }
    }

    const latex = (editor ? editor.getValue() : elements.latexInput.value).trim();
    const latexForRender = (typeof renderLatexOverride === "string"
      ? renderLatexOverride
      : latex).trim();

    if (latex.length === 0 || latexForRender.length === 0) {
      disableActions();
      showError(t("webui.error.empty_latex"), false);
      return;
    }

    const options = normalizeOptions(optionsFromHost ?? getOptions());
    if (optionsFromHost) {
      applyOptionsToInputs(options);
    }

    const effectiveDisplayMode = resolveDisplayMode(latexForRender, options.displayMode);

    elements.previewContent.style.fontSize = `${options.fontPt}pt`;
    elements.previewContent.style.color = options.colorHex;
    elements.previewContent.style.background = options.isTransparent ? "transparent" : "#ffffff";
    elements.previewContent.style.display = "inline-block";
    elements.previewContent.style.whiteSpace = effectiveDisplayMode === "inline" ? "nowrap" : "normal";
    elements.previewContent.style.alignItems = "";
    elements.previewContent.style.columnGap = "";

    try {
      if (!window.katex) {
        throw new Error(t("webui.error.katex_missing"));
      }

      const katexWarnings = [];
      window.katex.render(stripAutoNumbering(latexForRender), elements.previewContent, {
        displayMode: effectiveDisplayMode === "display",
        throwOnError: false,
        strict: (errorCode, errorMessage, token) => {
          const warning = formatKatexWarning(errorCode, errorMessage, token);
          if (warning) {
            katexWarnings.push(warning);
          }
          return "warn";
        },
        trust: false
      });

      const severeError = extractKatexSevereError(elements.previewContent);
      if (severeError) {
        disableActions();
        showError(severeError);
        return;
      }

      reserveTagSpace(elements.previewContent);
      fitPreview();

      const payload = await exportPreviewAsPng(options);
      const result = {
        isSuccess: true,
        errorMessage: null,
        pngBase64: payload.base64,
        pixelWidth: payload.width,
        pixelHeight: payload.height,
        latex,
        options
      };

      hideError();
      enableActions();
      const warningSuffix = katexWarnings.length > 0
        ? t("webui.status.warning_suffix", { count: katexWarnings.length })
        : "";
      setStatusByKey(
        "webui.status.render_success",
        {
          warningSuffix,
          width: payload.width,
          height: payload.height,
          dpi: options.dpi,
          mode: effectiveDisplayMode
        });
      notifyRenderSuccess(result);
    } catch (error) {
      disableActions();
      const message = error instanceof Error ? error.message : String(error);
      showError(message);
    }
  }

  // Scales preview content down to fit container width while preserving readability.
  function fitPreview() {
    const content = elements.previewContent;

    // Reset zoom to measure natural content width
    content.style.zoom = "1";

    requestAnimationFrame(() => {
      if (previewBoxWidth <= 0 || !content.firstChild) return;

      // Measure the inline-block container's actual width, which reliably
      // reflects content size regardless of whether the child is inline
      // (.katex in inline mode) or block (.katex-display).
      const contentWidth = content.scrollWidth;
      const availableWidth = previewBoxWidth - 20; // subtract box padding

      if (contentWidth > availableWidth && contentWidth > 0) {
        const scale = Math.max(0.3, availableWidth / contentWidth);
        content.style.zoom = String(scale);
      }
    });
  }

  // Captures rendered formula DOM into PNG and returns base64 plus pixel dimensions.
  async function exportPreviewAsPng(options) {
    const scale = Math.max(1, options.dpi / 96);
    const contentEl = elements.previewContent;

    // Create a temporary wrapper for precise capture
    const wrapper = document.createElement("div");
    wrapper.style.cssText = `
      position: absolute;
      left: -9999px;
      top: 0;
      display: inline-block;
      padding: 2px;
      background: ${options.isTransparent ? "transparent" : "#ffffff"};
      font-size: ${options.fontPt}pt;
      color: ${options.colorHex};
    `;

    // Clone the content
    const clone = contentEl.cloneNode(true);

    // Remove display mode margins from KaTeX
    const katexDisplay = clone.querySelector(".katex-display");
    if (katexDisplay) {
      katexDisplay.style.margin = "0";
      katexDisplay.style.padding = "0";
    }

    wrapper.appendChild(clone);
    reserveTagSpace(wrapper);

    document.body.appendChild(wrapper);

    try {
      // Use html2canvas for reliable HTML to canvas rendering
      const canvas = await html2canvas(wrapper, {
        scale: scale,
        backgroundColor: options.isTransparent ? null : "#ffffff",
        logging: false,
        useCORS: true,
        allowTaint: true
      });

      const dataUrl = canvas.toDataURL("image/png");
      return {
        base64: dataUrl.replace(/^data:image\/png;base64,/, ""),
        width: canvas.width,
        height: canvas.height
      };
    } finally {
      document.body.removeChild(wrapper);
    }
  }

  // Reflows KaTeX tag nodes to avoid overlap with formula body during capture.
  function reserveTagSpace(root) {
    if (!root) {
      return;
    }

    const displays = root.querySelectorAll(".katex-display");
    for (const display of displays) {
      const tags = display.querySelectorAll(".tag");
      for (const tag of tags) {
        // Keep KaTeX's tag DOM, but switch to normal flow to avoid absolute-position overlap.
        tag.style.position = "static";
        tag.style.display = "inline-block";
        tag.style.marginLeft = "0.8em";
        tag.style.verticalAlign = "top";
        tag.style.whiteSpace = "nowrap";
      }
    }
  }

  function getOptions() {
    return normalizeOptions({
      fontPt: Number(elements.fontPtInput.value || "24"),
      dpi: Number(elements.dpiSelect.value || "300"),
      colorHex: elements.colorInput.value || "#000000",
      isTransparent: Boolean(elements.transparentCheckbox.checked),
      displayMode: elements.displayModeSelect.value || "auto"
    });
  }

  function normalizeOptions(raw) {
    const source = raw ?? {};

    return {
      fontPt: Number(source.fontPt ?? source.FontPt ?? DEFAULT_OPTIONS.fontPt),
      dpi: Number(source.dpi ?? source.Dpi ?? DEFAULT_OPTIONS.dpi),
      colorHex: String(source.colorHex ?? source.ColorHex ?? DEFAULT_OPTIONS.colorHex),
      isTransparent: toBoolean(source.isTransparent ?? source.IsTransparent ?? DEFAULT_OPTIONS.isTransparent),
      displayMode: normalizeDisplayMode(source.displayMode ?? source.DisplayMode ?? DEFAULT_OPTIONS.displayMode),
    };
  }

  function applyOptionsToInputs(options) {
    elements.fontPtInput.value = String(options.fontPt);
    elements.dpiSelect.value = String(options.dpi);
    elements.colorInput.value = options.colorHex;
    elements.displayModeSelect.value = options.displayMode;
    elements.transparentCheckbox.checked = Boolean(options.isTransparent);
  }

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
    const normalized = String(value ?? "auto").trim().toLowerCase();
    if (normalized === "inline" || normalized === "display" || normalized === "auto") {
      return normalized;
    }

    return "auto";
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

  // Heuristic for selecting display mode when user picks "auto".
  function shouldUseDisplayMode(latex) {
    if (typeof latex !== "string") {
      return false;
    }

    const displayOnlyEnvironment = /\\begin\{(?:align\*?|aligned|gather\*?|equation\*?|split|cases|matrix|pmatrix|bmatrix|Bmatrix|vmatrix|Vmatrix)\}/;
    if (displayOnlyEnvironment.test(latex)) {
      return true;
    }

    // Multi-line formulas usually require block rendering.
    return /\\\\/.test(latex);
  }

  function notifyRenderSuccess(result) {
    if (host?.notifyRenderSuccess) {
      host.notifyRenderSuccess(JSON.stringify(result));
    }
  }

  function notifyRenderError(message) {
    if (host?.notifyRenderError) {
      host.notifyRenderError(message);
    }
  }

  function setStatus(message) {
    statusState = {
      kind: "raw",
      message: String(message ?? "")
    };
    elements.status.textContent = message;
  }

  function setStatusByKey(key, params = null) {
    statusState = {
      kind: "key",
      key,
      params
    };
    elements.status.textContent = t(key, params);
  }

  function refreshLocalizedRuntimeText() {
    if (!statusState || !elements.status) {
      return;
    }

    if (statusState.kind === "key") {
      elements.status.textContent = t(statusState.key, statusState.params || undefined);
      return;
    }

    elements.status.textContent = statusState.message;
  }

  // Keeps runtime status text synced after locale switches at runtime.
  function wireI18nRuntimeSync() {
    if (!i18n || typeof i18n.setLocale !== "function" || i18n.__slideTexPatchedSetLocale) {
      return;
    }

    const originalSetLocale = i18n.setLocale.bind(i18n);
    i18n.setLocale = async (locale) => {
      const resolved = await originalSetLocale(locale);
      refreshLocalizedRuntimeText();
      syncSettingsRowWrapState();
      return resolved;
    };
    i18n.__slideTexPatchedSetLocale = true;
  }

  function t(key, params) {
    if (i18n?.t) {
      return i18n.t(key, params);
    }

    return key;
  }

  function formatKatexWarning(errorCode, errorMessage, token) {
    const code = String(errorCode ?? "").trim();
    const message = String(errorMessage ?? "").trim();
    const tokenText = token && typeof token.text === "string" ? token.text.trim() : "";

    const parts = [];
    if (code.length > 0) {
      parts.push(code);
    }
    if (message.length > 0) {
      parts.push(message);
    }
    if (tokenText.length > 0) {
      parts.push(`token=${tokenText}`);
    }

    return parts.join(" | ");
  }

  function extractKatexSevereError(container) {
    if (!container) {
      return "";
    }

    const errorElement = container.querySelector(".katex-error");
    if (!errorElement) {
      return "";
    }

    const rawMessage = sanitizeKatexErrorMessage(
      errorElement.getAttribute("title") || errorElement.textContent || "");
    if (!rawMessage) {
      return t("webui.error.katex_render_failed");
    }

    return t("webui.error.katex_render_failed_with_detail", { detail: rawMessage });
  }

  function sanitizeKatexErrorMessage(raw) {
    return String(raw ?? "").replace(/\s+/g, " ").trim();
  }

  function showError(message, notify = true) {
    elements.errorMessage.textContent = message;
    elements.errorMessage.classList.remove("hidden");
    setStatusByKey("webui.status.render_failed");
    if (notify) {
      notifyRenderError(message);
    }
  }

  function hideError() {
    elements.errorMessage.classList.add("hidden");
    elements.errorMessage.textContent = "";
  }

  function disableActions() {
    elements.insertBtn.disabled = true;
    elements.updateBtn.disabled = true;
  }

  function enableActions() {
    elements.insertBtn.disabled = false;
    elements.updateBtn.disabled = false;
  }

  // Resolves COM host bridge object for both WebView2 and debug-host scenarios.
  function resolveHost() {
    if (window.chrome?.webview?.hostObjects?.sync?.slidetexHost) {
      return window.chrome.webview.hostObjects.sync.slidetexHost;
    }

    if (window.slidetexHost) {
      return window.slidetexHost;
    }

    return null;
  }

  // --- localStorage persistence ---

  // Loads persisted editor/render options from localStorage.
  function loadSavedSettings() {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) {
        return null;
      }

      const data = JSON.parse(raw);
      if (data?.version !== STORAGE_VERSION || !data?.settings) {
        return null;
      }

      return data.settings;
    } catch {
      // Fail silently on parse errors
      return null;
    }
  }

  // Persists selected option keys to localStorage with schema versioning.
  function saveSettings(options) {
    try {
      const settings = {};
      for (const key of PERSISTED_KEYS) {
        if (key in options) {
          settings[key] = options[key];
        }
      }

      const data = {
        version: STORAGE_VERSION,
        settings
      };

      localStorage.setItem(STORAGE_KEY, JSON.stringify(data));
    } catch {
      // Fail silently on quota exceeded or private mode
    }
  }

  // Applies previously persisted settings to current input controls.
  function restoreSavedSettings() {
    const saved = loadSavedSettings();
    if (!saved) {
      return;
    }

    const merged = normalizeOptions({ ...DEFAULT_OPTIONS, ...saved });
    applyOptionsToInputs(merged);
  }
})();
