// SlideTeX Note: Shared test infrastructure for headless browser test scripts.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import http from 'node:http';
import pixelmatch from 'pixelmatch';
import { PNG } from 'pngjs';

export function resolvePath(input, baseDir) {
  if (!input) {
    return '';
  }
  return path.isAbsolute(input) ? input : path.resolve(baseDir, input);
}

export function readJson(filePath) {
  const raw = fs.readFileSync(filePath, 'utf8');
  // Strip UTF-8 BOM that Windows PowerShell may emit.
  const text = raw.charCodeAt(0) === 0xFEFF ? raw.slice(1) : raw;
  return JSON.parse(text);
}

export function writeJson(filePath, data) {
  const dir = path.dirname(filePath);
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(filePath, JSON.stringify(data, null, 2) + '\n', 'utf8');
}

export function ensureDir(dir) {
  fs.mkdirSync(dir, { recursive: true });
}

export function ensureEmptyDir(dir) {
  fs.rmSync(dir, { recursive: true, force: true });
  fs.mkdirSync(dir, { recursive: true });
}

// Resolves Chrome binary path from CLI, env var, and common local install locations.
export function resolveChromePath(cliPath) {
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

export function mimeByExtension(filePath) {
  const ext = path.extname(filePath).toLowerCase();
  switch (ext) {
    case '.html': return 'text/html; charset=utf-8';
    case '.js': return 'application/javascript; charset=utf-8';
    case '.css': return 'text/css; charset=utf-8';
    case '.json': return 'application/json; charset=utf-8';
    case '.png': return 'image/png';
    case '.svg': return 'image/svg+xml';
    case '.woff2': return 'font/woff2';
    case '.woff': return 'font/woff';
    case '.ttf': return 'font/ttf';
    case '.map': return 'application/json; charset=utf-8';
    default: return 'application/octet-stream';
  }
}

// Starts a local static file server for loading WebUI assets in headless browser runs.
export function startStaticServer(rootDir) {
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

export function loadPng(filePath) {
  return PNG.sync.read(fs.readFileSync(filePath));
}

// Compares PNG outputs and writes diff image only when thresholds are exceeded.
export function compareImages(expectedPath, actualPath, diffPath, threshold) {
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
