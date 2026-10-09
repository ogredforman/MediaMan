/** A `?redirect=` value if it's a path on this site, else undefined: `//host` and `/\host` are other sites to a browser. */
export function safeRedirect(value: unknown): string | undefined {
  return typeof value === 'string' && value.startsWith('/') && !value.startsWith('//') && !value.includes('\\')
    ? value
    : undefined;
}
