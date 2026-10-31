import { useQueryClient } from '@tanstack/react-query';
import { useState, type SubmitEvent } from 'react';
import {
  getGetAccountQueryKey,
  getListProfilesQueryKey,
  useGetSignInOptions,
  useLogout,
  useSetAccountPassword,
} from '../api/mediaman';
import { passwordHint, passwordProblem } from '../lib/passwordRule';
import { clearTokens } from '../lib/session';
import { Field, type FieldErrors } from './FormField';
import { buttonClass } from './formClasses';
import { Logo } from './Logo';
import { PasswordInput } from './PasswordInput';
import { TitleBarStrip } from './TitleBarStrip';

const inputClass = 'rounded-card bg-bg px-3 py-2 text-fg';

/**
 * "Set a new password" (SPEC §7.11): a session with `passwordChangeRequired` can do nothing else until it sets a
 * password that meets the policy. The policy itself is out of its reach, so the rule comes from the sign-in options.
 * `onChanged` runs once the password is set and the session's cached answers are dropped.
 */
export function PasswordChangeForm({ onChanged }: { onChanged: () => void }) {
  const queryClient = useQueryClient();
  const options = useGetSignInOptions({ query: { retry: false }, request: { anonymous: true } });
  const policy = { requirePasswords: true, strongPasswords: options.data?.strongPasswords ?? false };
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [errors, setErrors] = useState<FieldErrors>({});

  const change = useSetAccountPassword({
    mutation: {
      onSuccess: async () => {
        // The probe and the account said the session was held back; ask again.
        queryClient.removeQueries({ queryKey: getListProfilesQueryKey() });
        await queryClient.invalidateQueries({ queryKey: getGetAccountQueryKey() });
        onChanged();
      },
      onError: (error) => {
        if (error.problem.errors !== undefined) setErrors(error.problem.errors);
      },
    },
  });
  const signOut = useLogout({ mutation: { onSettled: clearTokens } });

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    const problem = passwordProblem(policy, password);
    if (problem !== undefined) {
      setErrors({ newPassword: [problem] });
    } else if (password !== confirmPassword) {
      setErrors({ confirmPassword: ["The passwords don't match. Type the same password twice."] });
    } else {
      setErrors({});
      change.mutate({ data: { newPassword: password } });
    }
  };

  // Field errors show inline; anything else (e.g. the server is unreachable) shows under the form.
  const otherProblem = change.isError && change.error.problem.errors === undefined ? change.error.problem : undefined;

  return (
    <main className="flex min-h-screen items-center justify-center p-8">
      <TitleBarStrip />
      <section
        aria-labelledby="password-change-heading"
        className="flex w-full max-w-sm flex-col gap-4 rounded-card bg-surface p-8 shadow-soft"
      >
        <Logo size={64} className="self-center" />
        <h1 id="password-change-heading" className="text-2xl font-semibold">
          Set a new password
        </h1>
        <p className="text-sm text-fg-muted">
          Your password doesn&apos;t meet this server&apos;s password policy, or your account has none. Set one to
          carry on.
        </p>
        {options.isPending && <p className="text-sm text-fg-muted">Loading…</p>}
        {options.isError && (
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
        {options.isSuccess && (
          <form aria-label="Set a new password" onSubmit={submit} className="flex flex-col gap-4">
            <Field label="New password" field="newPassword" errors={errors} hint={passwordHint(policy)}>
              <PasswordInput
                name="newPassword"
                autoComplete="new-password"
                value={password}
                onChange={(e) => { setPassword(e.target.value); }}
                className={inputClass}
              />
            </Field>
            <Field label="Confirm new password" field="confirmPassword" errors={errors}>
              <PasswordInput
                name="confirmPassword"
                autoComplete="new-password"
                value={confirmPassword}
                onChange={(e) => { setConfirmPassword(e.target.value); }}
                className={inputClass}
              />
            </Field>
            <button type="submit" disabled={change.isPending} className={`${buttonClass} bg-highlight text-bg`}>
              {change.isPending ? 'Saving…' : 'Save and continue'}
            </button>
          </form>
        )}
        {otherProblem !== undefined && (
          <p role="alert" className="text-sm">
            {otherProblem.title} {otherProblem.action}
          </p>
        )}
        <button
          type="button"
          onClick={() => { signOut.mutate(); }}
          disabled={signOut.isPending}
          className={`${buttonClass} self-center bg-bg text-fg`}
        >
          Sign out
        </button>
      </section>
    </main>
  );
}
