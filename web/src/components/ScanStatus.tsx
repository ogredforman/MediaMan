import { useQueryClient } from '@tanstack/react-query';
import { useListLibraries, useStartLibraryScan } from '../api/mediaman';
import type { Library, ScanStatePhase } from '../api/mediaman.schemas';
import { applyLibraryUpdate } from '../lib/useLiveScanProgress';

const phaseLabels: Record<ScanStatePhase, string> = {
  scanning: 'finding files',
  matching: 'matching titles',
  importing: 'importing metadata',
};

function describeScan(library: Library): string {
  const { scan } = library;
  if (scan.state === 'queued') return `${library.name}: scan queued`;
  if (scan.state === 'running') return `${library.name}: ${scan.phase ? phaseLabels[scan.phase] : 'scanning'}…`;
  const last = scan.lastScan;
  if (last?.outcome === 'succeeded') {
    return `${library.name}: scanned, ${String(last.counts.linked)} linked, ${String(last.counts.needsAttention)} need attention`;
  }
  return library.name;
}

export function ScanLibraryButton({ library }: { library: Library }) {
  const queryClient = useQueryClient();
  const scan = useStartLibraryScan({
    mutation: {
      onSuccess: (updated) => {
        applyLibraryUpdate(queryClient, updated.id, updated.scan);
      },
    },
  });
  const busy = scan.isPending || library.scan.state !== 'idle';

  return (
    <>
      <button
        type="button"
        aria-label={`Scan ${library.name}`}
        disabled={busy}
        onClick={() => {
          scan.mutate({ libraryId: library.id });
        }}
        className="rounded-card bg-highlight px-3 py-1 text-sm font-medium text-bg transition-opacity duration-(--motion-fast) hover:opacity-90 disabled:opacity-50"
      >
        Scan
      </button>
      {scan.isError && (
        <span role="alert" className="text-sm">
          {scan.error.problem.title} {scan.error.problem.action}
        </span>
      )}
    </>
  );
}

/** Each library's scan state and a Scan button; `useLiveScanProgress` keeps it current (SPEC §11). */
export function ScanStatus() {
  const libraries = useListLibraries();
  if (!libraries.isSuccess) return null; // The footer's server status already explains a server that can't be reached.

  return (
    <ul aria-label="Libraries" className="ml-auto flex flex-col items-end gap-1 text-sm text-fg-muted">
      {libraries.data.items.map((library) => {
        const last = library.scan.lastScan;
        const failed = library.scan.state === 'idle' && last?.outcome === 'failed';
        return (
          <li key={library.id} className="flex items-center gap-3">
            {failed ? (
              <span role="alert">
                {library.name}: the last scan failed ({last.error?.detail ?? 'unknown error'}). Scan again, or check the
                server log.
              </span>
            ) : (
              <span role="status">{describeScan(library)}</span>
            )}
            <ScanLibraryButton library={library} />
          </li>
        );
      })}
    </ul>
  );
}
