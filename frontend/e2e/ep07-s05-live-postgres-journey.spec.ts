import { expect, test } from '@playwright/test';

// No API routes are intercepted here. scripts/test-ep07-s05-live-api-e2e.py
// creates an isolated PostgreSQL database and seeds only explicitly test-only rows.
const matchId = '33333333-3333-4333-8333-333333333333';
const insightId = 'local-e2e-synthetic-insight';
const eventId = 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee';
const selectedSeconds = 10;
const projectionRevision = 2;

test('reads persisted PostgreSQL evidence through the local API and replays its exact citation', async ({ page }) => {
  await page.setExtraHTTPHeaders({ 'X-Dev-Subject': 'ep07-s05-e2e', 'X-Dev-Roles': 'analyst' });
  const canonicalRequests: string[] = [];
  page.on('request', request => {
    const url = new URL(request.url());
    if (url.pathname.startsWith('/api/v1/canonical/matches/')) canonicalRequests.push(url.pathname);
  });

  await page.goto(`/matches/${matchId}?matchId=${matchId}&atSeconds=${selectedSeconds}&revision=${projectionRevision}`);
  await expect(page.getByRole('heading', { name: 'Match overview' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Evidence-backed insights' })).toBeVisible();
  await expect(page.getByText('Test-only evidence record. No match claim.').first()).toBeVisible();
  await expect(page.getByRole('button', { name: /Select Pass at 00:10, sequence 1/ })).toBeVisible();

  const disclosure = page.locator('details.canonical-evidence-disclosure');
  const summary = disclosure.locator('summary');
  await summary.focus();
  await page.keyboard.press('Enter');
  await expect(disclosure).toHaveJSProperty('open', true);
  await expect(disclosure).toContainText('Limitations are not recorded for this insight.');
  await disclosure.getByText('Stored detector conditions').click();
  await expect(disclosure).toContainText('synthetic-test-only');

  const citation = page.getByRole('link', {
    name: `Open event ${eventId} at 00:10, sequence 1, source revision 2`
  });
  await expect(citation).toBeVisible();
  await expect(citation).toHaveAttribute('href',
    `/replay/${matchId}?matchId=${matchId}&atSeconds=10&revision=2#canonical-event-1`);

  await citation.focus();
  await page.keyboard.press('Enter');
  await expect.poll(() => new URL(page.url()).pathname).toBe(`/replay/${matchId}`);
  const replayUrl = new URL(page.url());
  expect(replayUrl.searchParams.get('atSeconds')).toBe(String(selectedSeconds));
  expect(replayUrl.searchParams.get('revision')).toBe(String(projectionRevision));
  expect(replayUrl.hash).toBe('#canonical-event-1');
  await expect(page.locator('#canonical-event-1')).toBeFocused();
  await expect(page.locator('#canonical-event-1')).toContainText('00:10');

  expect(canonicalRequests).toEqual(expect.arrayContaining([
    `/api/v1/canonical/matches/${matchId}/summary`,
    `/api/v1/canonical/matches/${matchId}/events`,
    `/api/v1/canonical/matches/${matchId}/insights`,
    `/api/v1/canonical/matches/${matchId}/frames`,
    `/api/v1/canonical/matches/${matchId}/key-moments`,
    `/api/v1/canonical/matches/${matchId}/evidence/${insightId}`
  ]));
});
