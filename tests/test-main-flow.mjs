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
  _calls: { renderSuccess: [], renderError: [], insert: [], update: [], ocr: [], tagSelected: [] },
  notifyRenderSuccess(json) { this._calls.renderSuccess.push(JSON.parse(json)); },
  notifyRenderError(msg) { this._calls.renderError.push(msg); },
  requestInsert() { this._calls.insert.push(Date.now()); },
  requestUpdate() { this._calls.update.push(Date.now()); },
  requestFormulaOcr(dataUrl, optionsJson) {
    this._calls.ocr.push({ dataUrl, optionsJson });
  },
  requestTagSelected() { this._calls.tagSelected.push(Date.now()); },
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
    c.insert = []; c.update = []; c.ocr = []; c.tagSelected = [];
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

// Test 2: Complex formula requiring MathJax async retry
async function testComplexFormulaRetry(browser, baseUrl) {
  const page = await setupPage(browser, baseUrl);
  try {
    const latex = '{\\cal W} \\equiv \\frac {1} {4 \\rho^{2}} \\Big [\\cosh ( 2 \\varphi_{2} ) ( \\rho^{6} - 2 ) - ( 3 \\rho^{6} + 2 ) \\Big], \\qquad \\rho \\equiv e^{\\frac {1} {\\sqrt {6}} \\varphi_{1}}.';
    await page.evaluate(async (tex) => {
      await window.slideTex.renderFromHost({
        latex: tex,
        options: { fontPt: 24, dpi: 300, colorHex: '#000000', isTransparent: true, displayMode: 'display' }
      });
    }, latex);

    await page.waitForFunction(
      () => window.slidetexHost._calls.renderSuccess.length > 0 ||
            window.slidetexHost._calls.renderError.length > 0,
      { timeout: 15000 }
    );

    const result = await page.evaluate(() => ({
      successCount: window.slidetexHost._calls.renderSuccess.length,
      errorCount: window.slidetexHost._calls.renderError.length,
      payload: window.slidetexHost._calls.renderSuccess[0] || null
    }));

    assert(result.errorCount === 0, `Expected 0 renderError calls, got ${result.errorCount}`);
    assert(result.successCount === 1, 'Expected exactly 1 renderSuccess call');
    assert(result.payload.pixelWidth > 0, `pixelWidth should be > 0`);
    assert(result.payload.pixelHeight > 0, `pixelHeight should be > 0`);

    // Verify SVG structural integrity — ensures the retry path produces
    // the same math layout as a direct tex2svgPromise render.
    const svgCheck = await page.evaluate(() => {
      const pc = document.getElementById('previewContent');
      const svg = pc?.querySelector('svg');
      const merror = pc?.querySelector('mjx-merror, [data-mjx-error]');
      return {
        hasSvg: !!svg,
        hasMerror: !!merror,
        viewBox: svg?.getAttribute('viewBox') || '',
        pathCount: svg ? svg.querySelectorAll('path').length : 0,
      };
    });

    assert(svgCheck.hasSvg, 'Preview should contain an SVG element');
    assert(!svgCheck.hasMerror, 'Preview should not contain MathJax error markers');
    assert(svgCheck.viewBox === '0 -1342 25139.8 2277.9',
      `SVG viewBox should match expected layout, got "${svgCheck.viewBox}"`);
    assert(svgCheck.pathCount >= 40,
      `SVG should have >= 40 path elements (glyphs), got ${svgCheck.pathCount}`);

    return { pass: true, name: 'complex-formula-retry' };
  } catch (error) {
    return { pass: false, name: 'complex-formula-retry', error: error.message };
  } finally {
    await page.close();
  }
}

// Test 2b: Simulate WebView2 — dynamic font loading is broken, only
// pre-loaded fonts should work.  This catches the real-world bug where
// \cal renders as just a "W" in the PowerPoint VSTO add-in.
//
// We patch fetch() and the MathJax loader BEFORE the page loads so that
// the font preloading code path also runs under the same constraints as
// WebView2 (where fetch for local font files may fail or the loader's
// promise queue hangs).
async function testWebView2FontPreload(browser, baseUrl) {
  // Do NOT use setupPage — we need evaluateOnNewDocument before goto.
  const page = await browser.newPage();
  try {
    // Inject mock host (same as setupPage)
    await page.evaluateOnNewDocument(MOCK_HOST_SCRIPT);

    // Patch fetch + MathJax loader BEFORE page loads to simulate WebView2.
    // In WebView2, MathJax's dynamic font loading hangs because its
    // internal promise queue is broken.  We simulate this by making
    // fetch() for font URLs return a never-resolving promise, and
    // patching the loader after MathJax initialises.
    await page.evaluateOnNewDocument(`
      (function() {
        var origFetch = window.fetch;
        window.fetch = function(url) {
          if (typeof url === 'string' && url.indexOf('dynamic/') >= 0) {
            return new Promise(function() {}); // hang forever
          }
          return origFetch.apply(this, arguments);
        };
        // Patch MathJax loader once it appears
        var _timer = setInterval(function() {
          if (window.MathJax && window.MathJax.loader && window.MathJax.loader.load) {
            window.MathJax.loader.load = function() {
              return new Promise(function() {});
            };
            clearInterval(_timer);
          }
        }, 10);
      })();
    `);

    await page.goto(baseUrl, { waitUntil: 'networkidle0', timeout: 30000 });

    // Wait for initial render to settle (may take longer with broken fonts)
    await page.waitForFunction(
      () => window.slidetexHost._calls.renderSuccess.length > 0,
      { timeout: 20000 }
    );

    const latex = '{\\cal W} \\equiv \\frac {1} {4 \\rho^{2}} \\Big [\\cosh ( 2 \\varphi_{2} ) ( \\rho^{6} - 2 ) - ( 3 \\rho^{6} + 2 ) \\Big], \\qquad \\rho \\equiv e^{\\frac {1} {\\sqrt {6}} \\varphi_{1}}.';

    // Render via MathJax directly — must succeed using only pre-loaded
    // font data, without any dynamic loading.
    const renderOk = await page.evaluate((tex) => {
      try {
        var mj = window.MathJax;
        var math = mj.tex2svg(tex, { display: true });
        var svg = math && math.querySelector ? math.querySelector('svg') : null;
        if (!svg) return { ok: false, reason: 'no SVG produced' };
        // Verify calligraphic W glyph path is present (starts with "902 586")
        // as opposed to the italic W fallback (starts with "956 680")
        var html = svg.outerHTML;
        var hasCalGlyph = html.indexOf('902 586C902 570') >= 0;
        var hasItalicFallback = html.indexOf('956 680C937 680') >= 0;
        return {
          ok: true,
          viewBox: svg.getAttribute('viewBox') || '',
          pathCount: svg.querySelectorAll('path').length,
          svgLength: html.length,
          hasCalGlyph: hasCalGlyph,
          hasItalicFallback: hasItalicFallback
        };
      } catch (e) {
        return {
          ok: false,
          reason: e.retry ? 'RETRY error — font not pre-loaded' : (e.message || String(e))
        };
      }
    }, latex);

    assert(renderOk.ok,
      'tex2svg should succeed without dynamic loading, but: ' + renderOk.reason);
    assert(renderOk.pathCount >= 40,
      'SVG should have >= 40 paths (got ' + renderOk.pathCount + ') — missing font glyphs');
    assert(renderOk.viewBox === '0 -1342 25139.8 2277.9',
      'viewBox should match full formula, got "' + renderOk.viewBox + '"');
    assert(renderOk.hasCalGlyph,
      'SVG should contain calligraphic W glyph path (902 586), not italic fallback');
    assert(!renderOk.hasItalicFallback,
      'SVG should NOT contain italic W fallback path (956 680)');

    return { pass: true, name: 'webview2-font-preload' };
  } catch (error) {
    return { pass: false, name: 'webview2-font-preload', error: error.message };
  } finally {
    await page.close();
  }
}

// Test 3: OCR flow
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

// Test 4: Render error handling
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

// Test 5: Host image OCR flow — onHostImageOcr shows button, click triggers OCR + tag
async function testHostImageOcrFlow(browser, baseUrl) {
  const page = await setupPage(browser, baseUrl);
  try {
    // Simulate host sending a non-meta image selection
    const btnHidden = await page.evaluate(() => {
      return document.getElementById('ocrSelectedBtn').classList.contains('hidden');
    });
    assert(btnHidden, 'ocrSelectedBtn should be hidden initially');

    await page.evaluate(() => {
      window.slideTex.onHostImageOcr({ imageBase64: 'data:image/png;base64,iVBORw0KGgo=' });
    });

    const btnVisible = await page.evaluate(() => {
      return !document.getElementById('ocrSelectedBtn').classList.contains('hidden');
    });
    assert(btnVisible, 'ocrSelectedBtn should be visible after onHostImageOcr');

    // Click the OCR button — should trigger requestFormulaOcr
    await page.click('#ocrSelectedBtn');

    await page.waitForFunction(
      () => window.slidetexHost._calls.ocr.length > 0,
      { timeout: 10000 }
    );

    const ocrCall = await page.evaluate(() => {
      const call = window.slidetexHost._calls.ocr[0];
      return {
        hasDataUrl: typeof call.dataUrl === 'string' && call.dataUrl.startsWith('data:image/'),
        btnHiddenAfterClick: document.getElementById('ocrSelectedBtn').classList.contains('hidden')
      };
    });
    assert(ocrCall.hasDataUrl, 'OCR dataUrl should be a valid data: URL');
    assert(ocrCall.btnHiddenAfterClick, 'ocrSelectedBtn should be hidden after click');

    // Simulate OCR success — should trigger render then requestTagSelected
    await page.evaluate(() => {
      window.slidetexHost._calls.renderSuccess = [];
      window.slidetexHost._calls.tagSelected = [];
      window.slideTex.onFormulaOcrSuccess({ latex: '\\frac{a}{b}' });
    });

    await page.waitForFunction(
      () => window.slidetexHost._calls.tagSelected.length > 0,
      { timeout: 15000 }
    );

    const afterOcr = await page.evaluate(() => ({
      renderCount: window.slidetexHost._calls.renderSuccess.length,
      tagSelectedCount: window.slidetexHost._calls.tagSelected.length
    }));
    assert(afterOcr.renderCount >= 1, 'Should have triggered re-render after OCR');
    assert(afterOcr.tagSelectedCount === 1, 'Should have called requestTagSelected once');

    return { pass: true, name: 'host-image-ocr-flow' };
  } catch (error) {
    return { pass: false, name: 'host-image-ocr-flow', error: error.message };
  } finally {
    await page.close();
  }
}

// Test 6: Selection cleared hides OCR button
async function testSelectionCleared(browser, baseUrl) {
  const page = await setupPage(browser, baseUrl);
  try {
    // Show the OCR button first
    await page.evaluate(() => {
      window.slideTex.onHostImageOcr({ imageBase64: 'data:image/png;base64,iVBORw0KGgo=' });
    });

    const btnVisible = await page.evaluate(() => {
      return !document.getElementById('ocrSelectedBtn').classList.contains('hidden');
    });
    assert(btnVisible, 'ocrSelectedBtn should be visible after onHostImageOcr');

    // Clear selection
    await page.evaluate(() => {
      window.slideTex.onSelectionCleared();
    });

    const btnHidden = await page.evaluate(() => {
      return document.getElementById('ocrSelectedBtn').classList.contains('hidden');
    });
    assert(btnHidden, 'ocrSelectedBtn should be hidden after onSelectionCleared');

    return { pass: true, name: 'selection-cleared' };
  } catch (error) {
    return { pass: false, name: 'selection-cleared', error: error.message };
  } finally {
    await page.close();
  }
}

// Test 7: renderFromHost clears pending OCR state
async function testRenderFromHostClearsOcr(browser, baseUrl) {
  const page = await setupPage(browser, baseUrl);
  try {
    // Show OCR button
    await page.evaluate(() => {
      window.slideTex.onHostImageOcr({ imageBase64: 'data:image/png;base64,iVBORw0KGgo=' });
    });

    const btnVisible = await page.evaluate(() => {
      return !document.getElementById('ocrSelectedBtn').classList.contains('hidden');
    });
    assert(btnVisible, 'ocrSelectedBtn should be visible after onHostImageOcr');

    // renderFromHost should clear OCR state (formula shape selected)
    await page.evaluate(async () => {
      window.slidetexHost._calls.renderSuccess = [];
      await window.slideTex.renderFromHost({
        latex: 'x^2',
        options: { fontPt: 24, dpi: 300, colorHex: '#000000', isTransparent: true, displayMode: 'auto' }
      });
    });

    await page.waitForFunction(
      () => window.slidetexHost._calls.renderSuccess.length > 0,
      { timeout: 15000 }
    );

    const btnHidden = await page.evaluate(() => {
      return document.getElementById('ocrSelectedBtn').classList.contains('hidden');
    });
    assert(btnHidden, 'ocrSelectedBtn should be hidden after renderFromHost');

    return { pass: true, name: 'render-from-host-clears-ocr' };
  } catch (error) {
    return { pass: false, name: 'render-from-host-clears-ocr', error: error.message };
  } finally {
    await page.close();
  }
}

// Test 8: Render with custom color and DPI
async function testRenderWithColorAndDpi(browser, baseUrl) {
  const page = await setupPage(browser, baseUrl);
  try {
    await page.evaluate(async () => {
      await window.slideTex.renderFromHost({
        latex: '\\alpha + \\beta = \\gamma',
        options: { fontPt: 36, dpi: 600, colorHex: '#ff0000', isTransparent: false, displayMode: 'display' }
      });
    });

    await page.waitForFunction(
      () => window.slidetexHost._calls.renderSuccess.length > 0,
      { timeout: 15000 }
    );

    const result = await page.evaluate(() => {
      const p = window.slidetexHost._calls.renderSuccess[0];
      return {
        hasBase64: typeof p.pngBase64 === 'string' && p.pngBase64.length > 100,
        width: p.pixelWidth,
        height: p.pixelHeight,
        colorHex: p.options?.colorHex,
        dpi: p.options?.dpi
      };
    });

    assert(result.hasBase64, 'Should produce valid PNG base64');
    assert(result.width > 0, 'pixelWidth should be > 0');
    assert(result.height > 0, 'pixelHeight should be > 0');
    assert(result.colorHex === '#ff0000', 'Options should preserve colorHex');
    assert(result.dpi === 600, 'Options should preserve dpi');

    return { pass: true, name: 'render-color-and-dpi' };
  } catch (error) {
    return { pass: false, name: 'render-color-and-dpi', error: error.message };
  } finally {
    await page.close();
  }
}

// Test 9: OCR error callback
async function testOcrErrorCallback(browser, baseUrl) {
  const page = await setupPage(browser, baseUrl);
  try {
    await page.evaluate(() => {
      window.slideTex.onFormulaOcrError({ message: 'OCR service timeout' });
    });

    await page.waitForFunction(() => {
      const el = document.getElementById('errorMessage');
      return el && !el.classList.contains('hidden');
    }, { timeout: 5000 });

    const result = await page.evaluate(() => {
      const el = document.getElementById('errorMessage');
      return {
        errorVisible: el && !el.classList.contains('hidden'),
        errorText: el?.textContent || ''
      };
    });

    assert(result.errorVisible, 'Error message should be visible after OCR error');
    assert(result.errorText.includes('OCR service timeout'), 'Error should contain the message');

    return { pass: true, name: 'ocr-error-callback' };
  } catch (error) {
    return { pass: false, name: 'ocr-error-callback', error: error.message };
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
    results.push(await testComplexFormulaRetry(browser, serverContext.baseUrl));
    results.push(await testWebView2FontPreload(browser, serverContext.baseUrl));
    results.push(await testOcrFlow(browser, serverContext.baseUrl));
    results.push(await testRenderError(browser, serverContext.baseUrl));
    results.push(await testHostImageOcrFlow(browser, serverContext.baseUrl));
    results.push(await testSelectionCleared(browser, serverContext.baseUrl));
    results.push(await testRenderFromHostClearsOcr(browser, serverContext.baseUrl));
    results.push(await testRenderWithColorAndDpi(browser, serverContext.baseUrl));
    results.push(await testOcrErrorCallback(browser, serverContext.baseUrl));
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