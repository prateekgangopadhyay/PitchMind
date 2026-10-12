#!/usr/bin/env node
/** Validate all Engineering Council skills and 59 story routes at a repository checkout.
 * Standard Node 22 API only; pure read-only metadata checks (no cloud deployment).
 */
import assert from 'node:assert/strict';
import { readFileSync, existsSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (p) => readFileSync(path.join(root, p), 'utf8');
const parse = (p) => JSON.parse(read(p));
const epic = /^## EP-(\d\d) — (.+)$/gm;
const story = /^### (EP\d\d-S\d\d) — (.+)$/gm;

function acyclic(items) {
  const map = new Map(items.map(x => [x.storyId, x]));
  const visited = new Set(), stack = new Set();
  function visit(id) {
    if (stack.has(id)) throw new Error('Story dependency cycle at ' + id);
    if (visited.has(id)) return;
    const item = map.get(id);
    assert.ok(item, 'Unknown story dependency ' + id);
    stack.add(id);
    for (const dependency of item.dependsOn) visit(dependency);
    stack.delete(id); visited.add(id);
  }
  for (const item of items) visit(item.storyId);
  return visited.size;
}

function assertGateFailureProbe(pipeline) {
  assert.match(pipeline, /- name: runGateFailureProbe\s+type: boolean\s+default: false/,
    'Deliberate failure probe must be explicitly opt-in and default off');
  const start = pipeline.indexOf('  - job: GateFailureProbe');
  const end = pipeline.indexOf('  - job: Frontend', start);
  assert.ok(start >= 0 && end > start, 'Dedicated failure probe job must remain within Validate');
  const job = pipeline.slice(start, end);
  assert.match(job, /condition: and\(succeeded\(\), \$\{\{ eq\(parameters\.runGateFailureProbe, true\) \}\}\)/,
    'Failure probe must be opt-in and honor the pipeline succeeded() condition');
  assert.match(job, /--filter FullyQualifiedName~DeliberateGateFailureProbe/,
    'Failure probe must execute its dedicated deliberately failing test');
  assert.match(job, /PITCHMIND_GATE_FAILURE_PROBE:\s*'1'\s*(?:#.*)?$/m,
    'Failure probe must activate the test through its scoped environment variable');
  assert.doesNotMatch(job, /^\s*continueOnError\s*:/im,
    'Failure probe must not override Azure Pipelines failure propagation');
}

function assertStackedPrTrigger(pipeline) {
  assert.match(pipeline, /pr:\s*\n\s+branches:\s*\n\s+include:\s*\n\s+- main\s*\n\s+- codex\/\*/,
    'PR validation must include main and stacked codex/* target branches');
}

function gateFailureProbeSelfTest(pipeline) {
  assertGateFailureProbe(pipeline);
  assertStackedPrTrigger(pipeline);
  const bypassedSuccessCondition = pipeline.replace('condition: and(succeeded(),', 'condition: and(always(),');
  assert.notEqual(bypassedSuccessCondition, pipeline, 'Mutation must target the failure-probe job condition');
  const prMutations = [
    [pipeline.replace('      - codex/*', '      - feature/*'), /stacked codex/],
  ];
  for (const [mutated, expected] of prMutations) assert.throws(() => assertStackedPrTrigger(mutated), expected);
  const mutations = [
    [pipeline.replace('  default: false', '  default: true'), /opt-in/],
    [pipeline.replace('parameters.runGateFailureProbe, true', 'parameters.runGateFailureProbe, false'), /opt-in/],
    [bypassedSuccessCondition, /pipeline succeeded/],
    [pipeline.replace('FullyQualifiedName~DeliberateGateFailureProbe', 'FullyQualifiedName~UnrelatedTest'), /dedicated deliberately failing test/],
    [pipeline.replace("PITCHMIND_GATE_FAILURE_PROBE: '1'", "PITCHMIND_GATE_FAILURE_PROBE: '0'"), /scoped environment variable/],
    [pipeline.replace("PITCHMIND_GATE_FAILURE_PROBE: '1'", "PITCHMIND_GATE_FAILURE_PROBE: '10'"), /scoped environment variable/],
    [pipeline.replace('      env:\n        PITCHMIND_GATE_FAILURE_PROBE', '      continueOnError: true\n      env:\n        PITCHMIND_GATE_FAILURE_PROBE'), /override Azure Pipelines failure propagation/],
    [pipeline.replace('      env:\n        PITCHMIND_GATE_FAILURE_PROBE', '      continueOnError: $(probeMayContinueOnError)\n      env:\n        PITCHMIND_GATE_FAILURE_PROBE'), /override Azure Pipelines failure propagation/],
  ];
  for (const [mutated, expected] of mutations) assert.throws(() => assertGateFailureProbe(mutated), expected);
}

function check() {
  const reg = parse('docs/operating-model/COUNCIL-SKILL-REGISTRY.json');
  const design = parse('docs/design/DESIGN-SKILL-REGISTRY.json');
  const plan = parse('docs/operating-model/STORY-EXECUTION-ROUTING.json');
  const backlog = read('docs/backlog/HACKATHON-MVP-EPICS.md');
  const stories = [...backlog.matchAll(story)];
  const epics = [...backlog.matchAll(epic)];
  assert.equal(epics.length, 12, 'Exactly 12 source epics required');
  assert.equal(stories.length, 59, 'Exactly 59 authoritative backlog stories required');
  assert.equal(reg.roles.length, 28, 'Exactly 28 Council roles required');
  assert.equal(design.councilRoleCount, 28, 'Design skills are not accountable Council roles');
  assert.equal(design.skillCount, 3, 'Exactly three design skill sources are required');
  assert.equal(design.skills.length, 3, 'Exactly three design skill entries are required');
  assert.deepEqual(design.skills.map(x=>x.name).sort(), ['awesome-design-audit','design-taste-frontend','web-design-guidelines'],
    'All three named design specialties must be registered');
  assert.equal(new Set(reg.roles.map(x => x.name)).size, 28, 'Duplicate role');
  assert.equal(plan.stories.length, 59, 'Story routes missing');
  assert.equal(new Set(plan.stories.map(x => x.storyId)).size, 59, 'Duplicate routed story');

  const allSlugs = readdirSync(path.join(root, '.github/skills'), {withFileTypes: true})
    .filter(x => x.isDirectory()).map(x => x.name);
  const designSlugs = design.skills.map(x => x.name);
  const councilSlugs = reg.roles.map(x => x.name);
  assert.equal(new Set([...councilSlugs,...designSlugs]).size,31,'Skill names must be unique across groups');
  assert.deepEqual(allSlugs.sort(), [...councilSlugs,...designSlugs].sort(),
    'Disk skills must match the 28 Council roles plus 3 design skills');
  const seen = new Set();
  for (const role of reg.roles) {
    const file = role.skillFile;
    assert.equal(file, `.github/skills/${role.name}/SKILL.md`);
    assert.ok(existsSync(path.join(root, file)), 'Missing skill ' + file);
    const content = read(file);
    const meta = content.match(/^---\r?\n([\s\S]*?)\r?\n---/);
    assert.ok(meta, 'Missing frontmatter ' + file);
    assert.ok(new RegExp(`^name:\\s*["']?${role.name}["']?\\s*$`, 'm').test(meta[1]), 'Invalid skill name ' + file);
    assert.ok(/^description:\s*.+$/m.test(meta[1]), 'Missing skill description ' + file);
    assert.ok(content.includes('## Role-specific story gate and executable evidence'), 'Role-specific proof missing ' + file);
    assert.ok(content.includes('DEV'), 'Owner approval rule absent ' + file);
    seen.add(role.name);
  }
  for (const skill of design.skills) {
    assert.equal(skill.path, `.github/skills/${skill.name}/SKILL.md`, 'Design skill path mismatch');
    assert.ok(existsSync(path.join(root,skill.path)), 'Missing design skill '+skill.path);
    const content=read(skill.path);
    const meta=content.match(/^---\r?\n([\s\S]*?)\r?\n---/);
    assert.ok(meta,'Missing design skill frontmatter '+skill.path);
    assert.ok(new RegExp(`^name:\\s*["']?${skill.name}["']?\\s*$`, 'm').test(meta[1]),
      'Design skill frontmatter name mismatch '+skill.path);
    assert.ok(/^description:\s*.+$/m.test(meta[1]),
      'Design skill description missing '+skill.name);
    assert.ok(skill.source?.repo && /^[0-9a-f]{40}$/.test(skill.source.commit),
      'Pinned upstream version missing '+skill.name);
    if(skill.rubrics) {
      assert.equal(skill.rubrics.length,3,'Awesome Design must provide three rubrics');
      for(const file of skill.rubrics) assert.ok(existsSync(path.join(root,file)), 'Missing design rubric '+file);
    }
    if(skill.instrument) assert.ok(existsSync(path.join(root,skill.instrument)),
      'Missing design audit instrument '+skill.instrument);
  }
  for (const file of ['docs/design/PITCHMIND-DESIGN-SYSTEM.md',
    'docs/design/FRONTEND-DESIGN-DELIVERY.md','docs/design/VENDOR-SOURCES.md',
    'docs/design/vendor/taste-skill-LICENSE.txt','scripts/test_design_skills.mjs']) {
    assert.ok(existsSync(path.join(root,file)), 'Missing design governance or test '+file);
  }
  for (const role of ['ui-ux-engineer','lead-frontend-engineer','accessibility-engineer','qa-architect']) {
    const text=read(`.github/skills/${role}/SKILL.md`);
    for(const name of designSlugs) assert.ok(text.includes(name),
      'Role '+role+' is missing required design specialist '+name);
  }
  const sourceTitles = new Map(stories.map(x => [x[1], x[2]]));
  for (const row of plan.stories) {
    assert.equal(row.title, sourceTitles.get(row.storyId), 'Story title drift '+row.storyId);
    assert.ok(seen.has(row.accountableSkill), 'Unknown accountable skill '+row.storyId);
    assert.ok(row.consultSkills.length >= 2 && row.consultSkills.every(x=>seen.has(x)), 'Unrouted consult '+row.storyId);
    assert.ok(existsSync(path.join(root,row.workPacket)), 'Missing workpacket '+row.storyId);
    assert.equal(row.status,'unverified','Do not pre-accept stories '+row.storyId);
    assert.equal(row.assignedHuman,null,'Do not fake human assignment '+row.storyId);
    assert.equal(row.requiresOwnerDevApproval,true,'Every story must require explicit approval if deployed to DEV '+row.storyId);
    assert.ok(row.priority === 'P0'||row.priority === 'P1'||row.priority === 'P2');
  }
  assert.equal(acyclic(plan.stories),59,'Dependency DAG not complete');
  for (const file of ['01-system-context.md','02-container.md','03-components.md','04-deployment.md']) {
    const diagram=read('docs/architecture/'+file);
    assert.ok(diagram.includes('```mermaid'),'Missing living Mermaid C4 '+file);
    assert.ok(diagram.includes('TARGET') && diagram.includes('CURRENT') || (diagram.includes('TARGET')&&file.startsWith('01-')),'Current-vs-target ambiguity '+file);
  }
  assert.ok(existsSync(path.join(root,'docs/data/MANU-LIV-SOURCE-PROFILE.md')), 'Missing source profiling');
  assert.ok(existsSync(path.join(root,'data/simulations/manu-liv-match-dataset.json')), 'Actual JSON not found');
  const pipeline=read('pipelines/azure-pipelines.yml');
  assert.ok(pipeline.includes('default: false') && (pipeline.match(/ManualValidation@0/g)||[]).length >= 2,'Deployment approval must block ACR upload and DEV revision');
  assertGateFailureProbe(pipeline);
  assertStackedPrTrigger(pipeline);
  const counts = plan.stories.reduce((a,x)=>(a[x.priority]=(a[x.priority]||0)+1,a), {});
  assert.equal(counts.P0,49); assert.equal(counts.P1,7); assert.equal(counts.P2,3);
  return {epics:epics.length,stories:stories.length,councilSkills:reg.roles.length,designSkills:design.skills.length,discoverableSkills:allSlugs.length,priority:counts,cycles:0,approvalGates:2};
}

if (process.argv.includes('--self-test')) {
  assert.equal(acyclic([{storyId:'A',dependsOn:[]},{storyId:'B',dependsOn:['A']}]),2);
  assert.throws(()=>acyclic([{storyId:'A',dependsOn:['B']},{storyId:'B',dependsOn:['A']}]),/cycle/);
  assert.throws(()=>acyclic([{storyId:'A',dependsOn:['UNKNOWN']}]),/Unknown/);
  gateFailureProbeSelfTest(read('pipelines/azure-pipelines.yml'));
  console.log('PASS: dependency DAG, stacked PR trigger, and deliberate gate-failure probe self-tests');
} else {
  try { console.log('PASS: Council skill/story/architecture/approval contract',JSON.stringify(check())); }
  catch(error) { console.error('FAIL:',error.message); process.exitCode=1; }
}
