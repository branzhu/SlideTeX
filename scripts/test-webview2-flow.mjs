#!/usr/bin/env node
// SlideTeX Note: Orchestrates the WebView2 integration test (Tier 2).
// Builds the C# test project, runs the exe, and parses JSON results.

import { execSync, spawn } from 'node:child_process';
import path from 'node:path';
import fs from 'node:fs';
import { writeJson, ensureDir } from './lib/test-infra.mjs';

const ROOT = path.resolve(import.meta.dirname, '..');
const PROJECT = path.join(ROOT, 'src', 'SlideTeX.WebView2Test');
const CSPROJ = path.join(PROJECT, 'SlideTeX.WebView2Test.csproj');
const ARTIFACT_DIR = path.join(ROOT, 'artifacts', 'webview2-flow');
const TIMEOUT_MS = 30_000;

function log(msg) {
  console.log(`[webview2-test] ${msg}`);
}

function fail(msg) {
  console.error(`[webview2-test] FAIL: ${msg}`);
  process.exitCode = 1;
}

async function main() {
  ensureDir(ARTIFACT_DIR);

  if (!fs.existsSync(CSPROJ)) {
    fail(`Project not found: ${CSPROJ}`);
    return;
  }

  // Build
  log('Building SlideTeX.WebView2Test...');
  try {
    execSync(`dotnet build "${CSPROJ}" -c Release -v q`, {
      stdio: ['ignore', 'pipe', 'pipe'],
      cwd: ROOT,
      timeout: 60_000
    });
  } catch (err) {
    fail('dotnet build failed:\n' + (err.stderr?.toString() || err.message));
    return;
  }
  log('Build succeeded.');

  // Locate exe
  const exePath = path.join(PROJECT, 'bin', 'Release', 'net48', 'SlideTeX.WebView2Test.exe');
  if (!fs.existsSync(exePath)) {
    fail(`Exe not found at: ${exePath}`);
    return;
  }

  // Run
  log('Running WebView2 integration test...');
  const result = await runExe(exePath);

  if (result.stderr) {
    console.log(result.stderr);
  }

  if (result.error) {
    fail(result.error);
    writeJson(path.join(ARTIFACT_DIR, 'report.json'), { error: result.error });
    return;
  }

  // Parse JSON output
  let report;
  try {
    report = JSON.parse(result.stdout);
  } catch {
    fail('Failed to parse JSON output:\n' + result.stdout);
    writeJson(path.join(ARTIFACT_DIR, 'report.json'), { error: 'Invalid JSON', raw: result.stdout });
    return;
  }

  writeJson(path.join(ARTIFACT_DIR, 'report.json'), report);

  // Print results
  for (const r of report.results || []) {
    const icon = r.pass ? 'PASS' : 'FAIL';
    const errSuffix = r.error ? ` — ${r.error}` : '';
    console.log(`  ${icon}: ${r.name}${errSuffix}`);
  }

  log(`${report.passed} passed, ${report.failed} failed`);

  if (report.failed > 0 || result.exitCode !== 0) {
    process.exitCode = 1;
  }
}

function runExe(exePath) {
  return new Promise((resolve) => {
    let stdout = '';
    let stderr = '';
    let settled = false;

    const child = spawn(exePath, [], {
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true
    });

    child.stdout.on('data', (d) => { stdout += d.toString(); });
    child.stderr.on('data', (d) => { stderr += d.toString(); });

    const timer = setTimeout(() => {
      if (!settled) {
        settled = true;
        child.kill();
        resolve({ error: `Timeout after ${TIMEOUT_MS}ms`, stdout, stderr, exitCode: 1 });
      }
    }, TIMEOUT_MS);

    child.on('close', (code) => {
      clearTimeout(timer);
      if (!settled) {
        settled = true;
        resolve({ stdout: stdout.trim(), stderr, exitCode: code ?? 1, error: null });
      }
    });

    child.on('error', (err) => {
      clearTimeout(timer);
      if (!settled) {
        settled = true;
        const msg = err.message.includes('ENOENT')
          ? 'WebView2 Runtime not found or exe missing. Install the Evergreen WebView2 Runtime.'
          : err.message;
        resolve({ error: msg, stdout, stderr, exitCode: 1 });
      }
    });
  });
}

main();

