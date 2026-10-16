import { createFileRoute, Link } from '@tanstack/react-router';
import { useEffect, useMemo } from 'react';
import { PlayerOverlay } from '../components/player/PlayerOverlay';
import { buttonClass } from '../components/formClasses';
import { desktopBridge } from '../lib/desktop';
import { createDesktopPlayerController } from '../lib/playerController';

// The player OSD, full screen and outside the shell. In the desktop app it is the page of the transparent overlay window
// above MPV (ADR 0011), driven over the bridge; it makes no API calls, so it needs no session.
export const Route = createFileRoute('/player')({
  component: PlayerRoute,
});

function PlayerRoute() {
  const bridge = desktopBridge();
  const controller = useMemo(() => (bridge ? createDesktopPlayerController(bridge.player) : null), [bridge]);

  useEffect(() => {
    if (controller === null) return;
    // The video plays under this page, so it mustn't paint the theme background.
    document.documentElement.classList.add('player-page');
    return () => {
      document.documentElement.classList.remove('player-page');
    };
  }, [controller]);

  if (controller === null) {
    return (
      <main className="flex min-h-screen flex-col items-center justify-center gap-4 p-6 text-center">
        <h1 className="text-xl font-semibold">Nothing is playing here</h1>
        <p className="max-w-md text-fg-muted">Play a title from its page; playback opens here in the MediaMan desktop app.</p>
        <Link to="/" className={`${buttonClass} bg-accent text-fg`}>
          Go to Home
        </Link>
      </main>
    );
  }
  return <PlayerOverlay controller={controller} />;
}
