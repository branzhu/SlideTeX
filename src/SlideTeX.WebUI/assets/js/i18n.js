// SlideTeX Note: Runtime translation loading and locale lookup helpers.

(() => {
  const DEFAULT_LOCALE = "en-US";
  const SUPPORTED_LOCALES = ["zh-CN", "en-US"];
  const BUILTIN_MESSAGES = Object.freeze({
    "en-US": {
      webui: {
        status: {
          waiting: "Waiting to render"
        }
      }
    }
  });

  let activeLocale = DEFAULT_LOCALE;
  let activeMessages = BUILTIN_MESSAGES[DEFAULT_LOCALE] || {};

  function isPlainObject(value) {
    return value != null && typeof value === "object" && !Array.isArray(value);
  }

  // Deep-merges locale message trees so partial locales inherit default entries.
  function deepMerge(base, override) {
    if (!isPlainObject(base)) {
      return isPlainObject(override) ? { ...override } : override;
    }

    const output = { ...base };
    if (!isPlainObject(override)) {
      return output;
    }

    for (const key of Object.keys(override)) {
      const baseValue = output[key];
      const overrideValue = override[key];
      output[key] = isPlainObject(baseValue) && isPlainObject(overrideValue)
        ? deepMerge(baseValue, overrideValue)
        : overrideValue;
    }

    return output;
  }

  // Normalizes language tags into the locale set used by WebUI assets.
  function normalizeLocale(locale) {
    const raw = String(locale || "").trim();
    if (!raw) {
      return DEFAULT_LOCALE;
    }

    const lower = raw.toLowerCase();
    if (lower.startsWith("zh")) {
      return "zh-CN";
    }
    if (lower.startsWith("en")) {
      return "en-US";
    }

    const segments = raw.split("-");
    if (segments.length === 1) {
      return segments[0].toLowerCase();
    }

    return `${segments[0].toLowerCase()}-${segments[1].toUpperCase()}`;
  }

  // Resolves preferred locale from host context, query string, then browser defaults.
  function getRequestedLocale() {
    const fromHostContext = window.slideTexContext && window.slideTexContext.uiCulture;
    if (fromHostContext) {
      return fromHostContext;
    }

    const fromQuery = new URLSearchParams(window.location.search).get("uiCulture");
    if (fromQuery) {
      return fromQuery;
    }

    return navigator.language || DEFAULT_LOCALE;
  }

  function readInlineBundle() {
    const bundle = window.__SLIDETEX_I18N_BUNDLE__;
    if (!isPlainObject(bundle)) {
      return null;
    }

    if (!isPlainObject(bundle.locales)) {
      return null;
    }

    return bundle;
  }

  function getLocaleMessagesFromBundle(bundle, locale) {
    if (!bundle || !isPlainObject(bundle.locales)) {
      return null;
    }

    const messages = bundle.locales[locale];
    if (!isPlainObject(messages)) {
      return null;
    }

    return messages;
  }

  function getByPath(source, path) {
    const parts = String(path || "").split(".");
    let current = source;
    for (const part of parts) {
      if (!current || typeof current !== "object" || !(part in current)) {
        return null;
      }
      current = current[part];
    }

    return current;
  }

  function formatMessage(template, params) {
    if (!params) {
      return template;
    }

    return String(template).replace(/\{([^}]+)\}/g, (matched, name) => {
      if (Object.prototype.hasOwnProperty.call(params, name)) {
        return String(params[name]);
      }
      return matched;
    });
  }

  function t(key, params) {
    const template = getByPath(activeMessages, key);
    if (typeof template !== "string") {
      return key;
    }

    return formatMessage(template, params);
  }

  // Applies translated text, placeholders, and titles to DOM nodes with i18n attributes.
  function applyI18nToDom(root = document) {
    root.querySelectorAll("[data-i18n]").forEach((el) => {
      const key = el.getAttribute("data-i18n");
      if (!key) {
        return;
      }

      el.textContent = t(key);
    });

    root.querySelectorAll("[data-i18n-placeholder]").forEach((el) => {
      const key = el.getAttribute("data-i18n-placeholder");
      if (!key) {
        return;
      }

      el.setAttribute("placeholder", t(key));
    });

    root.querySelectorAll("[data-i18n-title]").forEach((el) => {
      const key = el.getAttribute("data-i18n-title");
      if (!key) {
        return;
      }

      el.setAttribute("title", t(key));
    });
  }

  // Activates locale messages with fallback merge and updates document language metadata.
  async function setLocale(locale) {
    const normalized = normalizeLocale(locale);
    const bundle = readInlineBundle();
    const bundleDefaultLocale = normalizeLocale(bundle && bundle.defaultLocale ? bundle.defaultLocale : DEFAULT_LOCALE);

    const fallback = deepMerge(
      BUILTIN_MESSAGES[bundleDefaultLocale] || BUILTIN_MESSAGES[DEFAULT_LOCALE] || {},
      getLocaleMessagesFromBundle(bundle, bundleDefaultLocale) || {}
    );
    const requested = deepMerge(
      BUILTIN_MESSAGES[normalized] || {},
      getLocaleMessagesFromBundle(bundle, normalized) || {}
    );
    const hasRequestedMessages = Object.keys(requested).length > 0;

    activeMessages = hasRequestedMessages ? deepMerge(fallback, requested) : fallback;
    activeLocale = hasRequestedMessages || SUPPORTED_LOCALES.includes(normalized)
      ? normalized
      : bundleDefaultLocale;

    document.documentElement.lang = activeLocale;
    applyI18nToDom();
    return activeLocale;
  }

  // Initializes i18n state once using host/browser preferred locale.
  async function init() {
    await setLocale(getRequestedLocale());
  }

  if (typeof window !== "undefined") {
    window.SlideTeXI18n = {
      init,
      setLocale,
      t,
      getLocale: () => activeLocale,
      applyI18nToDom,
      normalizeLocale
    };
  }

  if (typeof module !== "undefined" && module.exports) {
    module.exports = { normalizeLocale, deepMerge, formatMessage, getByPath, isPlainObject };
  }
})();
