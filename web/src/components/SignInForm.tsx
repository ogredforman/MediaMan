import { useState, type SubmitEvent } from 'react';
import { useLogin } from '../api/mediaman';
import { storeTokens } from '../lib/session';

const inputClass = 'rounded-card bg-bg px-3 py-2 text-fg';

/** Username and password sign-in (SPEC §7.11 step 1); `onSignedIn` runs once the tokens are stored. */
export function SignInForm({ onSignedIn }: { onSignedIn: () => void }) {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const signIn = useLogin({
    mutation: {
      onSuccess: (tokens) => {
        storeTokens(tokens);
        onSignedIn();
      },
    },
    request: { anonymous: true },
  });

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    signIn.mutate({ data: { username, password } });
  };

  return (
    <main className="flex min-h-screen items-center justify-center p-8">
      <form
        aria-labelledby="sign-in-heading"
        onSubmit={submit}
        className="flex w-full max-w-sm flex-col gap-4 rounded-card bg-surface p-8 shadow-soft"
      >
        <h1 id="sign-in-heading" className="text-2xl font-semibold">
          Sign in to <span className="text-highlight">MediaMan</span>
        </h1>
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
        <label className="flex flex-col gap-1 text-sm text-fg-muted">
          Password
          <input
            name="password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(e) => { setPassword(e.target.value); }}
            className={inputClass}
          />
        </label>
        {signIn.isError && (
          <p role="alert" className="text-sm">
            {signIn.error.problem.title} {signIn.error.problem.action}
          </p>
        )}
        <button
          type="submit"
          disabled={signIn.isPending}
          className="rounded-card bg-highlight px-4 py-2 font-medium text-bg transition-opacity duration-(--motion-fast) hover:opacity-90 disabled:opacity-50"
        >
          {signIn.isPending ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </main>
  );
}
