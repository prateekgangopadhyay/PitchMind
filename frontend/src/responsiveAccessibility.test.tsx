/// <reference types="vite/client" />
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { App } from './App';
import styles from './styles.css?raw';

function hexRgb(value: string): [number, number, number] {
  const hex = value.replace('#', '');
  return [0, 2, 4].map(offset => Number.parseInt(hex.slice(offset, offset + 2), 16)) as [number, number, number];
}

function relativeLuminance(value: string): number {
  const channels = hexRgb(value).map(channel => {
    const normalized = channel / 255;
    return normalized <= 0.04045 ? normalized / 12.92 : ((normalized + 0.055) / 1.055) ** 2.4;
  });
  return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
}

function contrastRatio(first: string, second: string): number {
  const luminances = [relativeLuminance(first), relativeLuminance(second)].sort((a, b) => b - a);
  return (luminances[0] + 0.05) / (luminances[1] + 0.05);
}

function token(name: string): string {
  const match = styles.match(new RegExp(`${name}:\\s*(#[0-9a-fA-F]{6})`));
  if (!match) throw new Error(`Missing color token ${name}`);
  return match[1];
}

describe('responsive navigation accessibility contract', () => {
  it('preserves skip target and native-link prerequisites for keyboard navigation', () => {
    const markup = renderToStaticMarkup(createElement(App));
    const skipIndex = markup.indexOf('class="skip-link"');
    const brandIndex = markup.indexOf('class="brand"');
    const navigationIndex = markup.indexOf('<nav');
    const mainIndex = markup.indexOf('<main id="main-content"');
    const navigation = markup.match(/<nav\b[^>]*>([\s\S]*?)<\/nav>/)?.[1];

    expect(skipIndex).toBeGreaterThanOrEqual(0);
    expect(skipIndex).toBeLessThan(brandIndex);
    expect(brandIndex).toBeLessThan(navigationIndex);
    expect(navigationIndex).toBeLessThan(mainIndex);
    expect(markup).toContain('href="#main-content"');
    expect(markup).toContain('id="main-content" tabindex="-1"');
    expect(markup).toContain('aria-label="Main navigation"');
    expect(navigation).toBeDefined();

    const links = [...(navigation ?? '').matchAll(/<a\b([^>]*)>([\s\S]*?)<\/a>/g)];
    expect(links.map(([, , label]) => label.replace(/<[^>]+>/g, '').trim())).toEqual([
      'Home', 'Match', 'Live match', 'Teams', 'Players', 'Insights', 'AI Analyst'
    ]);
    expect(links.every(([, attributes]) => /\bhref=/.test(attributes) && !/\btabindex="(?:[1-9]|-[1-9])/.test(attributes))).toBe(true);
    expect(links[0][1]).toContain('aria-current="page"');
  });

  it('declares a non-scrolling three-column navigation layout at the mobile breakpoint', () => {
    const mobileNavigation = styles.match(/@media\s*\(max-width:\s*640px\)\s*\{[\s\S]*?\.main-navigation\s*\{([^}]*)\}/)?.[1];
    const navigationLink = styles.match(/\.main-navigation a\s*\{([^}]*)\}/)?.[1];

    expect(mobileNavigation).toBeDefined();
    expect(mobileNavigation).toMatch(/display:\s*grid/);
    expect(mobileNavigation).toMatch(/grid-template-columns:\s*repeat\(3,\s*minmax\(0,\s*1fr\)\)/);
    expect(mobileNavigation).toMatch(/overflow:\s*visible/);
    expect(navigationLink).toMatch(/min-height:\s*48px/);
  });

  it('keeps active and inactive navigation text above WCAG AA normal-text contrast', () => {
    const background = token('--pm-bg');
    const active = token('--pm-accent');
    const inactive = token('--pm-text-secondary');

    expect(contrastRatio(active, background)).toBeGreaterThanOrEqual(4.5);
    expect(contrastRatio(inactive, background)).toBeGreaterThanOrEqual(4.5);
  });
});
