#!/usr/bin/env node
// SlideTeX Note: End-to-end integration test for the main WebUI user flow.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import puppeteer from 'puppeteer-core';
import { resolveChromePath, startStaticServer, ensureDir, ensureEmptyDir } from './lib/test-infra.mjs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');
const webRoot = path.join(repoRoot, 'src', 'SlideTeX.WebUI');
const artifactsDir = path.join(repoRoot, 'artifacts', 'main-flow');
const testImagePath = path.join(repoRoot, 'tests', 'render-regression', 'baseline-images', 'fraction_sqrt_layout.png');

function assert(condition, message) {
  if (!condition) throw new Error(`Assertion failed: ${message}`);
}

const MOCK_HOST_SCRIPT = `
window.slidetexHost = {
  _calls: { renderSuccess: [], renderError: [], insert: [], update: [], ocr: [] },
  notifyRenderSuccess(json) { this._calls.renderSuccess.push(JSON.parse(json)); },
  notifyRenderError(msg) { this._calls.renderError.push(msg); },
  requestInsert() { this._calls.insert.push(Date.now()); },
  requestUpdate() { this._calls.update.push(Date.now()); },
  requestFormulaOcr(dataUrl, optionsJson) {
    this._calls.ocr.push({ dataUrl, optionsJson });
  },
  requestOpenPane() {},
  requestEditSelected() {},
  requestRenumber() {}
};
`;

async function setupPage(browser, baseUrl) {
  const page = await browser.newPage();
  await page.evaluateOnNewDocument(MOCK_HOST_SCRIPT);
  await page.goto(baseUrl, { waitUntil: 'networkidle0' });
  // Wait for initial render triggered by page load to settle
  await page.waitForFunction(
    () => window.slidetexHost._calls.renderSuccess.length > 0,
    { timeout: 15000 }
  );
  // Reset call logs after initial render
  await page.evaluate(() => {
    const c = window.slidetexHost._calls;
    c.renderSuccess = []; c.renderError = [];
    c.insert = []; c.update = []; c.ocr = [];
  });
  return page;
}

// Test 1: Render + Insert flow
async function testRenderAndInsert(browser, baseUrl) {
  const page = await setupPage(browser, baseUrl);
  try {
    const latex = '\\frac{a}{b} + \\sqrt{c}';
    await page.evaluate(async (tex) => {
      await window.slideTex.renderFromHost({
        latex: tex,
        options: { fontPt: 24, dpi: 300, colorHex: '#000000', isTransparent: true, displayMode: 'auto' }
      });
    }, latex);

    await page.waitForFunction(
      () => window.slidetexHost._calls.renderSuccess.length > 0,
      { timeout: 15000 }
    );

    const result = await page.evaluate(() => ({
      successCount: window.slidetexHost._calls.renderSuccess.length,
      payload: window.slidetexHost._calls.renderSuccess[0],
      errorCount: window.slidetexHost._calls.renderError.length
    }));

    assert(result.successCount === 1, 'Expected exactly 1 renderSuccess call');
    assert(result.errorCount === 0, 'Expected 0 renderError calls');

    const p = result.payload;
    assert(typeof p.pngBase64 === 'string' && p.pngBase64.startsWith('iVBORw0KGgo'),
      'pngBase64 should start with PNG magic bytes');
    assert(p.pixelWidth > 0, `pixelWidth should be > 0, got ${p.pixelWidth}`);
    assert(p.pixelHeight > 0, `pixelHeight should be > 0, got ${p.pixelHeight}`);
    assert(p.latex === latex, `latex should match input`);

    // Click insert button and verify host call
    await page.click('#insertBtn');
    const insertCount = await page.evaluate(() => window.slidetexHost._calls.insert.length);
    assert(insertCount === 1, 'Expected requestInsert called once');

    return { pass: true, name: 'render-and-insert' };
  } catch (error) {
    return { pass: false, name: 'render-and-insert', error: error.message };
  } finally {
    await page.close();
  }
}

// Test 2: OCR flow
async function testOcrFlow(browser, baseUrl) {
  const page = await setupPage(browser, baseUrl);
  try {
    // Upload test image via file chooser
    const [fileChooser] = await Promise.all([
      page.waitForFileChooser(),
      page.click('#ocrBtn')
    ]);
    await fileChooser.accept([testImagePath]);

    // Wait for requestFormulaOcr to be called on the mock host
    await page.waitForFunction(
      () => window.slidetexHost._calls.ocr.length > 0,
      { timeout: 10000 }
    );

    const ocrCall = await page.evaluate(() => {
      const call = window.slidetexHost._calls.ocr[0];
      return {
        hasDataUrl: typeof call.dataUrl === 'string' && call.dataUrl.startsWith('data:image/'),
        hasOptions: typeof call.optionsJson === 'string'
      };
    });
    assert(ocrCall.hasDataUrl, 'OCR dataUrl should be a valid data: URL');
    assert(ocrCall.hasOptions, 'OCR optionsJson should be a string');

    // Simulate host callback with OCR result and verify re-render
    await page.evaluate(() => {
      window.slidetexHost._calls.renderSuccess = [];
      window.slideTex.onFormulaOcrSuccess({ latex: '\\frac{a}{b}' });
    });

    await page.waitForFunction(
      () => window.slidetexHost._calls.renderSuccess.length > 0,
      { timeout: 15000 }
    );

    const afterOcr = await page.evaluate(() => ({
      renderCount: window.slidetexHost._calls.renderSuccess.length,
      latex: window.slidetexHost._calls.renderSuccess[0]?.latex || ''
    }));
    assert(afterOcr.renderCount >= 1, 'Should have triggered re-render after OCR');
    assert(afterOcr.latex.includes('\\frac{a}{b}'), 'Re-render should use OCR result');

    return { pass: true, name: 'ocr-flow' };
  } catch (error) {
    return { pass: false, name: 'ocr-flow', error: error.message };
  } finally {
    await page.close();
  }
}

// Test 3: Render error handling
async function testRenderError(browser, baseUrl) {
  const page = await setupPage(browser, baseUrl);
  try {
    await page.evaluate(async (tex) => {
      await window.slideTex.renderFromHost({ latex: tex });
    }, '\\frac{a}{b');

    // Wait for error to appear in the UI
    await page.waitForFunction(() => {
      const el = document.getElementById('errorMessage');
      return el && !el.classList.contains('hidden');
    }, { timeout: 15000 });

    const result = await page.evaluate(() => {
      const el = document.getElementById('errorMessage');
      return {
        errorVisible: el && !el.classList.contains('hidden'),
        errorText: el?.textContent || '',
        renderErrorCount: window.slidetexHost._calls.renderError.length
      };
    });

    assert(result.errorVisible, 'Error message should be visible');
    assert(result.renderErrorCount > 0, 'notifyRenderError should have been called');

    return { pass: true, name: 'render-error' };
  } catch (error) {
    return { pass: false, name: 'render-error', error: error.message };
  } finally {
    await page.close();
  }
}

async function run() {
  if (!fs.existsSync(webRoot)) {
    throw new Error(`WebUI directory not found: ${webRoot}`);
  }

  const chromePath = resolveChromePath(process.env.CHROME_PATH || '');
  if (!chromePath) {
    throw new Error('Chrome executable not found. Set CHROME_PATH or install Chrome.');
  }

  ensureEmptyDir(artifactsDir);

  const serverContext = await startStaticServer(webRoot);
  const browser = await puppeteer.launch({
    executablePath: chromePath,
    headless: 'new',
    defaultViewport: { width: 1280, height: 900, deviceScaleFactor: 1 },
    args: ['--force-color-profile=srgb', '--disable-lcd-text', '--font-render-hinting=none']
  });

  const results = [];
  try {
    results.push(await testRenderAndInsert(browser, serverContext.baseUrl));
    results.push(await testOcrFlow(browser, serverContext.baseUrl));
    results.push(await testRenderError(browser, serverContext.baseUrl));
  } finally {
    await browser.close();
    await new Promise((resolve) => serverContext.server.close(resolve));
  }

  const report = {
    timestamp: new Date().toISOString(),
    chromePath,
    total: results.length,
    passed: results.filter((r) => r.pass).length,
    failed: results.filter((r) => !r.pass).length,
    results
  };

  const reportPath = path.join(artifactsDir, 'report.json');
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2) + '\n', 'utf8');

  const failures = results.filter((r) => !r.pass);
  if (failures.length > 0) {
    for (const f of failures) {
      console.error(`FAIL: ${f.name} — ${f.error}`);
    }
    throw new Error(`Main flow test failed. Cases: ${failures.map((f) => f.name).join(', ')}`);
  }

  console.log(`Main flow test passed. Cases: ${results.length}.`);
  console.log(`Report: ${path.relative(repoRoot, reportPath)}`);
}

run().catch((error) => {
  console.error(error instanceof Error ? error.stack || error.message : String(error));
  process.exitCode = 1;
});