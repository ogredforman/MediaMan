import { http, HttpResponse } from 'msw';
import {
  ContentType as ContentTypes,
  type Library,
  type LibraryCreateRequest,
  type LibraryUpdateRequest,
  type ScanState,
} from '../api/mediaman.schemas';

/** Matches the server's rules (SPEC §8 libraries); see `LibraryService`. */
const maximumNameLength = 100;
const maximumRoots = 20;
const defaultScanIntervalMinutes = 360;
const contentTypes = Object.values(ContentTypes);

/** The first mock library, there after every `reset`. */
export const mockLibrary: Library = {
  id: '01920000-0000-7000-8000-000000000001',
  name: 'Media',
  kind: 'external',
  contentType: 'mixed',
  rootPaths: ['/media'],
  scanIntervalMinutes: 0,
  scan: { state: 'idle' },
};

const problem = (body: Record<string, unknown> & { status: number }) =>
  HttpResponse.json(body, { status: body.status, headers: { 'Content-Type': 'application/problem+json' } });

const notFound = () =>
  problem({
    title: 'Library not found.',
    status: 404,
    code: 'library.not_found',
    detail: "The library doesn't exist, or it was removed.",
    action: 'Reload the library list.',
  });

const scanRunning = () =>
  problem({
    title: 'The library is being scanned.',
    status: 409,
    code: 'library.scan_running',
    detail: "The library's folders can't change, and it can't be removed, while its scan is running.",
    action: 'Wait for the scan to finish, then try again.',
  });

const isAbsolute = (path: string) => /^(\/|[A-Za-z]:[\\/]|\\\\)/.test(path);
const trimSeparators = (path: string) => (/^([A-Za-z]:)?[\\/]$/.test(path) ? path : path.replace(/[\\/]+$/, ''));
const folderName = (path: string) => path.split(/[\\/]/).filter(Boolean).pop() ?? path;
const isUnder = (path: string, root: string) => {
  const p = path.toLowerCase();
  const r = root.toLowerCase();
  return p === r || p.startsWith(r.endsWith('/') || r.endsWith('\\') ? r : `${r}/`) || p.startsWith(`${r}\\`);
};
const overlap = (a: string, b: string) => isUnder(a, b) || isUnder(b, a);

/**
 * The libraries, kept in memory with the server's rules: absolute roots that overlap no other root, a name that
 * defaults to the first root's folder, scan intervals of 0 or 15 to 10080 minutes, and `409` while a scan runs.
 * Folders aren't checked for existence. `reset` restores `mockLibrary` alone; `setScan` plays scan progress.
 */
export function createLibraryStore() {
  let libraries: Library[] = [];
  let nextId = 0;

  const reset = () => {
    libraries = [mockLibrary];
    nextId = 0;
  };
  reset();

  const sorted = () => [...libraries].sort((a, b) => a.name.localeCompare(b.name));

  /** The `library.invalid` answer for these fields, or the normalized roots and name if they keep every rule. */
  const check = (
    body: LibraryUpdateRequest,
    ownId: string | undefined,
    creating: boolean,
  ): { invalid: Response } | { roots: string[] | undefined; name: string | undefined } => {
    const errors: Record<string, string[]> = {};
    const name = body.name?.trim();
    if (name !== undefined && name.length > maximumNameLength) {
      errors.name = [`Use a name of at most ${String(maximumNameLength)} characters.`];
    }
    const contentType = body.contentType;
    if (contentType === undefined ? creating : !contentTypes.includes(contentType)) {
      errors.contentType = [`Pick one of: ${contentTypes.join(', ')}.`];
    }
    const minutes = body.scanIntervalMinutes;
    if (minutes !== undefined && minutes !== 0 && (minutes < 15 || minutes > 10080)) {
      errors.scanIntervalMinutes = ['Scan every 15 to 10080 minutes (a week), or enter 0 for no scheduled scans.'];
    }
    let roots: string[] | undefined;
    if (creating || body.rootPaths !== undefined) {
      const paths = body.rootPaths ?? [];
      if (paths.length === 0) errors.rootPaths = ['Add at least one folder that holds your media.'];
      else if (paths.length > maximumRoots) {
        errors.rootPaths = [`A library can have at most ${String(maximumRoots)} folders. Split them over more libraries.`];
      } else {
        roots = [];
        for (const [i, raw] of paths.entries()) {
          const key = `rootPaths[${String(i)}]`;
          const root = trimSeparators(raw.trim());
          const sibling = roots.find((r) => overlap(r, root));
          const other = libraries.find((l) => l.id !== ownId && l.rootPaths.some((r) => overlap(r, root)));
          if (root === '') errors[key] = ['Enter the folder that holds your media.'];
          else if (!isAbsolute(root)) errors[key] = ['Enter the full path to the folder, starting from the drive or root.'];
          else if (sibling !== undefined) {
            errors[key] = [
              sibling.toLowerCase() === root.toLowerCase()
                ? 'This folder is listed twice. Remove one of them.'
                : `This folder overlaps '${sibling}' in this library. Keep only the outer folder.`,
            ];
          } else if (other !== undefined) {
            errors[key] = [
              `This folder overlaps the library '${other.name}'. Each folder can belong to one library only; change that library first.`,
            ];
          }
          roots.push(root);
        }
      }
    }
    if (Object.keys(errors).length > 0) {
      return {
        invalid: problem({
          title: 'Some library details need fixing.',
          status: 400,
          code: 'library.invalid',
          detail: 'One or more fields are invalid; each one says why.',
          action: 'Fix the highlighted fields, then save.',
          errors,
        }),
      };
    }
    return { roots, name: name === '' ? undefined : name };
  };

  const handlers = [
    http.get('*/libraries', () => HttpResponse.json({ items: sorted(), pageInfo: {} })),
    http.post('*/libraries', async ({ request }) => {
      const body = (await request.json()) as LibraryCreateRequest;
      const checked = check(body, undefined, true);
      if ('invalid' in checked) return checked.invalid;
      const roots = checked.roots ?? [];
      nextId += 1;
      const library: Library = {
        id: `01920000-0000-7000-8001-${String(nextId).padStart(12, '0')}`,
        name: checked.name ?? folderName(roots[0] ?? ''),
        kind: 'external',
        contentType: body.contentType,
        rootPaths: roots,
        scanIntervalMinutes: body.scanIntervalMinutes ?? defaultScanIntervalMinutes,
        scan: { state: 'queued' },
      };
      libraries = [...libraries, library];
      return HttpResponse.json(library, { status: 201, headers: { Location: `/libraries/${library.id}` } });
    }),
    http.get('*/libraries/:libraryId', ({ params }) => {
      const library = libraries.find((l) => l.id === params.libraryId);
      return library === undefined ? notFound() : HttpResponse.json(library);
    }),
    http.patch('*/libraries/:libraryId', async ({ params, request }) => {
      const existing = libraries.find((l) => l.id === params.libraryId);
      if (existing === undefined) return notFound();
      const body = (await request.json()) as LibraryUpdateRequest;
      const checked = check(body, existing.id, false);
      if ('invalid' in checked) return checked.invalid;
      if (checked.roots !== undefined && existing.scan.state === 'running') return scanRunning();
      const added = checked.roots?.some((r) => !existing.rootPaths.includes(r)) ?? false;
      const updated: Library = {
        ...existing,
        name: checked.name ?? existing.name,
        contentType: body.contentType ?? existing.contentType,
        rootPaths: checked.roots ?? existing.rootPaths,
        scanIntervalMinutes: body.scanIntervalMinutes ?? existing.scanIntervalMinutes,
        scan: added && existing.scan.state === 'idle' ? { ...existing.scan, state: 'queued' } : existing.scan,
      };
      libraries = libraries.map((l) => (l.id === existing.id ? updated : l));
      return HttpResponse.json(updated);
    }),
    http.delete('*/libraries/:libraryId', ({ params }) => {
      const existing = libraries.find((l) => l.id === params.libraryId);
      if (existing === undefined) return notFound();
      if (existing.scan.state === 'running') return scanRunning();
      libraries = libraries.filter((l) => l.id !== existing.id);
      return new HttpResponse(null, { status: 204 });
    }),
    http.post('*/libraries/:libraryId/scan', ({ params }) => {
      const existing = libraries.find((l) => l.id === params.libraryId);
      if (existing === undefined) return notFound();
      const scan: ScanState = existing.scan.state === 'idle' ? { ...existing.scan, state: 'queued' } : existing.scan;
      const updated = { ...existing, scan };
      libraries = libraries.map((l) => (l.id === existing.id ? updated : l));
      return HttpResponse.json(updated, { status: 202 });
    }),
  ];

  return {
    handlers,
    reset,
    get: (libraryId: string) => libraries.find((l) => l.id === libraryId),
    /** Sets a library's scan state, as the scan worker would. */
    setScan: (libraryId: string, scan: ScanState) => {
      libraries = libraries.map((l) => (l.id === libraryId ? { ...l, scan } : l));
    },
  };
}

export const mockLibraries = createLibraryStore();
