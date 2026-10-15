import { describe, expect, it } from 'vitest';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { RoutePage } from './ShellPages';

describe('welcome information architecture', () => {
  it('leads to match availability without implying a fixture is already available', () => {
    const markup = renderToStaticMarkup(createElement(RoutePage, {
      route: { kind: 'welcome' },
      search: ''
    }));

    expect(markup).toContain('Find a match and inspect the evidence behind each view.');
    expect(markup).toContain('Check match availability');
    expect(markup).toContain('href="/home"');
    expect(markup).not.toContain('simulated match');
    expect(markup).not.toContain('available matches');
  });
});
