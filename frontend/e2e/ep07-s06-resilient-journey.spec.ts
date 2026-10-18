import { expect, test, type Page, type Route } from '@playwright/test';

// These are deliberately synthetic, test-only canonical API responses. They do
// not describe a real match or certify the candidate feed's unresolved units.
const matchId = '33333333-3333-4333-8333-333333333333';
const selectedSeconds = 3309;
const canonicalRevision = 4;
const throughSequence = 19;
const sourceSha256 = 'c'.repeat(64);
const insightSummary = 'Recorded sequence is available for review.';

type StubOptions = { unavailable?: string[]; withEvidence?: boolean; withReplay?: boolean; withTracking?: boolean; trackingRightsStatus?: string };

function trackingFrame(selectedTimeSeconds: number, rightsReviewStatus = 'approved_for_use') {
  return {
    frameId: 'test-tracking-frame', sequence: 18, canonicalRevision, projectionRevision: 4, period: 2,
    clockSeconds: selectedTimeSeconds, coordinateVersion: 'coordinates-test-v1', sourceSha256, quality: null,
    frame: {
      schemaVersion: 1, matchId, frameId: 'test-tracking-frame', sequence: 18, canonicalRevision,
      matchTime: { period: 2, elapsedSeconds: selectedTimeSeconds, sourceClockValue: selectedTimeSeconds,
        sourceClockUnit: 'seconds', sourceClockBasis: 'match_elapsed' },
      coordinateSystem: { lengthMetres: 105, widthMetres: 68, unit: 'metres', axisOrigin: 'bottom_left',
        teamDirections: [{ teamId: 'test-home', direction: 'right' }, { teamId: 'test-away', direction: 'left' }] },
      ball: { x: 52, y: 34 }, players: [
        { playerId: 'test-player-home', teamId: 'test-home', position: { x: 30, y: 20 } },
        { playerId: 'test-player-away', teamId: 'test-away', position: { x: 71, y: 42 } }
      ],
      source: { providerId: 'synthetic-test', sourceMatchId: 'test-source-match', sourceRecordId: 'test-tracking-frame',
        sourceSequence: 18, sourceSchemaVersion: 'tracking-test-v1', sourceSha256,
        provenanceStatus: 'synthetic-test-only', rightsReviewStatus }
    }
  };
}

function replayEvidenceItems(selectedTimeSeconds: number) {
  const references = [
    { kind: 'event', id: 'test-event-17', relation: 'supports' },
    { kind: 'frame', id: 'test-frame-18', relation: 'illustrates' },
    { kind: 'frame', id: 'test-frame-19', relation: 'illustrates' }
  ];
  const event = {
    kind: 'event', id: 'test-event-17', relation: 'supports', sourceCanonicalRevision: canonicalRevision,
    sequence: 17, clockSeconds: 3295, period: 2,
    payload: { schemaVersion: 1, matchId, canonicalRevision, eventId: 'test-event-17', sequence: 17,
      matchTime: { period: 2, elapsedSeconds: 3295 },
      source: { sourceSha256, provenanceStatus: 'synthetic-test-only', rightsReviewStatus: 'approved_for_use' },
      playerId: 'test-player-home', relatedPlayerId: null }
  };
  const frames = [18, 19].map((sequence, index) => {
    const clockSeconds = 3300 + index;
    const frameId = `test-frame-${sequence}`;
    return { kind: 'frame', id: frameId, relation: 'illustrates', sourceCanonicalRevision: canonicalRevision,
      sequence, clockSeconds, period: 2,
      payload: { schemaVersion: 1, matchId, frameId, sequence, canonicalRevision,
        matchTime: { period: 2, elapsedSeconds: clockSeconds, sourceClockValue: clockSeconds, sourceClockUnit: 'seconds', sourceClockBasis: 'match_elapsed' },
        coordinateSystem: { lengthMetres: 105, widthMetres: 68, unit: 'metres', axisOrigin: 'bottom_left',
          teamDirections: [{ teamId: 'test-home', direction: 'right' }, { teamId: 'test-away', direction: 'left' }] },
        ball: { x: 52 + index, y: 34 },
        players: [
          { playerId: 'test-player-home', teamId: 'test-home', position: { x: 30 + index, y: 20 } },
          { playerId: 'test-player-away', teamId: 'test-away', position: { x: 71, y: 42 } }
        ],
        source: { providerId: 'synthetic-test', sourceMatchId: 'test-source-match', sourceRecordId: frameId,
          sourceSequence: sequence, sourceSchemaVersion: 'tracking-test-v1', sourceSha256,
          provenanceStatus: 'synthetic-test-only', rightsReviewStatus: 'approved_for_use' } }
    };
  });
  return { references, items: [event, ...frames].filter(item => item.clockSeconds <= selectedTimeSeconds) };
}

async function stubCanonicalMatch(page: Page, options: StubOptions = {}) {
  await page.route('**/api/v1/simulation/candidate/status', route => route.fulfill({
    status: 200, contentType: 'application/json',
    body: JSON.stringify({ matchId: null, isStreaming: false, currentSequence: null, currentClockValue: null,
      period: null, sourceSha256: null, sourceStatus: 'candidate_not_canonical', coordinateStatus: 'unverified', playerMetricsStatus: 'unvalidated' })
  }));
  await page.route('**/api/v1/canonical/matches/**', async (route: Route) => {
    const url = new URL(route.request().url());
    const resource = url.pathname.split('/').at(-1);
    const atSeconds = Number(url.searchParams.get('atSeconds'));
    const selectedTimeSeconds = Number.isInteger(atSeconds) ? atSeconds : selectedSeconds;
    const context = { matchId, canonicalRevision, projectionRevision: 4, selectedTimeSeconds, throughSequence };
    if (options.unavailable?.includes(resource ?? '')) {
      await route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({
        status: 503, title: 'Service unavailable', code: 'CANONICAL_READ_UNAVAILABLE', correlationId: 'test-correlation'
      }) });
      return;
    }

    if (url.pathname.includes('/evidence/')) {
      const replay = options.withReplay ? replayEvidenceItems(selectedTimeSeconds) : null;
      const evidenceInsight = {
        insightId: 'test-insight-1', ...context, sourceVersion: 'test-source-v1', provenance: 'synthetic-test-only',
        sourceSha256, detectorVersion: 'test-detector-v1', truthClass: 'observed', summary: insightSummary,
        conditions: { sampleSize: 2, testOnly: true }, limitations: ['Test fixture only'],
        evidence: replay?.references ?? [{ kind: 'event', id: 'test-event-18', relation: 'supports' }]
      };
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
        insight: evidenceInsight,
        items: replay?.items ?? [{ kind: 'event', id: 'test-event-18', relation: 'supports', sourceCanonicalRevision: 4,
          sequence: 18, clockSeconds: Math.min(selectedTimeSeconds, 3300), period: 2,
          payload: { eventId: 'test-event-18' } }]
      }) });
      return;
    }

    const bodyByResource: Record<string, unknown> = {
      summary: {
        ...context, asOfSeconds: selectedTimeSeconds, algorithmVersion: 'test-projection-v1', sourceSha256,
        stateSha256: 'd'.repeat(64), provenance: 'synthetic-test-only', period: 2, phase: 'secondhalf',
        homeScore: 1, awayScore: 0, possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
      },
      events: {
        ...context, nextCursor: null, hasMore: false,
        items: [{ eventId: 'test-event-18', matchId, sequence: 18, type: 'Goal',
          clockSeconds: Math.min(selectedTimeSeconds, 3300), period: 2, occurredAt: '2026-01-01T00:55:00Z' }]
      },
      insights: {
        ...context, nextCursor: null, hasMore: false,
        items: [{ insightId: 'test-insight-1', ...context, sourceVersion: 'test-source-v1', provenance: 'synthetic-test-only',
          sourceSha256, detectorVersion: 'test-detector-v1', truthClass: 'observed', summary: insightSummary,
          conditions: { sampleSize: 2, testOnly: true }, limitations: ['Test fixture only'],
          evidence: options.withReplay ? replayEvidenceItems(selectedTimeSeconds).references
            : options.withEvidence ? [{ kind: 'event', id: 'test-event-18', relation: 'supports' }] : [] }]
      },
      frames: { ...context, items: options.withTracking ? [trackingFrame(selectedTimeSeconds, options.trackingRightsStatus)] : [], nextCursor: null, hasMore: false },
      'key-moments': { ...context, items: [], hasMore: false }
    };
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(bodyByResource[resource ?? ''] ?? {}) });
  });
}

test('steps through source-bound observed frames with keyboard controls at three widths', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await stubCanonicalMatch(page, { withReplay: true });
  await openSelectedMatch(page);
  const disclosure = page.locator('details.canonical-evidence-disclosure > summary');
  await disclosure.focus();
  await page.keyboard.press('Enter');

  const replay = page.getByRole('region', { name: 'Observed sequence replay' });
  await expect(replay).toBeVisible();
  await expect(replay.getByText('Frame 1 of 2 · 55:00')).toBeVisible();
  await expect(replay).toContainText('Team test-home · attacks toward right');
  await expect(replay).toContainText('Team test-away · attacks toward left');
  await expect(replay.getByRole('img', { name: 'Observed tracking frame at 55:00, period 2, sequence 18' })).toBeVisible();
  const replayPositions = replay.getByRole('region', { name: 'Observed replay positions' });
  await expect(replayPositions.getByRole('table', { name: 'Observed positions · frame test-frame-18 · sequence 18' })).toBeVisible();
  const replayTableSize = await replayPositions.evaluate(element => ({ client: element.clientWidth, scroll: element.scrollWidth }));
  expect(replayTableSize.scroll).toBeGreaterThan(replayTableSize.client);
  await replayPositions.focus();
  await page.keyboard.press('ArrowRight');
  expect(await replayPositions.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  const next = replay.getByRole('button', { name: 'Next observed frame' });
  await next.focus();
  await page.keyboard.press('Enter');
  await expect(replay.getByText('Frame 2 of 2 · 55:01')).toBeVisible();
  await expect(replay.getByRole('img', { name: 'Observed tracking frame at 55:01, period 2, sequence 19' })).toBeVisible();
  await expect(next).toBeDisabled();
  await expect(page.getByRole('slider', { name: 'Selected match time' })).toHaveAttribute('aria-valuetext', '55:09');
  for (const viewport of [
    { name: 'desktop', width: 1440, height: 1000 },
    { name: 'tablet', width: 768, height: 1024 },
    { name: 'mobile', width: 375, height: 812 }
  ]) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(overflow, `${viewport.name} replay should not require horizontal page scrolling`).toBe(false);
    await page.screenshot({ path: testInfo.outputPath(`ep07-s05-replay-${viewport.name}.png`), fullPage: true });
  }
});

test('keeps observed position columns readable in a keyboard-scrollable mobile table', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await stubCanonicalMatch(page, { withTracking: true });
  await openSelectedMatch(page);

  const positions = page.getByRole('region', { name: 'Observed pitch coordinates' });
  const table = positions.getByRole('table', { name: /Pitch view coordinates at/ });
  await expect(table).toBeVisible();
  await expect(table.getByRole('columnheader', { name: 'Player ID' })).toBeVisible();
  await expect(table.getByRole('cell', { name: 'test-player-away' })).toBeVisible();
  await expect(positions).toHaveAttribute('tabindex', '0');
  const dimensions = await positions.evaluate(element => ({ client: element.clientWidth, scroll: element.scrollWidth,
    rect: element.getBoundingClientRect().toJSON(), parent: element.parentElement?.getBoundingClientRect().toJSON(),
    page: document.documentElement.scrollWidth, viewport: window.innerWidth }));
  expect(dimensions.scroll, JSON.stringify(dimensions)).toBeGreaterThan(dimensions.client);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  await positions.screenshot({ path: '../docs/engineering/evidence/ep07-s05-replay/mobile-position-table-start-375x812.png' });
  await positions.focus();
  await page.keyboard.press('ArrowRight');
  expect(await positions.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
  await positions.screenshot({ path: '../docs/engineering/evidence/ep07-s05-replay/mobile-position-table-scrolled-375x812.png' });
});

test('does not display canonical pitch positions until tracking rights are approved', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await stubCanonicalMatch(page, { withTracking: true, trackingRightsStatus: 'pending' });
  await openSelectedMatch(page);

  const pitch = page.getByRole('region', { name: 'Observed pitch' });
  await expect(page.getByText('Observed tracking is unavailable until the source is approved for canonical use. Positions are not estimated.')).toBeVisible();
  await expect(pitch).toHaveCount(0);
  await expect(page.getByRole('img', { name: /Observed player and ball positions/ })).toHaveCount(0);
  await expect(page.getByRole('table', { name: /Pitch view coordinates/ })).toHaveCount(0);
  await expect(page.getByRole('region', { name: 'Match score' })).toBeVisible();
});

async function openSelectedMatch(page: Page) {
  await page.goto(`/matches/${matchId}?matchId=${matchId}&atSeconds=${selectedSeconds}&revision=${canonicalRevision}`);
  await expect(page.getByRole('heading', { name: 'Match overview' })).toBeVisible();
}

test('renders the API-backed match canvas edge to edge without mobile overflow', async ({ page }, testInfo) => {
  await stubCanonicalMatch(page, { withTracking: true, withEvidence: true });
  for (const viewport of [
    { name: 'desktop', width: 1440, height: 900 },
    { name: 'mobile', width: 375, height: 812 }
  ]) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await openSelectedMatch(page);
    await expect(page.getByRole('button', { name: /candidate live simulation/i })).toHaveCount(0);
    await expect(page.getByRole('region', { name: 'Match score' })).toContainText('55:09');
    const pitch = page.getByRole('region', { name: 'Observed pitch' });
    await expect(pitch.getByRole('img', { name: /Observed player and ball positions/ })).toBeVisible();
    await expect(pitch.getByRole('list', { name: 'Pitch marker teams' })).toContainText('test-home');
    await expect(pitch.getByRole('list', { name: 'Pitch marker teams' })).toContainText('test-away');
    await expect(page.getByRole('heading', { name: 'Recorded events' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Evidence-backed insights' })).toBeVisible();
    const widths = await page.evaluate(() => ({ viewport: window.innerWidth, document: document.documentElement.scrollWidth }));
    expect(widths.document, `match layout overflows at ${viewport.width}px`).toBeLessThanOrEqual(widths.viewport);
    await page.screenshot({ path: testInfo.outputPath(`ep07-match-api-${viewport.name}.png`), fullPage: true });
  }
});

test('audience mode changes disclosure while preserving selected-time facts', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 800 });
  await stubCanonicalMatch(page);
  await openSelectedMatch(page);
  await expect(page.getByRole('heading', { name: 'Evidence-backed insights' })).toBeVisible();

  const score = page.getByRole('region', { name: 'Match score' });
  const matchFacts = await score.innerText();
  const truthClass = page.getByLabel('Insight category: observed');
  await expect(truthClass).toBeVisible();
  const truthClassText = await truthClass.innerText();
  await expect(score).toContainText('55:09');
  await expect(score).toContainText('1');
  await expect(page.getByText(insightSummary).first()).toBeVisible();
  await expect(page.getByText('Synthetic match')).toBeVisible();

  const audience = page.getByRole('group', { name: 'Explanation detail' });
  await audience.getByRole('button', { name: 'Analyst' }).click();
  await expect(audience.getByRole('button', { name: 'Analyst' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByText('Detector test-detector-v1')).toBeVisible();
  await page.getByText('Recorded conditions and limitations').click();
  await expect(page.getByText('Test fixture only')).toBeVisible();
  expect(await score.innerText()).toBe(matchFacts);
  await expect(truthClass).toBeVisible();
  expect(await truthClass.innerText()).toBe(truthClassText);
  await expect(page.getByText(insightSummary).first()).toBeVisible();

  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  expect(overflow, 'the 320px match journey should not require horizontal page scrolling').toBe(false);
});

test('keyboard timeline selection retains the chosen event focus and time', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 800 });
  await stubCanonicalMatch(page);
  await openSelectedMatch(page);

  const time = page.getByRole('slider', { name: 'Selected match time' });
  const sliderBounds = await time.boundingBox();
  expect(sliderBounds?.height).toBeGreaterThanOrEqual(44);
  await time.focus();
  await expect(time).toHaveAttribute('aria-valuetext', '55:09');
  await time.press('ArrowRight');
  await expect(time).toHaveAttribute('aria-valuetext', '55:10');

  const event = page.getByRole('button', { name: 'Select Goal at 55:00, sequence 18' });
  await event.focus();
  await event.press('Enter');
  await expect(page.getByRole('slider', { name: 'Selected match time' })).toHaveAttribute('aria-valuetext', '55:00');
  await expect.poll(() => event.evaluate(element => document.activeElement === element)).toBe(true);
});

test('keeps canonical score available when optional reads, including AI-related insight, fail', async ({ page }) => {
  await stubCanonicalMatch(page, { unavailable: ['events', 'insights', 'frames', 'key-moments'] });
  await openSelectedMatch(page);

  await expect(page.getByRole('region', { name: 'Match score' })).toContainText('55:09');
  await expect(page.getByRole('heading', { name: 'Evidence-backed insights' })).toHaveCount(0);
  await expect(page.getByText('Recorded events service is temporarily unavailable.')).toBeVisible();
  await expect(page.getByText('Saved insights service is temporarily unavailable.').first()).toBeVisible();
  await expect(page.getByText('Observed tracking is unavailable from the canonical service at this selected time.')).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
});

// Narrow DOM heuristic only: inspects native anchors, buttons, and form
// controls against a subset of naming attributes. It misses aria-labelledby,
// custom role widgets, name computation, keyboard behavior, and assistive technology.
test('native links, buttons, and form controls have a detectable name', async ({ page }) => {
  await stubCanonicalMatch(page);
  await openSelectedMatch(page);

  const unnamed = await page.locator('a, button, input, select').evaluateAll(elements => elements.flatMap(element => {
    const control = element as HTMLElement & { labels?: NodeListOf<HTMLLabelElement> };
    const name = control.getAttribute('aria-label')?.trim() || control.innerText?.trim() ||
      control.getAttribute('title')?.trim() || (control.labels ? [...control.labels].map(label => label.innerText.trim()).join(' ') : '');
    return name ? [] : [`${control.tagName.toLowerCase()}${control.id ? `#${control.id}` : ''}`];
  }));
  expect(unnamed).toEqual([]);
});

test('discloses exact evidence lineage at four widths and supports keyboard citation replay', async ({ page }, testInfo) => {
  await stubCanonicalMatch(page, { withEvidence: true });
  await openSelectedMatch(page);
  const disclosureSummary = page.locator('details.canonical-evidence-disclosure > summary');
  await disclosureSummary.focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('details.canonical-evidence-disclosure')).toHaveJSProperty('open', true);

  const citation = page.getByRole('link', {
    name: 'Open event test-event-18 at 55:00, sequence 18, source revision 4'
  });
  await expect(citation).toBeVisible();
  await expect(citation).toHaveAttribute('href', `/replay/${matchId}?matchId=${matchId}&atSeconds=3300&revision=4#canonical-event-18`);
  await expect(page.getByText('source revision 4', { exact: false })).toBeVisible();

  for (const viewport of [
    { name: 'desktop', width: 1440, height: 1000 },
    { name: 'tablet', width: 768, height: 1024 },
    { name: 'mobile', width: 375, height: 812 },
    { name: 'compact', width: 320, height: 800 }
  ]) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    const overflows = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    expect(overflows, `${viewport.name} evidence disclosure should not cause horizontal page scrolling`).toBe(false);
    if (viewport.name === 'mobile' || viewport.name === 'compact') {
      const navigationLinks = await page.locator('.main-navigation a').evaluateAll(links => links.map(link => {
        const bounds = link.getBoundingClientRect();
        return { name: link.textContent?.trim(), left: bounds.left, right: bounds.right };
      }));
      expect(navigationLinks.length).toBeGreaterThan(0);
      for (const link of navigationLinks) {
        expect(link.left, `${link.name} should remain within the mobile viewport`).toBeGreaterThanOrEqual(0);
        expect(link.right, `${link.name} should remain within the mobile viewport`).toBeLessThanOrEqual(viewport.width);
      }
    }
    await page.screenshot({ path: testInfo.outputPath(`ep07-s05-evidence-${viewport.name}.png`), fullPage: true });
  }

  await citation.focus();
  await page.keyboard.press('Enter');
  await expect.poll(() => new URL(page.url()).pathname).toBe(`/replay/${matchId}`);
  const replayUrl = new URL(page.url());
  expect(replayUrl.searchParams.get('atSeconds')).toBe('3300');
  expect(replayUrl.searchParams.get('revision')).toBe('4');
  expect(replayUrl.hash).toBe('#canonical-event-18');
  await expect(page.locator('#canonical-event-18')).toBeFocused();
});
