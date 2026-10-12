import { useQueryClient, type QueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { getListLibrariesQueryKey } from '../api/mediaman';
import type { Library, LibraryPage, ScanProgressEvent } from '../api/mediaman.schemas';
import { invalidateBrowseQueries } from './browseQueries';
import { connectEvents } from './events';

function isScanProgress(value: unknown): value is ScanProgressEvent {
  if (typeof value !== 'object' || value === null) return false;
  const v = value as Record<string, unknown>;
  return typeof v.libraryId === 'string' && typeof v.scan === 'object' && v.scan !== null;
}

/** Puts a library's new scan state into every cached library list; a finished scan refreshes what shows the library. */
export function applyLibraryUpdate(queryClient: QueryClient, libraryId: string, scan: Library['scan']): void {
  const known = queryClient
    .getQueriesData<LibraryPage>({ queryKey: getListLibrariesQueryKey() })
    .some(([, page]) => page?.items.some((library) => library.id === libraryId));
  queryClient.setQueriesData<LibraryPage>({ queryKey: getListLibrariesQueryKey() }, (page) => {
    if (page === undefined) return page;
    return {
      ...page,
      items: page.items.map((library) => {
        return library.id === libraryId ? { ...library, scan } : library;
      }),
    };
  });
  if (!known) void queryClient.invalidateQueries({ queryKey: getListLibrariesQueryKey() });
  if (scan.state === 'idle') void invalidateBrowseQueries(queryClient);
}

/** Follows `GET /events` while mounted and applies `scan.progress` to the query cache (SPEC §11: live progress). */
export function useLiveScanProgress(): void {
  const queryClient = useQueryClient();

  useEffect(() => {
    const controller = new AbortController();
    void connectEvents({
      signal: controller.signal,
      // A scan may have finished while disconnected, so refresh what its events would have updated.
      onOpen: () => {
        void queryClient.invalidateQueries({ queryKey: getListLibrariesQueryKey() });
        void invalidateBrowseQueries(queryClient);
      },
      onEvent: (event) => {
        if (event.type === 'scan.progress' && isScanProgress(event.data)) {
          applyLibraryUpdate(queryClient, event.data.libraryId, event.data.scan);
        }
      },
    });
    return () => {
      controller.abort();
    };
  }, [queryClient]);
}
