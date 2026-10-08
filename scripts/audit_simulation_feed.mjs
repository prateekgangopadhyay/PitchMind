#!/usr/bin/env node
/**
 * PitchMind feed preflight: analyze the exact supplied match JSON without changing it.
 * Prints aggregate metadata and quality warnings only. No player names or biometrics.
 * This is a structural acceptance aid; rights, pitch units and football plausibility
 * require a human-approved versioned manifest before canonical import or publication.
 */
import assert from 'node:assert/strict';
import { readFileSync, existsSync, statSync } from 'node:fs';
import { createHash } from 'node:crypto';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
const names = ['eventId','matchId','sequence','clockSeconds','period','eventType'];
const MAX_BYTES = 50 * 1024 * 1024;

function inspect(records, sourceHash='test-only-hash') {
  assert.ok(Array.isArray(records) && records.length > 0, 'Feed must be a nonempty event array');
  const ids = new Set(), matches = new Set(), counts = new Map();
  let firstSequence = null, previousSequence = null, previousClock = -1, lastClock = 0;
  let playerObservations = 0, frames = 0, containsBiometricLikeValues = false;
  const rawCoordinateBounds = {min:null,max:null,count:0};
  const inspectNumeric = value => {
    if (value === null || value === undefined) return;
    assert.ok(typeof value === 'number' && Number.isFinite(value), 'Invalid numeric pitch coordinate');
    rawCoordinateBounds.count++;
    rawCoordinateBounds.min = rawCoordinateBounds.min === null ? value : Math.min(rawCoordinateBounds.min,value);
    rawCoordinateBounds.max = rawCoordinateBounds.max === null ? value : Math.max(rawCoordinateBounds.max,value);
  };
  for (const [index,event] of records.entries()) {
    assert.ok(event && typeof event === 'object' && !Array.isArray(event), 'Invalid event object at index '+index);
    for (const field of names) assert.ok(Object.hasOwn(event,field), 'Missing '+field+' at source index '+index);
    assert.ok(typeof event.eventId==='string' && event.eventId.length>0,'Invalid source eventId');
    assert.ok(!ids.has(event.eventId),'Duplicate source eventId at index '+index);
    ids.add(event.eventId);
    assert.ok(typeof event.matchId==='string' && event.matchId.length>0,'Missing matchId');
    matches.add(event.matchId);
    assert.ok(Number.isSafeInteger(event.sequence) && event.sequence>=0,'Invalid sequence');
    if(firstSequence===null)firstSequence=event.sequence;
    if(previousSequence!==null)assert.equal(event.sequence,previousSequence+1,'Sequence gap/conflict at source index '+index);
    previousSequence=event.sequence;
    assert.ok(Number.isFinite(event.clockSeconds)&&event.clockSeconds>=previousClock,'Backwards match clock at source index '+index);
    previousClock=event.clockSeconds;lastClock=event.clockSeconds;
    assert.ok(Number.isInteger(event.period)&&event.period>=1,'Invalid period');
    assert.ok(typeof event.eventType==='string'&&event.eventType.length>0,'Invalid event type');
    counts.set(event.eventType,(counts.get(event.eventType)||0)+1);
    for (const field of ['pitchX','pitchY','endX','endY','ballX','ballY']) inspectNumeric(event[field]);
    if (event.playerPositions!==undefined && event.playerPositions!==null) {
      assert.ok(Array.isArray(event.playerPositions),'playerPositions must be array');
      frames++;playerObservations+=event.playerPositions.length;
      for(const p of event.playerPositions) {
        assert.ok(p && typeof p==='object'&&!Array.isArray(p),'Invalid player position');
        inspectNumeric(p.x);inspectNumeric(p.y);
        if(Object.hasOwn(p,'heartRate')||Object.hasOwn(p,'stamina')||Object.hasOwn(p,'heatIndex'))containsBiometricLikeValues=true;
      }
    }
  }
  assert.equal(matches.size,1,'Source JSON mixes match IDs; expected one fixture');
  const warnings=[
    'Structural profile only: synthetic origin/distribution rights have not been verified by this tool.',
    'Source pitch coordinate unit, orientation and halftime transform require human manifest approval before metre-space analytics.',
    'Event-attached playerPositions snapshots must not be treated as dense optical tracking or reliable velocities.',
    'Raw game and player metrics are not real Premier League match observations and must be labeled simulated.'
  ];
  if (containsBiometricLikeValues) warnings.push('Biometric-like source attributes require rights/privacy review; do not publish player-level heartRate or stamina as fact.');
  const shots=counts.get('Shot')||0, fouls=counts.get('Foul')||0;
  if (shots > 50 || fouls > 100) warnings.push('Unusually high shot/foul frequency; football Product Owner realism signoff required (no silent event deletion).');
  return {
    state:'STRUCTURAL_ONLY_NOT_FOOTBALL_APPROVED',sha256:sourceHash,
    eventCount:records.length,matchCount:matches.size,sourceSequence:{start:firstSequence,end:previousSequence},
    clockSeconds:{last:lastClock},eventTypeCounts:Object.fromEntries([...counts].sort((a,b)=>a[0].localeCompare(b[0]))),
    eventAttachedFrameCount:frames,playerObservationCount:playerObservations,
    rawCoordinateBounds:{...rawCoordinateBounds,unit:'unknown'},hasBiometricLikeAttributes:containsBiometricLikeValues,
    warnings
  };
}
function checkApproval(report, manifestPath) {
  if(!manifestPath || !existsSync(manifestPath))throw new Error('Approved provenance/rights/units manifest absent; canonical admission BLOCKED');
  const m=JSON.parse(readFileSync(manifestPath,'utf8'));
  assert.equal(m.sourceSha256,report.sha256,'Manifest source hash mismatch');
  assert.equal(m.syntheticProvenanceReviewed,true,'Synthetic provenance not reviewed');
  assert.equal(m.rightsApproved,true,'Distribution rights not approved');
  assert.ok(['percentage','metres'].includes(m.coordinateUnit),'Unknown coordinate units');
  assert.ok(m.pitch && Number.isFinite(m.pitch.lengthMetres)&&Number.isFinite(m.pitch.widthMetres),'Pitch size not approved');
  assert.ok(typeof m.approvedBy==='string' && m.approvedBy.trim().length>0,'Named human approver missing');
  return true;
}
function parseArgs(argv) {
  const result={file:path.join(root,'data/simulations/manu-liv-match-dataset.json'),manifest:null,requireApproval:false,selfTest:false};
  for(let i=0;i<argv.length;i++){
    if(argv[i]==='--file')result.file=path.resolve(argv[++i]);
    else if(argv[i]==='--manifest')result.manifest=path.resolve(argv[++i]);
    else if(argv[i]==='--require-approval')result.requireApproval=true;
    else if(argv[i]==='--self-test')result.selfTest=true;
    else throw new Error('Unknown argument '+argv[i]);
  }
  return result;
}
function test() {
  const e=(id,n,clock)=>({eventId:id,matchId:'test-match',sequence:n,clockSeconds:clock,period:1,eventType:n?'Goal':'KickOff',playerPositions:[{x:2,y:98,heartRate:70}]});
  const data=[e('a',1,0),e('b',2,3)];
  const r=inspect(data); assert.equal(r.eventCount,2);assert.equal(r.eventTypeCounts.Goal,1);assert.ok(r.hasBiometricLikeAttributes);
  assert.throws(()=>inspect([data[0],data[0]]),/Duplicate/);
  assert.throws(()=>inspect([data[0],e('b',3,3)]),/gap/);
  assert.throws(()=>inspect([data[0],e('b',2,-1)]),/Backwards/);
  assert.throws(()=>checkApproval(r,null),/manifest absent/);
  console.log('PASS: feed audit validator self-test (not source acceptance)');
}
try {
  const options=parseArgs(process.argv.slice(2));
  if(options.selfTest)test();
  else {
    if(!existsSync(options.file))throw new Error('Source JSON missing: '+options.file);
    const size=statSync(options.file).size;
    assert.ok(size>0&&size<=MAX_BYTES,'Source exceeds read-only maximum 50MiB or empty');
    const bytes=readFileSync(options.file);
    const records=JSON.parse(bytes.toString('utf8'));
    const report=inspect(records,sha256(bytes));
    if(options.requireApproval){checkApproval(report,options.manifest);report.state='STRUCTURAL_PLUS_MANIFEST_REVIEWED';}
    console.log(JSON.stringify(report,null,2));
  }
} catch(error){console.error('BLOCKED: '+error.message);process.exitCode=2;}
