#!/usr/bin/env node
// Copy lint — deterministic pre-humanizer gate. Zero dependencies.
// Usage: node slop-scan.mjs <file.html|file.md|file.txt> [--json]
// Exit 1 on hard fails, 0 otherwise. Strips HTML tags before scanning.

import { readFileSync } from "node:fs";

const HARD_WORDS = [
  "world-class","game-changing","seamless","seamlessly","revolutionary","transformative",
  "cutting-edge","state-of-the-art","next-level","supercharge","game changer","unleash",
  "unlock the power","elevate your","empower your","delve","synergy","paradigm",
];
const SOFT_WORDS = [
  "leverage","robust","landscape","crucial","pivotal","holistic","streamline","elevate",
  "unlock","harness the (power|potential|full)","in the realm of","navigate the","dive into","deep dive",
  "best-in-class","turbocharge","effortlessly","innovative","groundbreaking",
];
const HEDGES = ["may help","can potentially","aims to","strives to","is designed to help","could enable"];
const OPENERS = [
  /^in today'?s (rapidly |ever-)?(evolving|changing|digital|fast-paced)/im,
  /^in an era (of|where)/im,
  /^in the (rapidly |ever-)?evolving (landscape|world) of/im,
  /^(now more than ever|as technology advances)/im,
];

const argPath = process.argv[2];
if (!argPath) { console.error("usage: node slop-scan.mjs <file> [--json]"); process.exit(2); }
const raw = readFileSync(argPath, "utf8");
const text = raw
  .replace(/<script[\s\S]*?<\/script>/gi, " ")
  .replace(/<style[\s\S]*?<\/style>/gi, " ")
  .replace(/<[^>]+>/g, " ")
  .replace(/&[a-z]+;/gi, " ")
  .replace(/\s+/g, " ");
const words = text.split(" ").filter(Boolean).length || 1;

const found = (list) => list
  .map((w) => {
    const isRe = /[(|)]/.test(w);
    const pat = isRe ? w : w.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    return { w, n: (text.match(new RegExp(pat, "gi")) || []).length };
  })
  .filter((x) => x.n > 0);

const hard = found(HARD_WORDS);
const soft = found(SOFT_WORDS);
const hedges = found(HEDGES);
const openers = OPENERS.filter((re) => re.test(text)).length;

const contrastFormula = (text.match(/\b\w[\w' ]{0,24}, not [a-z][\w' ]{0,24}\b/gi) || [])
  .filter((m) => !/\b(do|does|did|is|are|was|were|will|would|should|could|can|must|need) not\b/i.test(m));

const emDashJoins = (text.match(/ — /g) || []).length;
const emDashRate = +(emDashJoins * 1000 / words).toFixed(1);

const triads = (text.match(/\b\w+, \w+, and \w+\b/g) || []).length;
const triadRate = +(triads * 1000 / words).toFixed(1);

const hardFail = hard.length > 0 || contrastFormula.length > 0 || openers > 0;
const warnings = [];
if (soft.length) warnings.push({ rule: "soft-intensifiers", hits: soft });
if (hedges.length) warnings.push({ rule: "hedge-stack", hits: hedges });
if (emDashRate > 6) warnings.push({ rule: "em-dash-density", per1000: emDashRate, note: "check for clause-joining dashes; list-convention dashes are fine" });
if (triadRate > 8) warnings.push({ rule: "triad-rhythm", per1000: triadRate });

const report = {
  file: argPath, words,
  verdict: hardFail ? "FAIL" : warnings.length ? "PASS_WITH_WARNINGS" : "PASS",
  hardFails: {
    bannedIntensifiers: hard,
    contrastFormula_X_not_Y: contrastFormula,
    throatClearingOpeners: openers,
  },
  warnings,
};

console.log(JSON.stringify(report, null, 2));
process.exit(hardFail ? 1 : 0);
