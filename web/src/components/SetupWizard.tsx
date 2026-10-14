import { Link } from '@tanstack/react-router';
import { useEffect, useRef, useState, type ChangeEvent, type SubmitEvent } from 'react';
import { useCompleteSetup } from '../api/mediaman';
import type { ContentType, ServerMode } from '../api/mediaman.schemas';
import { storeTokens } from '../lib/session';
import { Field, FieldError, type FieldErrors } from './FormField';
import { buttonClass, inputClass } from './formClasses';
import { Logo } from './Logo';

/** Matches the server's rule (SPEC §7.1 step 2); the server checks it again. */
const minimumPasswordLength = 12;

type Step = 'account' | 'mode' | 'library';

const steps: { id: Step; title: string }[] = [
  { id: 'account', title: 'Create the owner account' },
  { id: 'mode', title: 'Choose who can connect' },
  { id: 'library', title: 'Add your first library' },
];

/** The step that owns a `setup.invalid` error field (`username`, `password`, `mode`, `library`, `library.*`). */
function stepOf(field: string): Step {
  if (field === 'mode') return 'mode';
  if (field === 'library' || field.startsWith('library.')) return 'library';
  return 'account';
}

/**
 * The first-run setup wizard (SPEC §7.1): one step per screen, with the whole setup sent in one `POST /setup`
 * from the last step. Errors the server reports per field come back to the step that owns the field.
 * `onComplete` runs once the new Superadmin's tokens are stored.
 */
export function SetupWizard({ onComplete }: { onComplete: () => void }) {
  const [step, setStep] = useState<Step>('account');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [mode, setMode] = useState<ServerMode>('local');
  const [path, setPath] = useState('');
  const [contentType, setContentType] = useState<ContentType>('mixed');
  const [name, setName] = useState('');
  const [errors, setErrors] = useState<FieldErrors>({});

  const setup = useCompleteSetup({
    mutation: {
      onSuccess: (tokens) => {
        storeTokens(tokens);
        onComplete();
      },
      onError: (error) => {
        const fieldErrors = error.problem.code === 'setup.invalid' ? error.problem.errors : undefined;
        if (fieldErrors === undefined) return;
        setErrors(fieldErrors);
        const first = steps.find((s) => Object.keys(fieldErrors).some((field) => stepOf(field) === s.id));
        if (first !== undefined) setStep(first.id);
      },
    },
    request: { anonymous: true },
  });

  const index = steps.findIndex((s) => s.id === step);
  const current = steps[index];

  // A new step replaces the screen, so focus moves to its heading for keyboard and screen-reader users.
  const heading = useRef<HTMLHeadingElement>(null);
  const firstRender = useRef(true);
  useEffect(() => {
    if (firstRender.current) {
      firstRender.current = false;
      return;
    }
    heading.current?.focus();
  }, [step]);

  // Editing a field clears its error; the next submit checks it again.
  const clearError = (field: string) => {
    setErrors((all) => Object.fromEntries(Object.entries(all).filter(([key]) => key !== field)));
  };
  const edit =
    (field: string, set: (value: string) => void) => (e: ChangeEvent<HTMLInputElement | HTMLSelectElement>) => {
      set(e.target.value);
      clearError(field);
    };

  // Checks what the browser can before moving on; the server has the final word on submit.
  const localErrors = (): FieldErrors => {
    const found: FieldErrors = {};
    if (step === 'account') {
      if (username.trim() === '') found.username = ['Enter a username.'];
      if (password.length < minimumPasswordLength) {
        found.password = [`Use a password of at least ${String(minimumPasswordLength)} characters.`];
      } else if (password !== confirmPassword) {
        found.confirmPassword = ["The passwords don't match. Type the same password twice."];
      }
    }
    if (step === 'library' && path.trim() === '') found['library.path'] = ['Enter the full path to a media folder.'];
    return found;
  };

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    const found = localErrors();
    // Errors on other steps stay until those fields are edited or the server answers again.
    const others = Object.fromEntries(Object.entries(errors).filter(([field]) => stepOf(field) !== step));
    setErrors({ ...others, ...found });
    if (Object.keys(found).length > 0) return;
    const next = steps[index + 1];
    if (next !== undefined) {
      setStep(next.id);
      return;
    }
    setup.mutate({
      data: {
        username: username.trim(),
        password,
        mode,
        library: { path: path.trim(), contentType, ...(name.trim() === '' ? {} : { name: name.trim() }) },
      },
    });
  };

  const back = () => {
    const previous = steps[index - 1];
    if (previous !== undefined) setStep(previous.id);
  };

  // Validation problems show on their fields; anything else (409, the network) shows here with its next action.
  const problem = setup.isError && setup.error.problem.code !== 'setup.invalid' ? setup.error.problem : undefined;

  return (
    <main className="flex min-h-screen items-center justify-center p-8">
      <form
        aria-labelledby="setup-heading"
        onSubmit={submit}
        noValidate
        className="flex w-full max-w-md flex-col gap-4 rounded-card bg-surface p-8 shadow-soft"
      >
        <div className="flex items-center gap-3">
          <Logo size={48} />
          <p className="text-sm text-fg-muted">
            Set up <span className="text-highlight">MediaMan</span>
          </p>
        </div>
        <ol aria-label="Setup progress" className="flex gap-2">
          {steps.map((s, i) => (
            <li
              key={s.id}
              aria-current={s.id === step ? 'step' : undefined}
              className={`h-1 flex-1 rounded-full ${i <= index ? 'bg-highlight' : 'bg-bg'}`}
            >
              <span className="sr-only">{s.title}</span>
            </li>
          ))}
        </ol>
        <p className="text-sm text-fg-muted">
          Step {index + 1} of {steps.length}
        </p>
        <h1 id="setup-heading" ref={heading} tabIndex={-1} className="text-2xl font-semibold focus:outline-none">
          {current?.title}
        </h1>

        {step === 'account' && (
          <>
            <p className="text-sm text-fg-muted">This account owns the server and can never be locked out of it.</p>
            <Field label="Username" field="username" errors={errors}>
              <input
                name="username"
                autoComplete="username"
                value={username}
                onChange={edit('username', setUsername)}
                className={inputClass}
              />
            </Field>
            <Field label="Password" field="password" errors={errors} hint={`At least ${String(minimumPasswordLength)} characters.`}>
              <input
                name="password"
                type="password"
                autoComplete="new-password"
                value={password}
                onChange={edit('password', setPassword)}
                className={inputClass}
              />
            </Field>
            <Field label="Confirm password" field="confirmPassword" errors={errors}>
              <input
                name="confirmPassword"
                type="password"
                autoComplete="new-password"
                value={confirmPassword}
                onChange={edit('confirmPassword', setConfirmPassword)}
                className={inputClass}
              />
            </Field>
          </>
        )}

        {step === 'mode' && (
          <fieldset className="flex flex-col gap-3">
            <legend className="mb-2 text-sm text-fg-muted">
              Either way, devices outside your home network can't connect until you turn on remote access in Settings.
            </legend>
            <ModeOption
              value="local"
              current={mode}
              onChange={(value) => { setMode(value); clearError('mode'); }}
              label="This computer only"
              description="Only apps on this computer can use MediaMan."
            />
            <ModeOption
              value="server"
              current={mode}
              onChange={(value) => { setMode(value); clearError('mode'); }}
              label="My home network"
              description="Phones, TVs and other computers on your network can use MediaMan too."
            />
            <FieldError messages={errors.mode} />
            <p className="text-sm text-fg-muted">You can change this later; it takes effect when the server restarts.</p>
          </fieldset>
        )}

        {step === 'library' && (
          <>
            <p className="text-sm text-fg-muted">
              MediaMan only reads this folder; it never renames, moves or deletes anything in it.
            </p>
            <FieldError messages={errors.library} />
            <Field label="Folder path" field="library.path" errors={errors} hint="For example D:\Movies or /mnt/media/movies.">
              <input
                name="path"
                value={path}
                onChange={edit('library.path', setPath)}
                className={inputClass}
              />
            </Field>
            <Field label="Contains" field="library.contentType" errors={errors}>
              <select
                name="contentType"
                value={contentType}
                onChange={edit('library.contentType', (value) => { setContentType(value as ContentType); })}
                className={inputClass}
              >
                <option value="mixed">Movies and shows</option>
                <option value="movies">Movies</option>
                <option value="shows">Shows</option>
                <option value="anime">Anime</option>
              </select>
            </Field>
            <Field label="Name (optional)" field="library.name" errors={errors} hint="Defaults to the folder's name.">
              <input
                name="name"
                value={name}
                onChange={edit('library.name', setName)}
                className={inputClass}
              />
            </Field>
          </>
        )}

        {problem !== undefined && (
          <div role="alert" className="flex flex-col gap-1 text-sm">
            <p>
              {problem.title} {problem.action}
            </p>
            {problem.code === 'setup.complete' && (
              <Link to="/sign-in" className="text-highlight underline">
                Go to sign in
              </Link>
            )}
          </div>
        )}

        <div className="flex justify-between gap-2">
          {index > 0 ? (
            <button type="button" onClick={back} className={`${buttonClass} bg-bg text-fg`}>
              Back
            </button>
          ) : (
            <span />
          )}
          <button type="submit" disabled={setup.isPending} className={`${buttonClass} bg-highlight text-bg`}>
            {index < steps.length - 1 ? 'Next' : setup.isPending ? 'Setting up…' : 'Finish setup'}
          </button>
        </div>
      </form>
    </main>
  );
}

function ModeOption({
  value,
  current,
  onChange,
  label,
  description,
}: {
  value: ServerMode;
  current: ServerMode;
  onChange: (mode: ServerMode) => void;
  label: string;
  description: string;
}) {
  return (
    <label className="grid cursor-pointer grid-cols-[auto_1fr] items-start gap-x-3 rounded-card bg-bg p-3">
      <input
        type="radio"
        name="mode"
        value={value}
        checked={current === value}
        onChange={() => { onChange(value); }}
        className="row-span-2 mt-1"
      />
      <span className="font-medium">{label}</span>
      <span className="text-sm text-fg-muted">{description}</span>
    </label>
  );
}
