interface ImportMetaEnv {
  /** Server origin (and path prefix, if any). Empty means same origin as the web UI. */
  readonly VITE_API_BASE_URL?: string;
  /** `true` serves the API from the generated MSW handlers instead of a server (`vite --mode mock`). */
  readonly VITE_API_MOCK?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
