import { useEffect, useState, type SubmitEvent } from 'react';
import { useGetSignInOptions, useLogin } from '../api/mediaman';
import type { SignInAccount } from '../api/mediaman.schemas';
import { storeTokens } from '../lib/session';
import { Field } from './FormField';
import { buttonClass } from './formClasses';
import { Logo } from './Logo';
import { PasswordInput } from './PasswordInput';
import { TitleBarStrip } from './TitleBarStrip';

const inputClass = 'rounded-card bg-bg px-3 py-2 text-fg';

/**
 * Sign-in (SPEC §7.11 step 1), as `GET /auth/sign-in-options` says: a username and password, or while passwords are
 * off, the accounts to pick from. `onSignedIn` runs once the tokens are stored, and `onSetupRequired` when the server
 * has no account yet (`423 setup.required`).
 */
export function SignInForm({ onSignedIn, onSetupRequired }: { onSignedIn: () => void; onSetupRequired: () => void }) {
  // No retries: a 423 goes straight to setup, and anything else offers Try again.
  const options = useGetSignInOptions({ query: { retry: false }, request: { anonymous: true } });
  const signIn = useLogin({
    mutation: {
      onSuccess: (tokens) => {
        storeTokens(tokens);
        onSignedIn();
      },
      onError: (error) => {
        if (error.problem.code === 'setup.required') onSetupRequired();
      },
    },
    request: { anonymous: true },
  });

  const setupRequired = options.error?.problem.code === 'setup.required';
  useEffect(() => {
    if (setupRequired) onSetupRequired();
  }, [setupRequired, onSetupRequired]);

  return (
    <main className="flex min-h-screen items-center justify-center p-8">
      <TitleBarStrip />
      <section
        aria-labelledby="sign-in-heading"
        className="flex w-full max-w-sm flex-col gap-4 rounded-card bg-surface p-8 shadow-soft"
      >
        <Logo size={64} className="self-center" />
        <h1 id="sign-in-heading" className="text-2xl font-semibold">
          Sign in to <span className="text-highlight">MediaMan</span>
        </h1>
        {options.isPending && <p className="text-sm text-fg-muted">Loading…</p>}
        {options.isError && !setupRequired && (
          <div role="alert" className="flex flex-col items-start gap-2 text-sm">
            <p>
              {options.error.problem.title} {options.error.problem.action}
            </p>
            <button
              type="button"
              onClick={() => { void options.refetch(); }}
              className={`${buttonClass} bg-bg text-fg`}
            >
              Try again
            </button>
          </div>
        )}
        {options.data?.passwordsRequired === true && (
          <PasswordForm
            pending={signIn.isPending}
            onSubmit={(username, password) => { signIn.mutate({ data: { username, password } }); }}
          />
        )}
        {options.data?.passwordsRequired === false && (
          <AccountList
            accounts={options.data.accounts ?? []}
            pending={signIn.isPending}
            onPick={(username) => { signIn.mutate({ data: { username } }); }}
          />
        )}
        {signIn.isError && (
          <p role="alert" className="text-sm">
            {signIn.error.problem.title} {signIn.error.problem.action}
          </p>
        )}
      </section>
    </main>
  );
}

function PasswordForm({ pending, onSubmit }: { pending: boolean; onSubmit: (username: string, password: string) => void }) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    onSubmit(username, password);
  };

  return (
    <form aria-label="Sign in with a password" onSubmit={submit} className="flex flex-col gap-4">
      <label className="flex flex-col gap-1 text-sm text-fg-muted">
        Username
        <input
          name="username"
          autoComplete="username"
          required
          value={username}
          onChange={(e) => { setUsername(e.target.value); }}
          className={inputClass}
        />
      </label>
      <Field label="Password" field="password" errors={{}}>
        <PasswordInput
          name="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(e) => { setPassword(e.target.value); }}
          className={inputClass}
        />
      </Field>
      <button type="submit" disabled={pending} className={`${buttonClass} bg-highlight text-bg`}>
        {pending ? 'Signing in…' : 'Sign in'}
      </button>
    </form>
  );
}

/** Passwords are off: picking an account signs in with its username alone. */
function AccountList({
  accounts,
  pending,
  onPick,
}: {
  accounts: SignInAccount[];
  pending: boolean;
  onPick: (username: string) => void;
}) {
  if (accounts.length === 0) {
    return (
      <p className="text-sm text-fg-muted">
        There are no accounts to sign in with. Ask the server&apos;s owner to enable your account.
      </p>
    );
  }
  return (
    <>
      <p className="text-sm text-fg-muted">Pick your account.</p>
      <ul aria-label="Accounts" className="flex flex-col gap-2">
        {accounts.map((account) => (
          <li key={account.username}>
            <button
              type="button"
              disabled={pending}
              onClick={() => { onPick(account.username); }}
              className={`${buttonClass} w-full bg-bg text-left text-fg`}
            >
              {account.username}
            </button>
          </li>
        ))}
      </ul>
    </>
  );
}
