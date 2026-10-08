import { describe, expect, it } from 'vitest';
import { App } from './App';
import { renderToStaticMarkup } from 'react-dom/server';
import { createElement } from 'react';
describe('M01 frontend shell', () => { it('renders PitchMind', () => { expect(renderToStaticMarkup(createElement(App))).toContain('Football intelligence'); }); });
