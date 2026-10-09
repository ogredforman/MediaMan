import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type SubmitEvent } from 'react';
import {
  getListLibrariesQueryKey,
  useCreateLibrary,
  useDeleteLibrary,
  useListLibraries,
  useUpdateLibrary,
} from '../api/mediaman';
import type { ContentType, Library, LibraryPage, LibraryUpdateRequest } from '../api/mediaman.schemas';
import type { ApiError } from '../lib/http';
import { Field, FieldError, type FieldErrors } from './FormField';
import { buttonClass, inputClass } from './formClasses';
import { ScanLibraryButton, ScanStateText } from './ScanStatus';

/** Matches the server's limit (SPEC §8 libraries); the server checks it again. */
const maximumRoots = 20;
const defaultScanIntervalMinutes = 360;

const contentTypeLabels: Record<ContentType, string> = {
  mixed: 'Movies and shows',
  movies: 'Movies',
  shows: 'Shows',
  anime: 'Anime',
};

const intervalPresets: { minutes: number; label: string }[] = [
  { minutes: 0, label: 'Never (scan by hand)' },
  { minutes: 15, label: 'Every 15 minutes' },
  { minutes: 60, label: 'Every hour' },
  { minutes: 360, label: 'Every 6 hours' },
  { minutes: 720, label: 'Every 12 hours' },
  { minutes: 1440, label: 'Every day' },
  { minutes: 10080, label: 'Every week' },
];

function intervalLabel(minutes: number): string {
  return intervalPresets.find((p) => p.minutes === minutes)?.label ?? `Every ${String(minutes)} minutes`;
}

function describeInterval(minutes: number): string {
  return minutes === 0 ? 'No scheduled full scans' : `Full scan ${intervalLabel(minutes).toLowerCase()}`;
}

type Editing = { library: Library | undefined } | undefined;

/** Settings → Libraries: each library with its folders and scan state, add and edit, and delete with a confirmation (SPEC §8 libraries, §11). */
export function LibrarySettings() {
  const libraries = useListLibraries();
  const [editing, setEditing] = useState<Editing>();

  if (editing !== undefined) {
    return (
      <LibraryForm
        library={editing.library}
        onDone={() => {
          setEditing(undefined);
        }}
      />
    );
  }

  return (
    <section aria-labelledby="libraries-heading" className="flex flex-col gap-4">
      <div className="flex items-center justify-between gap-4">
        <h2 id="libraries-heading" className="text-xl font-semibold">
          Libraries
        </h2>
        <button
          type="button"
          onClick={() => { setEditing({ library: undefined }); }}
          className={`${buttonClass} bg-highlight text-bg`}
        >
          Add library
        </button>
      </div>
      <p className="text-sm text-fg-muted">
        MediaMan only reads these folders; it never renames, moves or deletes anything in them.
      </p>
      {libraries.isPending && <p role="status">Loading libraries…</p>}
      {libraries.isError && (
        <p role="alert">
          {libraries.error.problem.title} {libraries.error.problem.action}
        </p>
      )}
      {libraries.isSuccess &&
        (libraries.data.items.length === 0 ? (
          <p>No libraries yet. Add one to start building your collection.</p>
        ) : (
          <ul aria-label="Library list" className="flex flex-col gap-3">
            {libraries.data.items.map((library) => (
              <LibraryCard
                key={library.id}
                library={library}
                onEdit={() => { setEditing({ library }); }}
              />
            ))}
          </ul>
        ))}
    </section>
  );
}

function LibraryCard({ library, onEdit }: { library: Library; onEdit: () => void }) {
  const queryClient = useQueryClient();
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const remove = useDeleteLibrary({
    mutation: {
      onSuccess: () => {
        queryClient.setQueriesData<LibraryPage>({ queryKey: getListLibrariesQueryKey() }, (page) =>
          page === undefined ? page : { ...page, items: page.items.filter((l) => l.id !== library.id) },
        );
        void queryClient.invalidateQueries({ queryKey: getListLibrariesQueryKey() });
      },
    },
  });
  const headingId = `library-${library.id}`;

  return (
    <li aria-labelledby={headingId} className="flex flex-col gap-3 rounded-card bg-surface p-4 shadow-soft">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h3 id={headingId} className="text-lg font-semibold">
          {library.name}
        </h3>
        <p className="text-sm text-fg-muted">
          {contentTypeLabels[library.contentType]} · {describeInterval(library.scanIntervalMinutes)}
        </p>
      </div>
      <ul aria-label={`Folders in ${library.name}`} className="flex flex-col gap-1 font-mono text-sm">
        {library.rootPaths.map((root) => (
          <li key={root}>{root}</li>
        ))}
      </ul>
      <div className="flex flex-wrap items-center gap-3 text-sm text-fg-muted">
        <ScanStateText library={library} />
      </div>

      {remove.isError && (
        <p role="alert" className="text-sm">
          {remove.error.problem.title} {remove.error.problem.action}
        </p>
      )}

      {confirmingDelete ? (
        <div className="flex flex-col gap-3 rounded-card bg-bg p-4">
          <p className="text-sm">
            Delete {library.name}? MediaMan forgets its folders and the titles found in them. Your files stay on disk;
            nothing in these folders is deleted or changed.
          </p>
          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={() => {
                setConfirmingDelete(false);
                remove.reset();
              }}
              className={`${buttonClass} bg-surface text-fg`}
            >
              Keep library
            </button>
            <button
              type="button"
              disabled={remove.isPending}
              onClick={() => { remove.mutate({ libraryId: library.id }); }}
              className={`${buttonClass} bg-highlight text-bg`}
            >
              {remove.isPending ? 'Deleting…' : 'Delete library'}
            </button>
          </div>
        </div>
      ) : (
        <div className="flex flex-wrap items-center gap-2">
          <ScanLibraryButton library={library} />
          <button type="button" aria-label={`Edit ${library.name}`} onClick={onEdit} className={`${buttonClass} bg-bg text-fg`}>
            Edit
          </button>
          <button
            type="button"
            aria-label={`Delete ${library.name}`}
            onClick={() => { setConfirmingDelete(true); }}
            className={`${buttonClass} bg-bg text-fg`}
          >
            Delete
          </button>
        </div>
      )}
    </li>
  );
}

interface Root {
  key: number;
  path: string;
}

/** The fields the form shows errors on, besides each folder's `rootPaths[N]`. */
const formFields = ['name', 'contentType', 'rootPaths', 'scanIntervalMinutes'];

const rootKey = (index: number) => `rootPaths[${String(index)}]`;

/** Drops the errors of the removed root and moves later roots' errors up one place, so each stays on its folder. */
function withoutRoot(errors: FieldErrors, removed: number): FieldErrors {
  const kept: FieldErrors = {};
  for (const [field, messages] of Object.entries(errors)) {
    const match = /^rootPaths\[(\d+)\]$/.exec(field);
    if (match === null) {
      if (field !== 'rootPaths') kept[field] = messages;
      continue;
    }
    const index = Number(match[1]);
    if (index < removed) kept[field] = messages;
    else if (index > removed) kept[rootKey(index - 1)] = messages;
  }
  return kept;
}

const sameRoots = (a: string[], b: string[]) => a.length === b.length && a.every((root, i) => root === b[i]);

/** Adds a library (`library` undefined) or edits one; edits send only what changed. */
function LibraryForm({ library, onDone }: { library: Library | undefined; onDone: () => void }) {
  const queryClient = useQueryClient();
  const [name, setName] = useState(library?.name ?? '');
  const [contentType, setContentType] = useState<ContentType>(library?.contentType ?? 'mixed');
  const initialPaths = library?.rootPaths ?? [''];
  const [roots, setRoots] = useState<Root[]>(() => initialPaths.map((path, key) => ({ key, path })));
  // Rows keep their key while others are added or removed, so React keeps each input's focus and text.
  const nextKey = useRef(initialPaths.length);
  const [interval, setScanInterval] = useState(library?.scanIntervalMinutes ?? defaultScanIntervalMinutes);
  const [errors, setErrors] = useState<FieldErrors>({});

  // The form replaces the list, so focus moves to its heading.
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    heading.current?.focus();
  }, []);

  const done = () => {
    void queryClient.invalidateQueries({ queryKey: getListLibrariesQueryKey() });
    onDone();
  };
  const onError = (error: ApiError) => {
    if (error.problem.code === 'library.invalid' && error.problem.errors !== undefined) setErrors(error.problem.errors);
  };
  const create = useCreateLibrary({ mutation: { onSuccess: done, onError } });
  const update = useUpdateLibrary({ mutation: { onSuccess: done, onError } });
  const mutation = library === undefined ? create : update;
  const busy = create.isPending || update.isPending;
  // Validation problems show on their fields; the rest (409 scan running, 403, the network) show here, and so does
  // a validation problem with no field to show it on, so Save never goes unanswered.
  const shown = (field: string) => {
    const root = /^rootPaths[(d+)]$/.exec(field);
    return root === null ? formFields.includes(field) : Number(root[1]) < roots.length;
  };
  const unshown =
    mutation.isError &&
    mutation.error.problem.code === 'library.invalid' &&
    (Object.keys(mutation.error.problem.errors ?? {}).length === 0 || !Object.keys(errors).every(shown));
  const problem =
    mutation.isError && (mutation.error.problem.code !== 'library.invalid' || unshown) ? mutation.error.problem : undefined;

  const clearError = (field: string) => {
    setErrors(({ [field]: _, ...rest }) => rest);
  };

  const editRoot = (index: number, path: string) => {
    setRoots((all) => all.map((root, i) => (i === index ? { ...root, path } : root)));
    clearError(rootKey(index));
  };
  const removeRoot = (index: number) => {
    setRoots((all) => all.filter((_, i) => i !== index));
    setErrors((all) => withoutRoot(all, index));
  };
  const addRoot = () => {
    const key = nextKey.current;
    nextKey.current += 1;
    setRoots((all) => [...all, { key, path: '' }]);
    clearError('rootPaths');
  };

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    const paths = roots.map((root) => root.path.trim());
    const found: FieldErrors = {};
    paths.forEach((path, i) => {
      if (path === '') found[rootKey(i)] = ['Enter the folder that holds your media.'];
    });
    if (library !== undefined && name.trim() === '') found.name = ['Enter a name for the library.'];
    setErrors(found);
    if (Object.keys(found).length > 0) return;

    if (library === undefined) {
      create.mutate({
        data: {
          contentType,
          rootPaths: paths,
          scanIntervalMinutes: interval,
          ...(name.trim() === '' ? {} : { name: name.trim() }),
        },
      });
      return;
    }
    const changes: LibraryUpdateRequest = {
      ...(name.trim() === library.name ? {} : { name: name.trim() }),
      ...(contentType === library.contentType ? {} : { contentType }),
      ...(sameRoots(paths, library.rootPaths) ? {} : { rootPaths: paths }),
      ...(interval === library.scanIntervalMinutes ? {} : { scanIntervalMinutes: interval }),
    };
    if (Object.keys(changes).length === 0) {
      onDone();
      return;
    }
    update.mutate({ libraryId: library.id, data: changes });
  };

  const presets = intervalPresets.some((p) => p.minutes === interval)
    ? intervalPresets
    : [...intervalPresets, { minutes: interval, label: intervalLabel(interval) }];

  return (
    <form
      aria-labelledby="library-form-heading"
      onSubmit={submit}
      noValidate
      className="flex max-w-2xl flex-col gap-4 rounded-card bg-surface p-6 shadow-soft"
    >
      <h2 id="library-form-heading" ref={heading} tabIndex={-1} className="text-xl font-semibold focus:outline-none">
        {library === undefined ? 'Add a library' : `Edit ${library.name}`}
      </h2>
      <p className="text-sm text-fg-muted">
        MediaMan only reads these folders; it never renames, moves or deletes anything in them.
      </p>

      <fieldset className="flex flex-col gap-2">
        <legend className="mb-1 text-sm text-fg-muted">Folders</legend>
        {roots.map((root, i) => (
          <div key={root.key} className="flex flex-col gap-1">
            <div className="flex gap-2">
              <input
                aria-label={`Folder ${String(i + 1)}`}
                value={root.path}
                onChange={(e) => { editRoot(i, e.target.value); }}
                placeholder="D:\Movies or /mnt/media/movies"
                className={`${inputClass} flex-1`}
              />
              {roots.length > 1 && (
                <button
                  type="button"
                  aria-label={`Remove folder ${String(i + 1)}`}
                  onClick={() => { removeRoot(i); }}
                  className={`${buttonClass} bg-bg text-fg`}
                >
                  Remove
                </button>
              )}
            </div>
            <FieldError messages={errors[rootKey(i)]} />
          </div>
        ))}
        <FieldError messages={errors.rootPaths} />
        {roots.length < maximumRoots && (
          <button type="button" onClick={addRoot} className={`${buttonClass} self-start bg-bg text-fg`}>
            Add another folder
          </button>
        )}
        <p className="text-xs text-fg-muted">
          {library === undefined
            ? 'Enter the full path to each folder.'
            : 'Removing a folder takes its titles out of MediaMan; the files stay on disk. Adding one starts a scan.'}
        </p>
      </fieldset>

      <Field label="Contains" field="contentType" errors={errors}>
        <select
          name="contentType"
          value={contentType}
          onChange={(e) => {
            setContentType(e.target.value as ContentType);
            clearError('contentType');
          }}
          className={inputClass}
        >
          {Object.entries(contentTypeLabels).map(([value, label]) => (
            <option key={value} value={value}>
              {label}
            </option>
          ))}
        </select>
      </Field>

      <Field
        label={library === undefined ? 'Name (optional)' : 'Name'}
        field="name"
        errors={errors}
        {...(library === undefined ? { hint: "Defaults to the first folder's name." } : {})}
      >
        <input
          name="name"
          maxLength={100}
          value={name}
          onChange={(e) => {
            setName(e.target.value);
            clearError('name');
          }}
          className={inputClass}
        />
      </Field>

      <Field
        label="Full scan"
        field="scanIntervalMinutes"
        errors={errors}
        hint="A full scan looks for new, changed and removed files."
      >
        <select
          name="scanIntervalMinutes"
          value={interval}
          onChange={(e) => {
            setScanInterval(Number(e.target.value));
            clearError('scanIntervalMinutes');
          }}
          className={inputClass}
        >
          {presets.map((p) => (
            <option key={p.minutes} value={p.minutes}>
              {p.label}
            </option>
          ))}
        </select>
      </Field>

      {problem !== undefined && (
        <p role="alert" className="text-sm">
          {problem.title} {problem.action}
        </p>
      )}

      <div className="flex justify-end gap-2">
        <button type="button" onClick={onDone} className={`${buttonClass} bg-bg text-fg`}>
          Cancel
        </button>
        <button type="submit" disabled={busy} className={`${buttonClass} bg-highlight text-bg`}>
          {busy ? 'Saving…' : library === undefined ? 'Add library' : 'Save'}
        </button>
      </div>
    </form>
  );
}
