import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5080',
        headers: {
          'X-Dev-Subject': 'local-analyst',
          'X-Dev-Roles': 'analyst'
        }
      }
    }
  },
  test: {
    environment: 'jsdom',
    css: true,
    include: ['src/**/*.test.{ts,tsx}']
  }
});
