import { expect, test, type Page, type Route } from '@playwright/test';

const matchId = '33333333-3333-4333-8333-333333333333';
const canonicalRevision = 4;
const throughSequence = 19;
const sourceSha256 = 'c'.repeat(64);

async function stubCanonicalMatch(page: Page) {
  await page.route('**/api/v1/canonical/matches/**', async (route: Route) => {
    const url = new URL(route.request().url());
    const resource = url.pathname.split('/').at(-1);
    const atSeconds = Number(url.searchParams.get('atSeconds'));
    const selectedTimeSeconds = Number.isInteger(atSeconds) ? atSeconds : 3309;
    const context = { matchId, canonicalRevision, selectedTimeSeconds, throughSequence };
    const bodyByResource: Record<string, unknown> = {
      summary: {
        ...context, asOfSeconds: selectedTimeSeconds, algorithmVersion: 'test-projection-v1', sourceSha256,
        stateSha256: 'd'.repeat(64), provenance: 'synthetic-test-only', period: 2, phase: 'secondhalf',
        homeScore: 1, awayScore: 0, possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
      },
      events: { ...context, items: [], nextCursor: null, hasMore: false },
      insights: { ...context, items: [], nextCursor: null, hasMore: false },
      frames: { ...context, items: [], nextCursor: null, hasMore: false },
      'key-moments': { ...context, items: [], nextCursor: null, hasMore: false }
    };
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(bodyByResource[resource ?? ''] ?? {})
    });
  });
}

test('selected-time replay slider has a mobile hit area and remains keyboard operable', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 800 });
  await stubCanonicalMatch(page);
  await page.goto(`/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=${canonicalRevision}`);
  await expect(page.getByRole('heading', { name: 'Match overview' })).toBeVisible();

  const slider = page.getByRole('slider', { name: 'Selected match time' });
  const bounds = await slider.boundingBox();
  expect(bounds?.height).toBeGreaterThanOrEqual(44);
  await slider.focus();
  await expect(slider).toHaveAttribute('aria-valuetext', '55:09');
  await slider.press('ArrowRight');
  await expect(slider).toHaveAttribute('aria-valuetext', '55:10');
  await expect(page.getByRole('region', { name: 'Canonical match score' })).toContainText('55:10');

  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  expect(overflow, 'the selected-time control should fit without horizontal scrolling at 320px').toBe(false);
  await page.screenshot({ path: '../ep07-s04-mobile-slider-after.png', fullPage: true });
});
