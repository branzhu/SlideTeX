#!/usr/bin/env node
// SlideTeX Note: Builds OCR baseline fixture from render-regression known-good pairs.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { resolvePath as resolvePathBase, readJson, writeJson } from './lib/test-infra.mjs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');

function resolvePath(input) {
  return resolvePathBase(input, repoRoot);
}

function parseArgs(argv) {
  const args = {
    sourceFixture: path.join(repoRoot, 'tests', 'render-regression', 'render-visual-mathjax-v1.json'),
    outputFixture: path.join(repoRoot, 'tests', 'ocr-baseline', 'ocr-baseline-v1.json')
  };

  for (let i = 0; i < argv.length; i++) {
    const token = argv[i];
    if (token === '--sourceFixture') {
      args.sourceFixture = resolvePath(argv[++i]);
      continue;
    }
    if (token === '--outputFixture') {
      args.outputFixture = resolvePath(argv[++i]);
      continue;
    }

    throw new Error(`Unknown argument: ${token}`);
  }

  return args;
}

function normalizeSuite(value) {
  if (Array.isArray(value)) {
    return value.map((item) => String(item || '').trim().toLowerCase()).filter(Boolean);
  }

  const normalized = String(value || '').trim().toLowerCase();
  if (!normalized) {
    return ['full'];
  }

  return [normalized];
}

function toPosix(relPath) {
  return relPath.replace(/\\/g, '/');
}

function run() {
  const args = parseArgs(process.argv.slice(2));
  if (!fs.existsSync(args.sourceFixture)) {
    throw new Error(`Source fixture not found: ${args.sourceFixture}`);
  }

  const sourceFixture = readJson(args.sourceFixture);
  const sourceDir = path.dirname(args.sourceFixture);
  const outputDir = path.dirname(args.outputFixture);
  const baselineImageDir = path.join(sourceDir, 'baseline-images');

  if (!fs.existsSync(baselineImageDir)) {
    throw new Error(`Baseline image directory not found: ${baselineImageDir}`);
  }

  const sourceCases = Array.isArray(sourceFixture.cases) ? sourceFixture.cases : [];
  const outputCases = [];
  const skippedCases = [];

  for (const sourceCase of sourceCases) {
    const id = String(sourceCase?.id || '').trim();
    const latex = String(sourceCase?.latex || '').trim();
    const expected = sourceCase?.expected || {};

    if (!id || !latex) {
      skippedCases.push({ id, reason: 'missing id or latex' });
      continue;
    }

    if (expected && expected.errorContains) {
      skippedCases.push({ id, reason: 'expected render error case' });
      continue;
    }

    const imageAbsPath = path.join(baselineImageDir, `${id}.png`);
    if (!fs.existsSync(imageAbsPath)) {
      skippedCases.push({ id, reason: 'baseline image missing' });
      continue;
    }

    outputCases.push({
      id,
      suite: normalizeSuite(sourceCase.suite),
      imagePath: toPosix(path.relative(outputDir, imageAbsPath)),
      latex
    });
  }

  const outputFixture = {
    meta: {
      name: 'OCR baseline known pairs',
      version: 1,
      generatedAt: new Date().toISOString(),
      sourceFixture: toPosix(path.relative(repoRoot, args.sourceFixture)),
      mathjaxVersion: sourceFixture?.meta?.mathjaxVersion || ''
    },
    defaults: {
      ocrOptions: {
        maxTokens: 256,
        timeoutMs: 20000
      },
      passCriteria: {
        requireExact: false,
        maxCer: 0.35,
        minPassRatio: 0.65
      }
    },
    cases: outputCases
  };

  writeJson(args.outputFixture, outputFixture);

  console.log(`OCR baseline fixture written: ${path.relative(repoRoot, args.outputFixture)}`);
  console.log(`Included cases: ${outputCases.length}`);
  console.log(`Skipped cases: ${skippedCases.length}`);
  if (skippedCases.length > 0) {
    console.log(`Skipped details: ${JSON.stringify(skippedCases, null, 2)}`);
  }
}

run();
