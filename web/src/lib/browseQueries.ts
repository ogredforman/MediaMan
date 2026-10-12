import type { QueryClient } from '@tanstack/react-query';

// Prefixes of the generated query keys: /titles (grids and every detail), /home and /search (not /catalog/search).
const browseKeys = ['/titles', '/home', '/search'];

/** Refetches what shows the library: the grids, every title detail, Home and search results. */
export function invalidateBrowseQueries(queryClient: QueryClient) {
  return queryClient.invalidateQueries({
    predicate: ({ queryKey: [first] }) => typeof first === 'string' && browseKeys.some((key) => first.startsWith(key)),
  });
}
