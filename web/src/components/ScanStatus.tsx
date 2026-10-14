import { useQueryClient } from '@tanstack/react-query';
import { useListLibraries, useStartLibraryScan } from '../api/mediaman';
import type { Library, ScanStatePhase } from '../api/mediaman.schemas';
import { applyLibraryUpdate } from '../lib/useLiveScanProgress';
import { HeaderPopover } from './HeaderPopover';

const phaseLabels: Record<ScanStatePhase, string> = {
  scanning: 'finding files',
  matching: 'matching titles',
  importing: 'importing metadata',
  artwork: 'fetching artwork',
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

/** The library's scan state in words; a failed last scan is an alert with what to do next. */
export function ScanStateText({ library }: { library: Library }) {
  const last = library.scan.lastScan;
  if (library.scan.state === 'idle' && last?.outcome === 'failed') {
    return (
      <span role="alert">
        {library.name}: the last scan failed ({last.error?.detail ?? 'unknown error'}). Scan again, or check the server
        log.
      </span>
    );
  }
  return <span role="status">{describeScan(library)}</span>;
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

function lastScanFailed(library: Library): boolean {
  return library.scan.state === 'idle' && library.scan.lastScan?.outcome === 'failed';
}

/** The header's summary of failed last scans, outside the dropdown so it's announced while the dropdown is closed. */
function failedSummary(failed: Library[]): string | undefined {
  const [first] = failed;
  if (first === undefined) return undefined;
  const what = failed.length === 1 ? `${first.name}: the last scan failed.` : `${String(failed.length)} library scans failed.`;
  return `${what} Open Libraries to see why and scan again.`;
}

/**
 * The header's Libraries dropdown: a summary (scanning, or an alert when a last scan failed) beside a button opening
 * each library's scan state and a Scan button; `useLiveScanProgress` keeps it current (SPEC §11).
 */
export function LibrariesMenu() {
  const libraries = useListLibraries();
  if (!libraries.isSuccess) return null; // The footer's server status already explains a server that can't be reached.

  const items = libraries.data.items;
  const scanning = items.some((library) => library.scan.state !== 'idle');
  const failed = failedSummary(items.filter(lastScanFailed));

  return (
    <div className="flex items-center gap-2">
      {failed !== undefined && (
        <span role="alert" className="max-w-xs text-sm text-fg">
          {failed}
        </span>
      )}
      <HeaderPopover
        label={scanning ? 'Libraries, scanning' : 'Libraries'}
        button={
          <>
            <span>Libraries</span>
            {scanning && (
              <span className="flex items-center gap-1 text-highlight">
                <span aria-hidden className="size-2 rounded-full bg-highlight" />
                scanning…
              </span>
            )}
          </>
        }
      >
        {() =>
          items.length === 0 ? (
            <p>No libraries yet. Add one in Settings → Libraries.</p>
          ) : (
            <ul aria-label="Libraries" className="flex flex-col gap-2">
              {items.map((library) => (
                <li key={library.id} className="flex items-center justify-between gap-3">
                  <ScanStateText library={library} />
                  <ScanLibraryButton library={library} />
                </li>
              ))}
            </ul>
          )
        }
      </HeaderPopover>
    </div>
  );
}
