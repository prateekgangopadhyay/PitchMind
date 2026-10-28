import { describe, expect, it } from 'vitest';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { RoutePage } from './ShellPages';
import { MatchContextPlayersPage, MatchContextTeamsPage, MatchContextInsightsPage, MatchContextAnalystPage } from './MatchContextPages';

describe('match context navigation and pages', () => {
  it('renders a data-neutral sign-in page without example match claims', () => {
    const markup = renderToStaticMarkup(createElement(RoutePage, { route: { kind: 'welcome' }, search: '' }));
    expect(markup).toContain('Sign in to your account');
    expect(markup).toContain('Synthetic EPL intelligence view');
    expect(markup).toContain('Understand the');
    expect(markup).toContain('Premier League');
    expect(markup).toContain('beyond the scoreline.');
    expect(markup).toContain('Illustrative interface preview');
    expect(markup).not.toMatch(/Arsenal|Manchester City|67:24|4 - 3 - 3|AI Insight/);
  });

  it.each([
    ['teams', MatchContextTeamsPage, 'Teams'],
    ['players', MatchContextPlayersPage, 'Players'],
    ['insights', MatchContextInsightsPage, 'Insights'],
    ['analyst', MatchContextAnalystPage, 'AI Analyst']
  ] as const)('keeps the %s submenu inside match context and asks for a selected match', (kind, Page, title) => {
    const route = { kind } as const;
    const markup = renderToStaticMarkup(createElement(Page, { route, seconds: 0 }));
    expect(markup).toContain(title);
    expect(markup).toContain('Choose a match');
    expect(markup).toContain('aria-label="Selected match analysis"');
    expect(markup).not.toMatch(/Arsenal|Manchester City|sample player|example match/);
  });

  it('renders match selection without injecting a fixture', () => {
    const markup = renderToStaticMarkup(createElement(RoutePage, { route: { kind: 'matches', replay: false }, search: '' }));
    expect(markup).toContain('Choose a match');
    expect(markup).not.toMatch(/Arsenal|Manchester City|Liverpool/);
  });
});
