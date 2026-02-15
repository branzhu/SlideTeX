#!/usr/bin/env node
// SlideTeX Note: Renders known-good fixtures and compares outputs for regressions.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import http from 'node:http';
import { fileURLToPath } from 'node:url';
import pixelmatch from 'pixelmatch';
import { PNG } from 'pngjs';
import puppeteer from 'puppeteer-core';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');
const webRoot = path.join(repoRoot, 'src', 'SlideTeX.WebUI');

// Parses CLI switches and normalizes defaults for regression test execution.
function parseArgs(argv) {
  const args = {
    mode: 'verify',
    fixture: path.join(repoRoot, 'tests', 'render-regression', 'render-visual-mathjax-v1.json'),
    artifactsDir: path.join(repoRoot, 'artifacts', 'render-regression'),
    suite: 'all',
    caseIds: [],
    chromePath: '',
    headless: true
  };

  for (let i = 0; i < argv.length; i++) {
    const token = argv[i];
    if (token === '--mode') {
      args.mode = String(argv[++i] || '').trim();
      continue;
    }
    if (token === '--fixture') {
      args.fixture = resolvePath(argv[++i]);
      continue;
    }
    if (token === '--artifactsDir') {
      args.artifactsDir = resolvePath(argv[++i]);
      continue;
    }
    if (token === '--suite') {
      args.suite = String(argv[++i] || '').trim().toLowerCase();
      continue;
    }
    if (token === '--caseId') {
      const raw = String(argv[++i] || '').trim();
      if (raw.length > 0) {
        args.caseIds = raw.split(',').map((v) => v.trim()).filter(Boolean);
      }
      continue;
    }
    if (token === '--chromePath') {
      args.chromePath = resolvePath(argv[++i]);
      continue;
    }
    if (token === '--headful') {
      args.headless = false;
      continue;
    }
    throw new Error(`Unknown argument: ${token}`);
  }

  if (args.mode !== 'verify' && args.mode !== 'update-baseline') {
    throw new Error(`Unsupported mode: ${args.mode}`);
  }
  if (args.suite !== 'all' && args.suite !== 'smoke' && args.suite !== 'full') {
    throw new Error(`Unsupported suite: ${args.suite}`);
  }

  return args;
}

function resolvePath(input) {
  if (!input) {
    return '';
  }
  return path.isAbsolute(input) ? input : path.resolve(repoRoot, input);
}

function ensureDir(dir) {
  fs.mkdirSync(dir, { recursive: true });
}

function ensureEmptyDir(dir) {
  fs.rmSync(dir, { recursive: true, force: true });
  fs.mkdirSync(dir, { recursive: true });
}

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

function writeJson(filePath, data) {
  fs.writeFileSync(filePath, JSON.stringify(data, null, 2) + '\n', 'utf8');
}

function sequenceEquals(left, right) {
  if (left.length !== right.length) {
    return false;
  }
  for (let i = 0; i < left.length; i++) {
    if (String(left[i]) !== String(right[i])) {
      return false;
    }
  }
  return true;
}

// Resolves Chrome binary path from CLI, env var, and common local install locations.
function resolveChromePath(cliPath) {
  if (cliPath && fs.existsSync(cliPath)) {
    return cliPath;
  }

  if (process.env.CHROME_PATH && fs.existsSync(process.env.CHROME_PATH)) {
    return process.env.CHROME_PATH;
  }

  const home = os.homedir();
  const candidates = [
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
    path.join(home, 'AppData', 'Local', 'Google', 'Chrome', 'Application', 'chrome.exe')
  ];

  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) {
      return candidate;
    }
  }

  return '';
}

function mimeByExtension(filePath) {
  const ext = path.extname(filePath).toLowerCase();
  switch (ext) {
    case '.html':
      return 'text/html; charset=utf-8';
    case '.js':
      return 'application/javascript; charset=utf-8';
    case '.css':
      return 'text/css; charset=utf-8';
    case '.json':
      return 'application/json; charset=utf-8';
    case '.png':
      return 'image/png';
    case '.svg':
      return 'image/svg+xml';
    case '.woff2':
      return 'font/woff2';
    case '.woff':
      return 'font/woff';
    case '.ttf':
      return 'font/ttf';
    case '.map':
      return 'application/json; charset=utf-8';
    default:
      return 'application/octet-stream';
  }
}

// Starts a local static file server for loading WebUI assets in headless browser runs.
function startStaticServer(rootDir) {
  const server = http.createServer((req, res) => {
    const urlPath = decodeURIComponent((req.url || '/').split('?')[0]);
    const relPath = urlPath === '/' ? '/index.html' : urlPath;
    const candidatePath = path.normalize(path.join(rootDir, relPath));

    if (!candidatePath.startsWith(rootDir)) {
      res.statusCode = 403;
      res.end('Forbidden');
      return;
    }

    if (!fs.existsSync(candidatePath) || fs.statSync(candidatePath).isDirectory()) {
      res.statusCode = 404;
      res.end('Not Found');
      return;
    }

    res.statusCode = 200;
    res.setHeader('Content-Type', mimeByExtension(candidatePath));
    fs.createReadStream(candidatePath).pipe(res);
  });

  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const address = server.address();
      if (!address || typeof address === 'string') {
        reject(new Error('Failed to resolve static server address.'));
        return;
      }
      resolve({
        server,
        baseUrl: `http://127.0.0.1:${address.port}/index.html`
      });
    });
  });
}

// Merges case definition with fixture defaults and normalizes expected fields.
function normalizeCase(definition, defaults) {
  const options = {
    ...(defaults.options || {}),
    ...(definition.options || {})
  };
  const imageDiff = {
    ...(defaults.imageDiff || {}),
    ...(definition.imageDiff || {})
  };
  const layout = {
    ...(defaults.layout || {}),
    ...(definition.layout || {})
  };
  const expected = {
    tags: [],
    ...(definition.expected || {})
  };

  return {
    ...definition,
    locale: definition.locale || defaults.locale || 'zh-CN',
    fontChecks: definition.fontChecks || defaults.fontChecks || [],
    options,
    imageDiff,
    layout,
    expected
  };
}

function normalizeSuiteValue(value) {
  if (Array.isArray(value)) {
    return value.map((v) => String(v || '').trim().toLowerCase()).filter(Boolean);
  }

  const normalized = String(value || '').trim().toLowerCase();
  if (!normalized) {
    return ['full'];
  }

  return [normalized];
}

function assertCaseIdUniqueness(allCases) {
  const map = new Map();
  for (const c of allCases) {
    const rawId = String(c.id || '').trim();
    if (!rawId) {
      throw new Error('Fixture contains case with empty id.');
    }

    const key = rawId.toLowerCase();
    const existed = map.get(key);
    if (existed && existed !== rawId) {
      throw new Error(
        `Case id collision on case-insensitive filesystem: "${existed}" vs "${rawId}". Please use distinct ids beyond letter case.`
      );
    }
    map.set(key, rawId);
  }
}

function pickCaseList(fixture, selectedIds, suite) {
  const defaults = fixture.defaults || {};
  const allCases = (fixture.cases || []).map((c) => normalizeCase(c, defaults));
  assertCaseIdUniqueness(allCases);
  const suiteFiltered = suite === 'all'
    ? allCases
    : allCases.filter((c) => normalizeSuiteValue(c.suite).includes(suite));

  if (!selectedIds || selectedIds.length === 0) {
    return suiteFiltered;
  }

  const selected = suiteFiltered.filter((c) => selectedIds.includes(c.id));
  const missing = selectedIds.filter((id) => !selected.some((c) => c.id === id));
  if (missing.length > 0) {
    throw new Error(`Case id not found in fixture: ${missing.join(', ')}`);
  }

  return selected;
}

function loadPng(filePath) {
  return PNG.sync.read(fs.readFileSync(filePath));
}

// Compares PNG outputs and writes diff image only when thresholds are exceeded.
function compareImages(expectedPath, actualPath, diffPath, threshold) {
  const expected = loadPng(expectedPath);
  const actual = loadPng(actualPath);

  if (expected.width !== actual.width || expected.height !== actual.height) {
    return {
      ok: false,
      reason: `Image size mismatch: expected=${expected.width}x${expected.height}, actual=${actual.width}x${actual.height}`,
      diffPixels: Number.NaN,
      diffRatio: Number.NaN
    };
  }

  const diff = new PNG({ width: expected.width, height: expected.height });
  const diffPixels = pixelmatch(
    expected.data,
    actual.data,
    diff.data,
    expected.width,
    expected.height,
    {
      threshold: 0.1,
      includeAA: false
    }
  );
  const totalPixels = expected.width * expected.height;
  const diffRatio = totalPixels > 0 ? diffPixels / totalPixels : 0;
  const ok = diffPixels <= threshold.maxDiffPixels && diffRatio <= threshold.maxDiffRatio;

  if (!ok) {
    fs.writeFileSync(diffPath, PNG.sync.write(diff));
  } else if (fs.existsSync(diffPath)) {
    fs.rmSync(diffPath, { force: true });
  }

  return {
    ok,
    reason: ok
      ? ''
      : `Image diff exceeded threshold: diffPixels=${diffPixels}, diffRatio=${diffRatio.toFixed(6)}`,
    diffPixels,
    diffRatio
  };
}

function createDomSnapshot(metrics) {
  return {
    tags: metrics.tagTexts,
    displayCount: metrics.displayCount,
    hasRenderError: metrics.hasRenderError,
    fontChecks: metrics.fontChecks,
    overlapCount: metrics.layout.overlapCount,
    gapViolations: metrics.layout.gapViolations,
    isClipped: metrics.layout.isClipped
  };
}

function compareDomSnapshot(expected, actual) {
  const failures = [];

  if (!sequenceEquals(expected.tags || [], actual.tags || [])) {
    failures.push('DOM baseline tag sequence mismatch');
  }
  if (Number(expected.displayCount) !== Number(actual.displayCount)) {
    failures.push('DOM baseline displayCount mismatch');
  }
  if (Boolean(expected.hasRenderError) !== Boolean(actual.hasRenderError)) {
    failures.push('DOM baseline hasRenderError mismatch');
  }

  const expectedFonts = expected.fontChecks || [];
  const actualFonts = actual.fontChecks || [];
  if (expectedFonts.length !== actualFonts.length) {
    failures.push('DOM baseline fontChecks length mismatch');
  } else {
    for (let i = 0; i < expectedFonts.length; i++) {
      const left = expectedFonts[i];
      const right = actualFonts[i];
      if (String(left.font) !== String(right.font) || Boolean(left.ok) !== Boolean(right.ok)) {
        failures.push(`DOM baseline fontChecks mismatch at index ${i}`);
        break;
      }
    }
  }

  if (Number(expected.overlapCount) !== Number(actual.overlapCount)) {
    failures.push('DOM baseline overlapCount mismatch');
  }
  if (Number(expected.gapViolations) !== Number(actual.gapViolations)) {
    failures.push('DOM baseline gapViolations mismatch');
  }
  if (Boolean(expected.isClipped) !== Boolean(actual.isClipped)) {
    failures.push('DOM baseline isClipped mismatch');
  }

  return failures;
}

// Collects render metrics directly from page context after host-triggered rendering.
async function collectMetrics(page, testCase) {
  return page.evaluate(async (payload) => {
    const sleepFrames = async () => {
      await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
    };

    const hasOverlap = (a, b) => {
      return !(a.right <= b.left || a.left >= b.right || a.bottom <= b.top || a.top >= b.bottom);
    };

    const toRect = (domRect) => ({
      left: domRect.left,
      right: domRect.right,
      top: domRect.top,
      bottom: domRect.bottom,
      width: domRect.width,
      height: domRect.height
    });
    const normalizeTagText = (value) =>
      String(value || '')
        // Remove zero-width chars that may be injected for layout.
        .replace(/[\u200B-\u200D\uFEFF]/g, '')
        .replace(/\s+/g, ' ')
        .trim();
    const splitTagTokens = (value) => {
      const normalized = normalizeTagText(value);
      if (!normalized) {
        return [];
      }

      const matches = normalized.match(/\([^()]+\)/g);
      if (matches && matches.length > 0) {
        return matches.map((token) => normalizeTagText(token)).filter(Boolean);
      }

      return [normalized];
    };

    localStorage.removeItem('slidetex:user-settings');

    if (!window.slideTex || typeof window.slideTex.renderFromHost !== 'function') {
      throw new Error('window.slideTex.renderFromHost is not available');
    }

    await window.slideTex.renderFromHost({
      latex: payload.latex,
      options: payload.options
    });

    if (document.fonts && document.fonts.ready) {
      await document.fonts.ready;
    }
    await sleepFrames();

    const statusText = (document.getElementById('status')?.textContent || '').trim();
    const errorElement = document.getElementById('errorMessage');
    const errorMessage = (errorElement?.textContent || '').trim();
    const errorVisible = Boolean(errorElement && !errorElement.classList.contains('hidden'));

    const previewContent = document.getElementById('previewContent');
    const renderErrorElement = document.querySelector('#previewContent mjx-merror, #previewContent [data-mjx-error]');
    const renderErrorText = renderErrorElement
      ? String(renderErrorElement.getAttribute('title') || renderErrorElement.textContent || '').trim()
      : '';

    let tagTexts = [];
    try {
      const raw = previewContent?.dataset?.tagTokens || '[]';
      const parsed = JSON.parse(raw);
      if (Array.isArray(parsed)) {
        tagTexts = parsed.map((item) => normalizeTagText(item)).filter((text) => text.length > 0);
      }
    } catch {
      tagTexts = [];
    }

    const displayCount = previewContent?.dataset?.displayMode === 'display' ? 1 : 0;

    let overlapCount = 0;
    let gapViolations = 0;
    const tagGaps = [];

    for (const display of []) {
      const tags = Array.from(display.querySelectorAll('.tag')).map((el) => toRect(el.getBoundingClientRect()));
      const bases = Array.from(display.querySelectorAll('.base'))
        .filter((el) => !el.closest('.tag'))
        .map((el) => toRect(el.getBoundingClientRect()));

      if (tags.length === 0 || bases.length === 0) {
        continue;
      }

      const formulaRect = {
        left: Math.min(...bases.map((r) => r.left)),
        right: Math.max(...bases.map((r) => r.right)),
        top: Math.min(...bases.map((r) => r.top)),
        bottom: Math.max(...bases.map((r) => r.bottom))
      };

      for (const tagRect of tags) {
        if (hasOverlap(formulaRect, tagRect)) {
          overlapCount += 1;
        }

        const gap = tagRect.left - formulaRect.right;
        tagGaps.push(gap);
        if (Number.isFinite(payload.layout.minTagGapPx) && gap < payload.layout.minTagGapPx) {
          gapViolations += 1;
        }
        if (Number.isFinite(payload.layout.maxTagGapPx) && gap > payload.layout.maxTagGapPx) {
          gapViolations += 1;
        }
      }
    }

    const previewBox = document.getElementById('previewBox');
    const isClipped = Boolean(
      previewBox && previewContent && (
        previewContent.scrollWidth > previewBox.clientWidth + 1 ||
        previewContent.scrollHeight > previewBox.clientHeight + 1
      )
    );

    const fontChecks = (payload.fontChecks || []).map((fontName) => {
      const query = `16px \"${fontName}\"`;
      const ok = Boolean(document.fonts && document.fonts.check(query));
      return { font: fontName, ok };
    });

    return {
      statusText,
      errorMessage,
      errorVisible,
      hasRenderError: Boolean(renderErrorElement),
      renderErrorText,
      tagTexts,
      displayCount,
      fontChecks,
      layout: {
        overlapCount,
        gapViolations,
        tagGaps,
        isClipped
      },
      preview: {
        width: previewContent ? previewContent.scrollWidth : 0,
        height: previewContent ? previewContent.scrollHeight : 0
      }
    };
  }, {
    latex: testCase.latex,
    options: testCase.options,
    fontChecks: testCase.fontChecks,
    layout: testCase.layout
  });
}

// Executes full known-good workflow: select cases, render, compare, and write report.
async function run() {
  const args = parseArgs(process.argv.slice(2));
  if (!fs.existsSync(args.fixture)) {
    throw new Error(`Fixture not found: ${args.fixture}`);
  }

  if (!fs.existsSync(webRoot)) {
    throw new Error(`WebUI directory not found: ${webRoot}`);
  }

  const fixture = readJson(args.fixture);
  const cases = pickCaseList(fixture, args.caseIds, args.suite);
  if (cases.length === 0) {
    throw new Error(`No cases selected (suite=${args.suite}).`);
  }

  const chromePath = resolveChromePath(args.chromePath);
  if (!chromePath) {
    throw new Error('Chrome executable not found. Please set --chromePath or CHROME_PATH.');
  }

  const baselineImageDir = path.join(path.dirname(args.fixture), 'baseline-images');
  const baselineDomDir = path.join(path.dirname(args.fixture), 'baseline-dom');
  ensureDir(baselineImageDir);
  ensureDir(baselineDomDir);

  const actualDir = path.join(args.artifactsDir, 'actual');
  const diffDir = path.join(args.artifactsDir, 'diff');
  const logsDir = path.join(args.artifactsDir, 'logs');
  ensureEmptyDir(actualDir);
  ensureEmptyDir(diffDir);
  ensureEmptyDir(logsDir);

  const serverContext = await startStaticServer(webRoot);
  const browser = await puppeteer.launch({
    executablePath: chromePath,
    headless: args.headless ? 'new' : false,
    defaultViewport: {
      width: 1280,
      height: 900,
      deviceScaleFactor: 1
    },
    args: [
      '--force-color-profile=srgb',
      '--disable-lcd-text',
      '--font-render-hinting=none'
    ]
  });

  const summary = {
    meta: {
      mode: args.mode,
      suite: args.suite,
      fixture: path.relative(repoRoot, args.fixture),
      chromePath,
      baseUrl: serverContext.baseUrl,
      caseCount: cases.length,
      timestamp: new Date().toISOString()
    },
    results: []
  };

  try {
    for (const testCase of cases) {
      const page = await browser.newPage();
      const errors = [];
      const warnings = [];
      const locale = testCase.locale || 'zh-CN';
      const expected = testCase.expected || {};

      await page.evaluateOnNewDocument((injectedLocale) => {
        window.slideTexContext = { uiCulture: injectedLocale };
      }, locale);

      await page.goto(serverContext.baseUrl, { waitUntil: 'networkidle0' });
      const metrics = await collectMetrics(page, testCase);

      const domSnapshot = createDomSnapshot(metrics);
      const baselineImagePath = path.join(baselineImageDir, `${testCase.id}.png`);
      const baselineDomPath = path.join(baselineDomDir, `${testCase.id}.json`);
      const actualImagePath = path.join(actualDir, `${testCase.id}.png`);
      const diffImagePath = path.join(diffDir, `${testCase.id}.png`);
      const logPath = path.join(logsDir, `${testCase.id}.json`);

      if (expected.errorContains) {
        const errorSource = `${metrics.errorMessage} ${metrics.renderErrorText}`;
        const expectedErrorTokens = Array.isArray(expected.errorContains)
          ? expected.errorContains.map((token) => String(token))
          : [String(expected.errorContains)];
        const matched = expectedErrorTokens.some((token) => errorSource.includes(token));
        if (!matched) {
          errors.push(`Expected error to contain one of: ${expectedErrorTokens.join(' | ')}`);
        }
      } else {
        if (metrics.errorVisible || metrics.hasRenderError) {
          errors.push(`Unexpected render error: ${metrics.errorMessage || metrics.renderErrorText || 'unknown error'}`);
        }
      }

      const expectedTags = (expected.tags || []).map((v) => String(v));
      if (!sequenceEquals(expectedTags, metrics.tagTexts)) {
        errors.push(`Tag mismatch. expected=${JSON.stringify(expectedTags)}, actual=${JSON.stringify(metrics.tagTexts)}`);
      }

      if (typeof expected.requireDisplay === 'boolean') {
        const hasDisplay = metrics.displayCount > 0;
        if (hasDisplay !== expected.requireDisplay) {
          errors.push(`Display mode mismatch. expectedDisplay=${expected.requireDisplay}, actualDisplay=${hasDisplay}`);
        }
      }

      const allowTagOverlap = Boolean(testCase.layout.allowTagOverlap);
      if (!allowTagOverlap && metrics.layout.overlapCount > 0) {
        errors.push(`Tag overlaps formula body: overlapCount=${metrics.layout.overlapCount}`);
      }

      if (metrics.layout.gapViolations > 0) {
        errors.push(`Tag gap out of range: gapViolations=${metrics.layout.gapViolations}`);
      }

      if (Boolean(testCase.layout.requireNotClipped) && metrics.layout.isClipped) {
        errors.push('Preview content is clipped by preview box.');
      }

      const fontFailures = metrics.fontChecks.filter((item) => !item.ok).map((item) => item.font);
      if (fontFailures.length > 0) {
        errors.push(`Font check failed: ${fontFailures.join(', ')}`);
      }

      if (!testCase.skipImageDiff && !expected.errorContains) {
        const previewHandle = await page.$('#previewContent');
        if (!previewHandle) {
          errors.push('Cannot locate #previewContent for screenshot.');
        } else {
          await previewHandle.screenshot({ path: actualImagePath });

          if (args.mode === 'update-baseline') {
            fs.copyFileSync(actualImagePath, baselineImagePath);
          } else if (!fs.existsSync(baselineImagePath)) {
            errors.push(`Baseline image not found: ${path.relative(repoRoot, baselineImagePath)}`);
          } else {
            const compare = compareImages(baselineImagePath, actualImagePath, diffImagePath, testCase.imageDiff);
            if (!compare.ok) {
              errors.push(compare.reason);
            }
            metrics.imageDiff = {
              diffPixels: compare.diffPixels,
              diffRatio: compare.diffRatio,
              threshold: testCase.imageDiff
            };
          }
        }
      }

      if (args.mode === 'update-baseline') {
        writeJson(baselineDomPath, domSnapshot);
      } else if (fs.existsSync(baselineDomPath)) {
        const baselineDom = readJson(baselineDomPath);
        const domFailures = compareDomSnapshot(baselineDom, domSnapshot);
        errors.push(...domFailures);
      } else {
        warnings.push(`DOM baseline missing: ${path.relative(repoRoot, baselineDomPath)}`);
      }

      const result = {
        caseId: testCase.id,
        pass: errors.length === 0,
        mode: args.mode,
        locale,
        metrics,
        warnings,
        errors
      };

      writeJson(logPath, result);
      summary.results.push(result);
      await page.close();
    }
  } finally {
    await browser.close();
    await new Promise((resolve) => serverContext.server.close(resolve));
  }

  const failed = summary.results.filter((item) => !item.pass);
  summary.meta.failedCount = failed.length;
  summary.meta.passedCount = summary.results.length - failed.length;

  const reportPath = path.join(args.artifactsDir, 'report.json');
  writeJson(reportPath, summary);

  if (failed.length > 0) {
    const ids = failed.map((item) => item.caseId).join(', ');
    throw new Error(`Render known-good comparison failed. Cases: ${ids}. Report: ${path.relative(repoRoot, reportPath)}`);
  }

  console.log(`Render known-good comparison passed. Cases: ${summary.results.length}.`);
  console.log(`Report: ${path.relative(repoRoot, reportPath)}`);
}

run().catch((error) => {
  console.error(error instanceof Error ? error.stack || error.message : String(error));
  process.exitCode = 1;
});
