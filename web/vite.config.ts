/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite';
import { tanstackRouter } from '@tanstack/router-plugin/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// With MEDIAMAN_SERVER_URL set (and VITE_API_BASE_URL=/api), the dev server proxies /api/* to a running server, so the UI
// and API share an origin and no CORS is needed. The server e2e run uses this (playwright.server.config.ts).
const serverUrl = process.env.MEDIAMAN_SERVER_URL;

// The server serves the built app under /app/ (ADR 0009; WebUi.RequestPath); the dev server keeps it at /.
export default defineConfig(({ command }) => ({
  base: command === 'build' ? '/app/' : '/',
  // The router plugin generates src/routeTree.gen.ts from src/routes/ and must run before React.
  plugins: [tanstackRouter({ target: 'react', autoCodeSplitting: true }), react(), tailwindcss()],
  server: serverUrl
    ? { proxy: { '/api': { target: serverUrl, changeOrigin: true, rewrite: (path) => path.replace(/^\/api/, '') } } }
    : {},
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // Whole-app renders are slow under a full parallel run; above the 5 s findBy* wait in setup.ts.
    testTimeout: 15_000,
    include: ['src/**/*.test.{ts,tsx}'],
  },
}));
