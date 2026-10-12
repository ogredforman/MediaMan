import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { desktopBridge, type DesktopMpvSetting, type DesktopPlayerBridge } from '../lib/desktop';
import { buttonClass } from './formClasses';

const mpvSettingKey = ['desktop', 'mpvSetting'] as const;

const sourceLabels: Record<DesktopMpvSetting['source'], string> = {
  picked: 'Picked in Settings',
  environment: 'From the MEDIAMAN_MPV_PATH environment variable',
  default: 'The default: mpv from your PATH',
};

/** Settings → Playback: the MPV the desktop app plays with (SPEC §7.4 step 6, §11). MPV exists only in the desktop app. */
export function PlaybackSettings() {
  const player = desktopBridge()?.player;
  return (
    <section aria-labelledby="playback-heading" className="flex flex-col gap-4">
      <h2 id="playback-heading" className="text-xl font-semibold">
        Playback
      </h2>
      {player === undefined ? (
        <p className="text-fg-muted">
          The MPV player settings are in the MediaMan desktop app, which plays through MPV. Open Settings → Playback there to
          choose which MPV it uses.
        </p>
      ) : (
        <MpvSettings player={player} />
      )}
    </section>
  );
}

function MpvSettings({ player }: { player: DesktopPlayerBridge }) {
  const queryClient = useQueryClient();
  const setting = useQuery({ queryKey: mpvSettingKey, queryFn: () => player.mpvSetting() });
  const choose = useMutation({
    mutationFn: () => player.chooseMpv(),
    onSuccess: (picked) => {
      if (picked !== null) void queryClient.invalidateQueries({ queryKey: mpvSettingKey });
    },
  });
  const resetMpv = useMutation({
    mutationFn: () => player.useDefaultMpv(),
    onSuccess: (next) => {
      queryClient.setQueryData(mpvSettingKey, next);
    },
  });
  const busy = choose.isPending || resetMpv.isPending;

  return (
    <div className="flex flex-col gap-4 rounded-card bg-surface p-4">
      <h3 className="font-medium">MPV player</h3>
      {setting.isPending && <p role="status">Loading the MPV setting…</p>}
      {setting.isError && (
        <p role="alert">Couldn't read which MPV the desktop app uses. Restart the desktop app, then open this page again.</p>
      )}
      {setting.isSuccess && (
        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
          <dt className="text-fg-muted">In use</dt>
          <dd className="break-all font-mono">{setting.data.path}</dd>
          <dt className="text-fg-muted">Source</dt>
          <dd>{sourceLabels[setting.data.source]}</dd>
        </dl>
      )}
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          disabled={busy}
          onClick={() => {
            choose.mutate();
          }}
          className={`${buttonClass} bg-highlight text-bg`}
        >
          Choose MPV…
        </button>
        <button
          type="button"
          disabled={busy || setting.data?.source !== 'picked'}
          onClick={() => {
            resetMpv.mutate();
          }}
          className={`${buttonClass} bg-bg`}
        >
          Use default
        </button>
      </div>
      {choose.isError && <p role="alert">Couldn't save the MPV choice. Press Choose MPV to try again.</p>}
      {resetMpv.isError && <p role="alert">Couldn't go back to the default MPV. Press Use default to try again.</p>}
      <p className="text-xs text-fg-muted">The next Play uses this MPV.</p>
    </div>
  );
}
