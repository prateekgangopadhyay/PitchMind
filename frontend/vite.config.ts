import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
export default defineConfig({ plugins: [react()], server: { proxy: { '/api': 'http://127.0.0.1:5080' } }, test: { environment: 'jsdom', include: ['src/**/*.test.tsx'] } });
