import { defineConfig } from 'orval';

// Generates the typed client, TanStack Query hooks and MSW handlers in src/api/ from the
// contract (SPEC §10.1). Never hand-edit src/api/; run `npm run generate -w web` instead.
export default defineConfig({
  mediaman: {
    input: { target: '../contracts/openapi.yaml' },
    output: {
      mode: 'split',
      target: 'src/api/mediaman.ts',
      client: 'react-query',
      httpClient: 'fetch',
      mock: true,
      clean: true,
      override: {
        mutator: { path: 'src/lib/http.ts', name: 'apiFetch' },
        fetch: { includeHttpResponseReturnType: false },
      },
    },
  },
});
