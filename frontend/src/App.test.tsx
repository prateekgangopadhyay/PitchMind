import { describe, expect, it } from 'vitest';
import { App } from './App';
import { renderToStaticMarkup } from 'react-dom/server';
describe('M01 frontend shell', () => { it('renders PitchMind', () => { expect(renderToStaticMarkup(<App />)).toContain('Football intelligence'); }); });
