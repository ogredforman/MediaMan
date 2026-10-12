import { useQueryClient } from '@tanstack/react-query';
import { useState, type SubmitEvent } from 'react';
import { getGetServerModeQueryKey, useGetServerMode, useSetServerMode } from '../api/mediaman';
import type { ServerMode } from '../api/mediaman.schemas';
import { buttonClass } from './formClasses';

const modes: { value: ServerMode; label: string; description: string }[] = [
  { value: 'local', label: 'This computer only', description: 'Only apps on this computer can use MediaMan.' },
  {
    value: 'server',
    label: 'My home network',
    description: 'Phones, TVs and other computers on your network can use MediaMan too.',
  },
];

/** Settings → Server: where the server listens, recorded now and used from the next restart (SPEC §7.1 step 3, §11). */
export function ServerSettings() {
  const queryClient = useQueryClient();
  const state = useGetServerMode();
  const save = useSetServerMode();
  const [picked, setPicked] = useState<ServerMode>();
  const mode = picked ?? state.data?.mode;

  const onSubmit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (mode === undefined) return;
    save.mutate(
      { data: { mode } },
      {
        onSuccess: (next) => {
          queryClient.setQueryData(getGetServerModeQueryKey(), next);
          setPicked(undefined);
        },
      },
    );
  };

  return (
    <section aria-labelledby="server-heading" className="flex flex-col gap-4">
      <h2 id="server-heading" className="text-xl font-semibold">
        Server
      </h2>
      {state.isPending && <p role="status">Loading the server mode…</p>}
      {state.isError && (
        <p role="alert">
          {state.error.problem.title} {state.error.problem.action}
        </p>
      )}
      {state.isSuccess && (
        <form onSubmit={onSubmit} className="flex flex-col gap-4 rounded-card bg-surface p-4">
          <fieldset className="flex flex-col gap-2">
            <legend className="mb-2 font-medium">Who can use this server</legend>
            {modes.map((option) => (
              <label
                key={option.value}
                className="grid cursor-pointer grid-cols-[auto_1fr] items-start gap-x-3 rounded-card bg-bg p-3"
              >
                <input
                  type="radio"
                  name="mode"
                  value={option.value}
                  checked={mode === option.value}
                  onChange={() => {
                    setPicked(option.value);
                  }}
                  className="row-span-2 mt-1"
                />
                <span className="font-medium">{option.label}</span>
                <span className="text-sm text-fg-muted">{option.description}</span>
              </label>
            ))}
          </fieldset>
          <p className="text-xs text-fg-muted">
            Either way, devices outside your network can't connect while remote access is off.
          </p>
          <div>
            <button
              type="submit"
              disabled={save.isPending || mode === state.data.mode}
              className={`${buttonClass} bg-highlight text-bg`}
            >
              Save mode
            </button>
          </div>
          {state.data.restartRequired && (
            <p role="status" className="rounded-card bg-bg p-3 text-sm">
              Restart the MediaMan server to use this mode. Until then it keeps listening as before.
            </p>
          )}
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
