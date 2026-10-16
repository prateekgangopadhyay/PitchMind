import { expect, test } from '@playwright/test';

// Test-only canonical response. It contains no real club, player, or match facts.
const matchId = '44444444-4444-4444-8444-444444444444';

test('keeps the seven-screen shell clear, keyboard reachable, and usable at 320px', async ({ page }, testInfo) => {
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

  for (const viewport of [
    { name: 'desktop', width: 1440, height: 1000 },
    { name: 'tablet', width: 768, height: 1024 },
    { name: 'mobile-375', width: 375, height: 812 },
    { name: 'mobile', width: 320, height: 800 }
  ]) {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await page.goto('/welcome');
    await expect(page.getByRole('heading', { name: 'Welcome to PitchMind' })).toBeVisible();
    const skipLink = page.getByRole('link', { name: 'Skip to main content' });
    await page.keyboard.press('Tab');
    await expect(skipLink).toBeFocused();
    const skipFocusStyle = await skipLink.evaluate(element => ({
      outlineStyle: getComputedStyle(element).outlineStyle,
      outlineWidth: getComputedStyle(element).outlineWidth
    }));
    expect(skipFocusStyle).toEqual({ outlineStyle: 'solid', outlineWidth: '3px' });
    await page.keyboard.press('Enter');
    await expect(page.locator('#main-content')).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(page.getByRole('link', { name: 'Check match availability' })).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'Choose a match' })).toBeVisible();
    await expect(page.getByText('Match data with source details')).toBeVisible();
    await expect(page.getByText('Simulated EPL-context match')).toBeVisible();

    const matches = page.getByRole('list', { name: 'Matches' });
    await expect(matches).toBeVisible();
    await expect(matches.getByRole('listitem')).toHaveCount(1);
    const card = page.locator('.match-card');
    await expect(card.getByRole('heading', { name: 'Home team 1–0 Away team' })).toBeVisible();
    const sourceDetails = card.locator('details');
    await expect(sourceDetails).toHaveJSProperty('open', false);
    await expect(sourceDetails.getByText('Source hash')).not.toBeVisible();
    await page.screenshot({ path: testInfo.outputPath(`ep07-s01-home-${viewport.name}.png`), fullPage: true });
    await sourceDetails.locator('summary').focus();
    await page.keyboard.press('Enter');
    await expect(sourceDetails).toHaveJSProperty('open', true);
    await expect(sourceDetails).toContainText(matchId);
    const sourceHashLine = sourceDetails.locator('p').last();
    const sourceHashDimensions = await sourceHashLine.evaluate(element => ({
      clientWidth: element.clientWidth,
      scrollWidth: element.scrollWidth
    }));
    expect(sourceHashDimensions.scrollWidth).toBeLessThanOrEqual(sourceHashDimensions.clientWidth);
    if (viewport.name === 'mobile') {
      await page.screenshot({ path: testInfo.outputPath('ep07-s01-home-source-details-open-mobile-320.png'), fullPage: true });
    }

    await card.getByRole('link', { name: 'Open match', exact: true }).focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'Match overview' })).toBeVisible();
    await expect(page.getByRole('alert')).toContainText('Match data service is temporarily unavailable.');
    await expect(page.getByText('1–0')).toHaveCount(0);
    const matchCopy = await page.locator('body').innerText();
    expect(matchCopy).not.toMatch(/\b(canonical|API|Foundry|payload|contract)\b/i);
    const matchWidth = await page.evaluate(() => ({ viewport: window.innerWidth, document: document.documentElement.scrollWidth }));
    expect(matchWidth.document, `Match should reflow at ${viewport.width}px`).toBeLessThanOrEqual(matchWidth.viewport);
    await page.getByRole('link', { name: 'Home', exact: true }).focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'Choose a match' })).toBeVisible();

    for (const view of [
      { name: 'Teams', heading: 'Team overview', message: 'Team profiles are not available yet.' },
      { name: 'Players', heading: 'Player overview', message: 'Player profiles are not available yet.' },
      { name: 'Insights', heading: 'Evidence at this moment', message: 'Insights are temporarily unavailable.' },
      { name: 'AI Analyst', heading: 'Understand a match', message: 'Match questions are not available yet.' }
    ]) {
      await page.getByRole('link', { name: view.name, exact: true }).focus();
      await page.keyboard.press('Enter');
      await expect(page.getByRole('heading', { name: view.heading })).toBeVisible();
      await expect(page.getByText(view.message)).toBeVisible();
      const width = await page.evaluate(() => ({ viewport: window.innerWidth, document: document.documentElement.scrollWidth }));
      expect(width.document, `${view.name} should reflow at ${viewport.width}px`).toBeLessThanOrEqual(width.viewport);
      const visibleCopy = await page.locator('body').innerText();
      expect(visibleCopy).not.toMatch(/\b(canonical|API|Foundry|payload|contract)\b/i);
      if (view.name === 'AI Analyst') {
        await page.screenshot({ path: testInfo.outputPath(`ep07-s01-analyst-${viewport.name}.png`), fullPage: true });
      }
    }

    await page.getByRole('link', { name: 'Home', exact: true }).focus();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'Choose a match' })).toBeVisible();
    const pageWidth = await page.evaluate(() => ({ viewport: window.innerWidth, document: document.documentElement.scrollWidth }));
    expect(pageWidth.document, `Home should reflow at ${viewport.width}px`).toBeLessThanOrEqual(pageWidth.viewport);
  }
});
