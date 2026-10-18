import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';

const matchId = '44444444-4444-4444-8444-444444444444';

test('keeps all seven EP07-S01 views free of axe WCAG A/AA violations at desktop and 320px', async ({ page }, testInfo) => {
  await page.route('**/api/v1/canonical/fixtures**', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      items: [{
        matchId,
        canonicalRevision: 2,
        projectionRevision: 3,
        asOfSeconds: 3309,
        throughSequence: 18,
        phase: 'secondhalf',
        homeScore: 1,
        awayScore: 0,
        provenance: 'synthetic-test-only',
        algorithmVersion: 'test-v1',
        sourceSha256: 'a'.repeat(64)
      }],
      nextMatchId: null,
      hasMore: false
    })
  }));
  await page.route('**/api/v1/canonical/matches/**', route => route.fulfill({
    status: 503,
    contentType: 'application/problem+json',
    body: JSON.stringify({ status: 503, title: 'Unavailable', code: 'TEST_UNAVAILABLE', correlationId: 'test' })
  }));

  const auditRuns: { view: string; width: number; violations: number }[] = [];
  const audit = async (view: string) => {
    const results = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
      .analyze();
    const violations = results.violations.map(violation => ({
      id: violation.id,
      impact: violation.impact,
      help: violation.help,
      nodes: violation.nodes.map(node => ({ target: node.target, summary: node.failureSummary }))
    }));
    auditRuns.push({ view, width: page.viewportSize()?.width ?? 0, violations: violations.length });
    expect(violations, `${view} at ${page.viewportSize()?.width}px: ${JSON.stringify(violations, null, 2)}`).toEqual([]);
  };

  for (const width of [320, 1440]) {
    await page.setViewportSize({ width, height: width === 320 ? 800 : 1000 });
    await page.goto('/welcome');
    await expect(page.getByRole('heading', { name: 'Football intelligence, grounded in evidence.' })).toBeVisible();
    await audit('Welcome');

    await page.getByRole('link', { name: 'Browse available matches' }).click();
    await expect(page.getByRole('heading', { name: 'Choose a match' })).toBeVisible();
    await audit('Home / match list');
    const sourceDetails = page.locator('details').first();
    await sourceDetails.locator('summary').click();
    await expect(sourceDetails).toHaveJSProperty('open', true);
    await audit('Home / expanded source details');

    await page.getByRole('link', { name: 'Open match', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Match overview' })).toBeVisible();
    await audit('Match overview unavailable state');

    for (const view of [
      { navigation: 'Teams', heading: 'Teams' },
      { navigation: 'Players', heading: 'Players' },
      { navigation: 'Insights', heading: 'Insights' },
      { navigation: 'AI Analyst', heading: 'AI Analyst' }
    ]) {
      await page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link', { name: view.navigation, exact: true }).click();
      await expect(page.getByRole('heading', { name: view.heading })).toBeVisible();
      await audit(view.navigation);
    }
  }

  await testInfo.attach('axe-wcag-audit.json', {
    body: JSON.stringify({
      axePlaywright: '4.13.0',
      tags: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'],
      scans: auditRuns
    }, null, 2),
    contentType: 'application/json'
  });
});
