import { createFileRoute } from '@tanstack/react-router';
import { PlaybackSettings } from '../../components/PlaybackSettings';

export const Route = createFileRoute('/_app/settings/playback')({
  component: PlaybackSettings,
});
