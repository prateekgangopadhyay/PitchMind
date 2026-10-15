import { expect, test, type Page, type Route } from '@playwright/test';

// These are deliberately synthetic, test-only canonical API responses. They do
// not describe a real match or certify the candidate feed's unresolved units.
const matchId = '33333333-3333-4333-8333-333333333333';
const selectedSeconds = 3309;
const canonicalRevision = 4;
const throughSequence = 19;
const sourceSha256 = 'c'.repeat(64);
const insightSummary = 'Recorded sequence is available for review.';

type StubOptions = { unavailable?: string[]; withEvidence?: boolean };

async function stubCanonicalMatch(page: Page, options: StubOptions = {}) {
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
      const evidenceInsight = {
        insightId: 'test-insight-1', ...context, sourceVersion: 'test-source-v1', provenance: 'synthetic-test-only',
        sourceSha256, detectorVersion: 'test-detector-v1', truthClass: 'observed', summary: insightSummary,
        conditions: { sampleSize: 2, testOnly: true }, limitations: ['Test fixture only'],
        evidence: [{ kind: 'event', id: 'test-event-18', relation: 'supports' }]
      };
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({
        insight: evidenceInsight,
        items: [{ kind: 'event', id: 'test-event-18', relation: 'supports', sourceCanonicalRevision: 4,
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
          evidence: options.withEvidence ? [{ kind: 'event', id: 'test-event-18', relation: 'supports' }] : [] }]
      },
      frames: { ...context, items: [], nextCursor: null, hasMore: false },
      'key-moments': { ...context, items: [], hasMore: false }
    };
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(bodyByResource[resource ?? ''] ?? {}) });
  });
}

async function openSelectedMatch(page: Page) {
  await page.goto(`/matches/${matchId}?matchId=${matchId}&atSeconds=${selectedSeconds}&revision=${canonicalRevision}`);
  await expect(page.getByRole('heading', { name: 'Match overview' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Evidence-backed insights' })).toBeVisible();
}

test('audience mode changes disclosure while preserving selected-time facts', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 800 });
  await stubCanonicalMatch(page);
  await openSelectedMatch(page);

  const score = page.getByRole('region', { name: 'Canonical match score' });
  const matchFacts = await score.innerText();
  const truthClass = page.getByLabel('Insight category: observed');
  await expect(truthClass).toBeVisible();
  const truthClassText = await truthClass.innerText();
  await expect(score).toContainText('55:09');
  await expect(score).toContainText('1');
  await expect(page.getByText(insightSummary)).toBeVisible();
  await expect(page.getByText('Simulated EPL-context match')).toBeVisible();

  const audience = page.getByRole('group', { name: 'Explanation detail' });
  await audience.getByRole('button', { name: 'Analyst' }).click();
  await expect(audience.getByRole('button', { name: 'Analyst' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByText('Detector test-detector-v1')).toBeVisible();
  await page.getByText('Recorded conditions and limitations').click();
  await expect(page.getByText('Test fixture only')).toBeVisible();
  expect(await score.innerText()).toBe(matchFacts);
  await expect(truthClass).toBeVisible();
  expect(await truthClass.innerText()).toBe(truthClassText);
  await expect(page.getByText(insightSummary)).toBeVisible();

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

  await expect(page.getByRole('region', { name: 'Canonical match score' })).toContainText('55:09');
  await expect(page.getByText('Recorded events service is temporarily unavailable.')).toBeVisible();
  await expect(page.getByText('Saved insights service is temporarily unavailable.')).toBeVisible();
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
