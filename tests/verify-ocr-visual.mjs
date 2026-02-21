#!/usr/bin/env node
// SlideTeX Note: Verifies OCR output by rendering LaTeX visually and comparing against baseline images.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import puppeteer from 'puppeteer-core';
import {
  resolveChromePath,
  startStaticServer,
  ensureDir,
  readJson,
  writeJson,
  resolvePath as resolvePathBase,
  loadPng,
  compareImages
} from './lib/test-infra.mjs';
import pixelmatch from 'pixelmatch';
import { PNG } from 'pngjs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');
const webRoot = path.join(repoRoot, 'src', 'SlideTeX.WebUI');

const require = createRequire(import.meta.url);
const { sanitizeOcrLatex } = require(path.join(webRoot, 'assets', 'js', 'ocr-latex-postprocess.js'));

function parseArgs(argv) {
  const args = {
    report: '',
    fixture: path.join(repoRoot, 'tests', 'render-regression', 'render-visual-mathjax-v1.json'),
    baselineDir: '',
    artifactsDir: path.join(repoRoot, 'artifacts', 'ocr-visual'),
    chromePath: '',
    maxDiffRatio: 0.10,
    headless: true
  };

  for (let i = 0; i < argv.length; i++) {
    const token = argv[i];
    if (token === '--report') {
      args.report = resolvePath(argv[++i]);
      continue;
    }
    if (token === '--fixture') {
      args.fixture = resolvePath(argv[++i]);
      continue;
    }
    if (token === '--baselineDir') {
      args.baselineDir = resolvePath(argv[++i]);
      continue;
    }
    if (token === '--artifactsDir') {
      args.artifactsDir = resolvePath(argv[++i]);
      continue;
    }
    if (token === '--chromePath') {
      args.chromePath = resolvePath(argv[++i]);
      continue;
    }
    if (token === '--maxDiffRatio') {
      args.maxDiffRatio = parseFloat(argv[++i]);
      continue;
    }
    if (token === '--headful') {
      args.headless = false;
      continue;
    }
    throw new Error(`Unknown argument: ${token}`);
  }

  return args;
}

function resolvePath(input) {
  return resolvePathBase(input, repoRoot);
}

// Builds a map of caseId → render options from the visual regression fixture.
function buildRenderOptionsMap(fixture) {
  const defaults = fixture.defaults || {};
  const defaultOptions = defaults.options || {};
  const map = new Map();
  for (const c of fixture.cases || []) {
    const options = { ...defaultOptions, ...(c.options || {}) };
    map.set(c.id, { latex: c.latex, options });
  }
  return map;
}

// Trims white (or near-white) borders from a PNG, returning a cropped copy.
function trimWhiteBorders(img, tolerance = 250) {
  const { width, height, data } = img;
  const isBackground = (x, y) => {
    const idx = (y * width + x) * 4;
    const a = data[idx + 3];
    if (a < 10) return true; // fully transparent = background
    return data[idx] >= tolerance && data[idx + 1] >= tolerance && data[idx + 2] >= tolerance;
  };

  let top = 0, bottom = height - 1, left = 0, right = width - 1;

  outer: for (; top < height; top++) {
    for (let x = 0; x < width; x++) if (!isBackground(x, top)) break outer;
  }
  outer: for (; bottom > top; bottom--) {
    for (let x = 0; x < width; x++) if (!isBackground(x, bottom)) break outer;
  }
  outer: for (; left < width; left++) {
    for (let y = top; y <= bottom; y++) if (!isBackground(left, y)) break outer;
  }
  outer: for (; right > left; right--) {
    for (let y = top; y <= bottom; y++) if (!isBackground(right, y)) break outer;
  }

  const cw = right - left + 1;
  const ch = bottom - top + 1;
  if (cw <= 0 || ch <= 0) return img;

  const cropped = new PNG({ width: cw, height: ch });
  PNG.bitblt(img, cropped, left, top, cw, ch, 0, 0);
  return cropped;
}

// Compares two PNGs after trimming white borders and padding to equal size.
function compareImagesWithPadding(expectedPath, actualPath, diffPath, threshold) {
  const rawExpected = loadPng(expectedPath);
  const rawActual = loadPng(actualPath);
  const expected = trimWhiteBorders(rawExpected);
  const actual = trimWhiteBorders(rawActual);

  // If trimmed sizes differ drastically, the formulas are structurally different.
  const wRatio = Math.max(expected.width, actual.width) / Math.max(1, Math.min(expected.width, actual.width));
  const hRatio = Math.max(expected.height, actual.height) / Math.max(1, Math.min(expected.height, actual.height));
  if (wRatio > 1.5 || hRatio > 1.5) {
    return {
      ok: false,
      reason: `Trimmed size ratio too large: wRatio=${wRatio.toFixed(2)}, hRatio=${hRatio.toFixed(2)} (expected=${expected.width}x${expected.height}, actual=${actual.width}x${actual.height})`,
      diffPixels: NaN,
      diffRatio: NaN
    };
  }

  const w = Math.max(expected.width, actual.width);
  const h = Math.max(expected.height, actual.height);

  const padImage = (img) => {
    if (img.width === w && img.height === h) return img;
    const padded = new PNG({ width: w, height: h, fill: true });
    padded.data.fill(0xFF);
    PNG.bitblt(img, padded, 0, 0, img.width, img.height, 0, 0);
    return padded;
  };

  const ePad = padImage(expected);
  const aPad = padImage(actual);
  const diff = new PNG({ width: w, height: h });

  const diffPixels = pixelmatch(
    ePad.data, aPad.data, diff.data, w, h,
    { threshold: 0.1, includeAA: false }
  );
  const totalPixels = w * h;
  const diffRatio = totalPixels > 0 ? diffPixels / totalPixels : 0;
  const ok = diffPixels <= threshold.maxDiffPixels && diffRatio <= threshold.maxDiffRatio;

  if (!ok) {
    fs.writeFileSync(diffPath, PNG.sync.write(diff));
  } else if (fs.existsSync(diffPath)) {
    fs.rmSync(diffPath, { force: true });
  }

  return {
    ok,
    reason: ok ? '' : `diffPixels=${diffPixels}, diffRatio=${diffRatio.toFixed(6)} (trimmed: expected=${expected.width}x${expected.height}, actual=${actual.width}x${actual.height})`,
    diffPixels,
    diffRatio
  };
}

async function run() {
  const args = parseArgs(process.argv.slice(2));

  if (!args.report || !fs.existsSync(args.report)) {
    console.log('OCR visual verification skipped: no OCR report found.');
    if (args.report) {
      console.log(`  --report path: ${args.report}`);
    }
    process.exitCode = 0;
    return;
  }

  if (!fs.existsSync(args.fixture)) {
    throw new Error(`Visual regression fixture not found: ${args.fixture}`);
  }
  if (!fs.existsSync(webRoot)) {
    throw new Error(`WebUI directory not found: ${webRoot}`);
  }

  const ocrReport = readJson(args.report);
  const visualFixture = readJson(args.fixture);
  const renderOptionsMap = buildRenderOptionsMap(visualFixture);

  // Determine baseline image directory
  const baselineDir = args.baselineDir
    || path.join(path.dirname(args.fixture), 'baseline-images');
  if (!fs.existsSync(baselineDir)) {
    throw new Error(`Baseline image directory not found: ${baselineDir}`);
  }

  // Filter OCR results that have actualLatex and a matching visual baseline
  const ocrResults = (ocrReport.results || []).filter((r) => {
    if (!r.actualLatex || r.error) return false;
    const baselineImage = path.join(baselineDir, `${r.caseId}.png`);
    return fs.existsSync(baselineImage);
  });

  if (ocrResults.length === 0) {
    console.log('OCR visual verification skipped: no eligible cases with actualLatex and baseline images.');
    process.exitCode = 0;
    return;
  }

  const actualDir = path.join(args.artifactsDir, 'actual');
  const diffDir = path.join(args.artifactsDir, 'diff');
  ensureDir(actualDir);
  ensureDir(diffDir);

  const chromePath = resolveChromePath(args.chromePath);
  if (!chromePath) {
    throw new Error('Chrome executable not found. Please set --chromePath or CHROME_PATH.');
  }

  const serverContext = await startStaticServer(webRoot);
  const browser = await puppeteer.launch({
    executablePath: chromePath,
    headless: args.headless ? 'new' : false,
    defaultViewport: { width: 1280, height: 900, deviceScaleFactor: 1 },
    args: ['--force-color-profile=srgb', '--disable-lcd-text', '--font-render-hinting=none']
  });

  const threshold = { maxDiffPixels: 5000, maxDiffRatio: args.maxDiffRatio };
  const results = [];

  try {
    for (const ocrCase of ocrResults) {
      const caseId = ocrCase.caseId;
      const fixtureEntry = renderOptionsMap.get(caseId);
      const renderOptions = fixtureEntry ? fixtureEntry.options : {};

      const baselineImagePath = path.join(baselineDir, `${caseId}.png`);
      const actualImagePath = path.join(actualDir, `${caseId}.png`);
      const diffImagePath = path.join(diffDir, `${caseId}.png`);

      const page = await browser.newPage();
      try {
        await page.evaluateOnNewDocument(() => {
          window.slideTexContext = { uiCulture: 'zh-CN' };
        });
        await page.goto(serverContext.baseUrl, { waitUntil: 'networkidle0' });

        await page.evaluate(async (payload) => {
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
          await new Promise((resolve) => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        }, { latex: sanitizeOcrLatex(ocrCase.actualLatex), options: renderOptions });

        const previewHandle = await page.$('#previewContent');
        if (!previewHandle) {
          results.push({ caseId, visualMatch: false, diffRatio: NaN, stringCer: ocrCase.cer, error: 'Cannot locate #previewContent' });
          continue;
        }

        await previewHandle.screenshot({ path: actualImagePath });
        const compare = compareImagesWithPadding(baselineImagePath, actualImagePath, diffImagePath, threshold);

        results.push({
          caseId,
          visualMatch: compare.ok,
          diffRatio: compare.diffRatio,
          diffPixels: compare.diffPixels,
          stringCer: ocrCase.cer ?? null,
          reason: compare.reason || ''
        });
      } finally {
        await page.close();
      }
    }
  } finally {
    await browser.close();
    await new Promise((resolve) => serverContext.server.close(resolve));
  }

  // Build report
  const visualPassCount = results.filter((r) => r.visualMatch).length;
  const visualPassRatio = results.length > 0 ? visualPassCount / results.length : 0;
  const stringDiffButVisualMatch = results.filter((r) => r.visualMatch && r.stringCer != null && r.stringCer > 0);

  const report = {
    meta: {
      ocrReport: path.relative(repoRoot, args.report),
      fixture: path.relative(repoRoot, args.fixture),
      baselineDir: path.relative(repoRoot, baselineDir),
      maxDiffRatio: args.maxDiffRatio,
      timestamp: new Date().toISOString()
    },
    summary: {
      totalCases: results.length,
      visualPassCount,
      visualPassRatio: Math.round(visualPassRatio * 1e6) / 1e6,
      stringDiffButVisualMatchCount: stringDiffButVisualMatch.length,
      overallPass: visualPassRatio >= 0.80
    },
    stringDiffButVisualMatch: stringDiffButVisualMatch.map((r) => r.caseId),
    results
  };

  const reportPath = path.join(args.artifactsDir, 'ocr-visual-report.json');
  writeJson(reportPath, report);

  if (report.summary.overallPass) {
    console.log(`OCR visual verification passed. Cases=${results.length} VisualPass=${visualPassCount} Ratio=${visualPassRatio.toFixed(4)}.`);
  } else {
    console.log(`OCR visual verification failed. Cases=${results.length} VisualPass=${visualPassCount} Ratio=${visualPassRatio.toFixed(4)}.`);
    process.exitCode = 1;
  }

  if (stringDiffButVisualMatch.length > 0) {
    console.log(`String-different but visually matching: ${stringDiffButVisualMatch.length} cases.`);
  }

  console.log(`Report: ${path.relative(repoRoot, reportPath)}`);
}

run().catch((error) => {
  console.error(error instanceof Error ? error.stack || error.message : String(error));
  process.exitCode = 1;
});