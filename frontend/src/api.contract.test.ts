import { describe, expect, it } from 'vitest';
import type { Insight, KeyMoment, MechanismReplay, Snapshot } from './api';

type Assert<T extends true> = T;
type IsRequired<T, K extends keyof T> = object extends Pick<T, K> ? false : true;
const requiredContractFields: [
  Assert<IsRequired<Snapshot, 'competitionId'>>,
  Assert<IsRequired<Snapshot, 'competitionRulesVersion'>>,
  Assert<IsRequired<Snapshot, 'maximumSubstituteReentriesPerPlayer'>>,
  Assert<IsRequired<Insight, 'sourceSha256'>>,
  Assert<IsRequired<KeyMoment, 'sourceSha256'>>,
  Assert<IsRequired<MechanismReplay, 'sourceSha256'>>,
] = [true, true, true, true, true, true];
void requiredContractFields;

describe('versioned API DTO field parity', () => {
  it('keeps optional competition context explicit on match snapshots', () => {
    const competition: Pick<Snapshot,
      'competitionId' | 'competitionRulesVersion' | 'maximumSubstituteReentriesPerPlayer'> = {
      competitionId: null,
      competitionRulesVersion: null,
      maximumSubstituteReentriesPerPlayer: null
    };

    expect(competition).toEqual({
      competitionId: null,
      competitionRulesVersion: null,
      maximumSubstituteReentriesPerPlayer: null
    });
  });

  it('requires source hashes on insight, key-moment, and replay contracts', () => {
    const hash = 'a'.repeat(64);
    const insight: Pick<Insight, 'sourceSha256'> = { sourceSha256: hash };
    const keyMoment: Pick<KeyMoment, 'sourceSha256'> = { sourceSha256: hash };
    const replay: Pick<MechanismReplay, 'sourceSha256'> = { sourceSha256: hash };

    expect([insight.sourceSha256, keyMoment.sourceSha256, replay.sourceSha256]).toEqual([hash, hash, hash]);
  });
});
