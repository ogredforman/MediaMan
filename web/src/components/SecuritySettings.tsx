import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type SubmitEvent } from 'react';
import { getGetPasswordPolicyQueryKey, useGetPasswordPolicy, useSetPasswordPolicy } from '../api/mediaman';
import type { PasswordPolicy } from '../api/mediaman.schemas';
import { passwordHint } from '../lib/passwordRule';
import { CheckboxOption } from './FormField';
import { buttonClass } from './formClasses';

const samePolicy = (a: PasswordPolicy, b: PasswordPolicy) =>
  a.requirePasswords === b.requirePasswords && a.strongPasswords === b.strongPasswords;

/**
 * Settings → Security, the Superadmin's alone: the password policy, used from each account's next sign-in. Turning
 * passwords off asks first, since anyone who can reach the server can then sign in as any account (SPEC §7.11, §11).
 */
export function SecuritySettings() {
  const queryClient = useQueryClient();
  const state = useGetPasswordPolicy();
  const save = useSetPasswordPolicy();
  const [picked, setPicked] = useState<PasswordPolicy>();
  const [confirmingOff, setConfirmingOff] = useState(false);
  const [saved, setSaved] = useState(false);
  const policy = picked ?? state.data;
  const keepOn = useRef<HTMLButtonElement>(null);

  // The confirmation takes focus on its safe answer, so Enter doesn't turn passwords off by accident.
  useEffect(() => {
    if (confirmingOff) keepOn.current?.focus();
  }, [confirmingOff]);

  const choose = (option: keyof PasswordPolicy, checked: boolean) => {
    if (policy === undefined) return;
    setPicked({ ...policy, [option]: checked });
    setConfirmingOff(false);
    setSaved(false);
    save.reset();
  };

  const send = (next: PasswordPolicy) => {
    save.mutate(
      { data: next },
      {
        onSuccess: (stored) => {
          queryClient.setQueryData(getGetPasswordPolicyQueryKey(), stored);
          setPicked(undefined);
          setConfirmingOff(false);
          setSaved(true);
        },
      },
    );
  };

  const onSubmit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (policy === undefined || state.data === undefined) return;
    if (state.data.requirePasswords && !policy.requirePasswords) setConfirmingOff(true);
    else send(policy);
  };

  return (
    <section aria-labelledby="security-heading" className="flex flex-col gap-4">
      <h2 id="security-heading" className="text-xl font-semibold">
        Security
      </h2>
      {state.isPending && <p role="status">Loading the password policy…</p>}
      {state.isError && (
        <p role="alert">
          {state.error.problem.title} {state.error.problem.action}
        </p>
      )}
      {state.isSuccess && policy !== undefined && (
        <form onSubmit={onSubmit} className="flex flex-col gap-4 rounded-card bg-surface p-4">
          <fieldset className="flex flex-col gap-2">
            <legend className="mb-2 font-medium">Passwords on this server</legend>
            <CheckboxOption
              name="requirePasswords"
              checked={policy.requirePasswords}
              onChange={(e) => {
                choose('requirePasswords', e.target.checked);
              }}
              label="Require passwords"
              description="Turn this off and anyone who can reach the server signs in by picking an account, with no password. Passwords already set are kept for when you turn it back on, and accounts without one set one at their next sign-in."
            />
            <CheckboxOption
              name="strongPasswords"
              checked={policy.strongPasswords}
              onChange={(e) => {
                choose('strongPasswords', e.target.checked);
              }}
              label="Strong passwords"
              description="Ask for longer passwords that mix kinds of characters. An account whose password is too weak sets a new one at its next sign-in."
            />
          </fieldset>
          <p className="text-xs text-fg-muted">{passwordHint(policy)}</p>
          <div>
            <button
              type="submit"
              disabled={save.isPending || samePolicy(policy, state.data)}
              className={`${buttonClass} bg-highlight text-bg`}
            >
              Save
            </button>
          </div>
          {confirmingOff && (
            <div
              role="alertdialog"
              aria-labelledby="passwords-off-heading"
              className="flex flex-col gap-2 rounded-card bg-bg p-3"
            >
              <p id="passwords-off-heading">
                Turn passwords off? Anyone who can reach this server can then sign in as any account, including yours.
              </p>
              <div className="flex gap-2">
                <button
                  type="button"
                  disabled={save.isPending}
                  onClick={() => {
                    send(policy);
                  }}
                  className={`${buttonClass} bg-highlight text-bg`}
                >
                  Turn passwords off
                </button>
                <button
                  ref={keepOn}
                  type="button"
                  onClick={() => {
                    setConfirmingOff(false);
                  }}
                  className={`${buttonClass} bg-surface`}
                >
                  Keep them on
                </button>
              </div>
            </div>
          )}
          {saved && <p role="status">Saved. It applies from each account's next sign-in.</p>}
          {save.isError && (
            <p role="alert">
              {save.error.problem.title} {save.error.problem.action}
            </p>
          )}
        </form>
      )}
    </section>
  );
}
