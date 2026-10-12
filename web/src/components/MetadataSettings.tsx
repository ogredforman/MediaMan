import { useQueryClient } from '@tanstack/react-query';
import { useState, type SubmitEvent } from 'react';
import { getGetMetadataSettingsQueryKey, useGetMetadataSettings, useSetMetadataSettings } from '../api/mediaman';
import type { MetadataSettings as MetadataSettingsState } from '../api/mediaman.schemas';
import { Field, type FieldErrors } from './FormField';
import { buttonClass, inputClass } from './formClasses';

const sourceLabels: Record<MetadataSettingsState['tmdbAccessTokenSource'], string> = {
  shipped: 'The token that ships with MediaMan',
  configuration: 'The server configuration (MediaMan:Metadata:Tmdb:AccessToken)',
  settings: 'Your own token, set here',
};

/** Settings → Metadata: where the TMDB token comes from, and an override of your own (SPEC §8 metadata [M], §11). */
export function MetadataSettings() {
  const queryClient = useQueryClient();
  const settings = useGetMetadataSettings();
  const save = useSetMetadataSettings();
  const [token, setToken] = useState('');
  const [errors, setErrors] = useState<FieldErrors>({});
  const [confirmingClear, setConfirmingClear] = useState(false);

  const send = (tmdbAccessToken: string) => {
    setErrors({});
    save.mutate(
      { data: { tmdbAccessToken } },
      {
        onSuccess: (next) => {
          queryClient.setQueryData(getGetMetadataSettingsQueryKey(), next);
          setToken('');
          setConfirmingClear(false);
        },
        onError: (error) => {
          if (error.problem.errors?.tmdbAccessToken !== undefined) setErrors(error.problem.errors);
        },
      },
    );
  };

  const onSubmit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (token.trim() === '') {
      setErrors({ tmdbAccessToken: ['Paste a TMDB API Read Access Token first.'] });
      return;
    }
    send(token);
  };

  const unshownProblem = save.isError && save.error.problem.errors?.tmdbAccessToken === undefined ? save.error.problem : undefined;

  return (
    <section aria-labelledby="metadata-heading" className="flex flex-col gap-4">
      <h2 id="metadata-heading" className="text-xl font-semibold">
        Metadata
      </h2>
      <div className="flex flex-col gap-4 rounded-card bg-surface p-4">
        <h3 className="font-medium">TMDB</h3>
        {settings.isPending && <p role="status">Loading the metadata settings…</p>}
        {settings.isError && (
          <p role="alert">
            {settings.error.problem.title} {settings.error.problem.action}
          </p>
        )}
        {settings.isSuccess && (
          <p className="text-sm">
            <span className="text-fg-muted">Token in use: </span>
            {sourceLabels[settings.data.tmdbAccessTokenSource]}
          </p>
        )}
        <form onSubmit={onSubmit} className="flex flex-col gap-3" noValidate>
          <Field
            label="Your TMDB access token"
            field="tmdbAccessToken"
            errors={errors}
            hint="The API Read Access Token from your TMDB account's API page. It's stored encrypted and never shown again."
          >
            <input
              type="password"
              autoComplete="off"
              value={token}
              onChange={(e) => {
                setToken(e.target.value);
              }}
              className={inputClass}
            />
          </Field>
          <div className="flex flex-wrap gap-2">
            <button type="submit" disabled={save.isPending} className={`${buttonClass} bg-highlight text-bg`}>
              Save token
            </button>
            {settings.data?.tmdbAccessTokenSource === 'settings' && !confirmingClear && (
              <button
                type="button"
                disabled={save.isPending}
                onClick={() => {
                  setConfirmingClear(true);
                }}
                className={`${buttonClass} bg-bg`}
              >
                Remove my token
              </button>
            )}
          </div>
        </form>
        {confirmingClear && (
          <div role="alertdialog" aria-labelledby="clear-token-heading" className="flex flex-col gap-2 rounded-card bg-bg p-3">
            <p id="clear-token-heading">
              Remove your TMDB token? It's deleted from this server, and MediaMan goes back to the configured or shipped token.
            </p>
            <div className="flex gap-2">
              <button
                type="button"
                disabled={save.isPending}
                onClick={() => {
                  send('');
                }}
                className={`${buttonClass} bg-highlight text-bg`}
              >
                Remove token
              </button>
              <button
                type="button"
                onClick={() => {
                  setConfirmingClear(false);
                }}
                className={`${buttonClass} bg-surface`}
              >
                Keep it
              </button>
            </div>
          </div>
        )}
        {unshownProblem !== undefined && (
          <p role="alert">
            {unshownProblem.title} {unshownProblem.action}
          </p>
        )}
      </div>
    </section>
  );
}
