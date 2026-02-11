#!/usr/bin/env node
// SlideTeX Note: Builds i18n bundle artifacts consumed by the WebUI runtime.

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, "..");

const defaultLocale = "en-US";
const i18nDir = path.join(repoRoot, "src", "SlideTeX.WebUI", "assets", "i18n");
const indexHtmlPath = path.join(repoRoot, "src", "SlideTeX.WebUI", "index.html");

const markerStart = "<!-- SLIDETEX_I18N_BUNDLE_START -->";
const markerEnd = "<!-- SLIDETEX_I18N_BUNDLE_END -->";

// Returns true when value is a plain object and not an array.
function isPlainObject(value) {
  return value != null && typeof value === "object" && !Array.isArray(value);
}

// Flattens nested translation keys to dot-path form for consistency checks.
function flattenKeys(source, prefix = "", output = new Set()) {
  if (!isPlainObject(source)) {
    return output;
  }

  for (const [key, value] of Object.entries(source)) {
    const pathKey = prefix ? `${prefix}.${key}` : key;
    if (isPlainObject(value)) {
      flattenKeys(value, pathKey, output);
    } else {
      output.add(pathKey);
    }
  }

  return output;
}

// Reads all locale JSON files and validates basic payload shape.
function readLocales() {
  if (!fs.existsSync(i18nDir)) {
    throw new Error(`i18n 目录不存在: ${i18nDir}`);
  }

  const files = fs.readdirSync(i18nDir)
    .filter((name) => name.toLowerCase().endsWith(".json"))
    .sort((a, b) => a.localeCompare(b));

  if (files.length === 0) {
    throw new Error(`未找到语言文件: ${i18nDir}`);
  }

  const locales = {};
  for (const file of files) {
    const locale = path.basename(file, ".json");
    const fullPath = path.join(i18nDir, file);
    const raw = fs.readFileSync(fullPath, "utf8");

    let parsed;
    try {
      parsed = JSON.parse(raw);
    } catch (error) {
      throw new Error(`JSON 解析失败: ${fullPath}\n${error.message}`);
    }

    if (!isPlainObject(parsed)) {
      throw new Error(`语言文件必须为对象: ${fullPath}`);
    }

    locales[locale] = parsed;
  }

  if (!(defaultLocale in locales)) {
    throw new Error(`缺少默认语言文件: ${defaultLocale}.json`);
  }

  return locales;
}

// Ensures every locale uses the same key set as the default locale.
function validateLocaleKeyConsistency(locales) {
  const defaultKeys = flattenKeys(locales[defaultLocale]);
  for (const [locale, payload] of Object.entries(locales)) {
    if (locale === defaultLocale) {
      continue;
    }

    const localeKeys = flattenKeys(payload);
    const missing = [...defaultKeys].filter((key) => !localeKeys.has(key));
    const extra = [...localeKeys].filter((key) => !defaultKeys.has(key));

    if (missing.length > 0 || extra.length > 0) {
      const lines = [];
      if (missing.length > 0) {
        lines.push(`missing(${locale}): ${missing.join(", ")}`);
      }
      if (extra.length > 0) {
        lines.push(`extra(${locale}): ${extra.join(", ")}`);
      }
      throw new Error(`语言 key 不一致:\n${lines.join("\n")}`);
    }
  }
}

// Rewrites the inline i18n bundle block inside index.html markers.
function updateIndexHtml(bundle) {
  if (!fs.existsSync(indexHtmlPath)) {
    throw new Error(`index.html 不存在: ${indexHtmlPath}`);
  }

  const indexRaw = fs.readFileSync(indexHtmlPath, "utf8");
  const startIdx = indexRaw.indexOf(markerStart);
  const endIdx = indexRaw.indexOf(markerEnd);

  if (startIdx < 0 || endIdx < 0 || endIdx < startIdx) {
    throw new Error("index.html 缺少 i18n bundle 标记块。");
  }

  const before = indexRaw.slice(0, startIdx + markerStart.length);
  const after = indexRaw.slice(endIdx);
  const bundleJson = JSON.stringify(bundle, null, 2);

  const block = [
    "",
    "<script id=\"slidetex-i18n-bundle\">",
    "window.__SLIDETEX_I18N_BUNDLE__ = " + bundleJson + ";",
    "</script>",
    ""
  ].join("\n");

  const updated = before + block + after;
  fs.writeFileSync(indexHtmlPath, updated, "utf8");
}

// Entry point for locale read/validate/build/update workflow.
function main() {
  const locales = readLocales();
  validateLocaleKeyConsistency(locales);

  const bundle = {
    version: 1,
    defaultLocale,
    locales
  };

  updateIndexHtml(bundle);
  console.log(`Generated inline i18n bundle into ${path.relative(repoRoot, indexHtmlPath)} (${Object.keys(locales).join(", ")})`);
}

main();


