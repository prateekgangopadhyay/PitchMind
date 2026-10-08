#!/usr/bin/env node
/** Networkless regression tests for vendored Awesome Design copy-check instrument.
 * This is NOT a browser/a11y/visual UI test or an upstream-freshness guarantee.
 */
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, rmSync, readFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const instrument = path.join(root, 'scripts/design-slop-scan.mjs');
const registry = JSON.parse(readFileSync(path.join(root,'docs/design/DESIGN-SKILL-REGISTRY.json'),'utf8'));
assert.equal(registry.skills.length,3, 'Need Taste, Vercel and Awesome');
const temp = mkdtempSync(path.join(os.tmpdir(),'pitchmind-design-skill-check-'));
try {
  const good = path.join(temp,'good.md');
  const bad = path.join(temp,'bad.md');
  writeFileSync(good,'A match insight links to a recorded event and its evidence.\n');
  writeFileSync(bad,'Seamless revolutionary solutions make every match world-class.\n');
  function run(file) {
    const result = spawnSync(process.execPath,[instrument,file],{encoding:'utf8',timeout:10000});
    assert.equal(result.error,undefined, 'Copy instrument failed to launch');
    assert.ok(result.stdout.trim(),'Copy instrument must emit a JSON verdict');
    return {status:result.status, report:JSON.parse(result.stdout)};
  }
  const ok=run(good),fail=run(bad);
  assert.equal(ok.status,0,'Grounded match copy should pass');
  assert.equal(ok.report.verdict,'PASS');
  assert.equal(fail.status,1,'Generic banned language should fail');
  assert.equal(fail.report.verdict,'FAIL');
  assert.ok(fail.report.hardFails.bannedIntensifiers.length>0,'Expected copy risk findings');
  console.log('PASS: Taste, Vercel and Awesome design registry + Awesome copy instrument self-test');
} finally {
  rmSync(temp,{recursive:true,force:true});
}
