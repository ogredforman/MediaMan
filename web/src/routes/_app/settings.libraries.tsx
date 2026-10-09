import { createFileRoute } from '@tanstack/react-router';
import { LibrarySettings } from '../../components/LibrarySettings';

export const Route = createFileRoute('/_app/settings/libraries')({
  component: LibrarySettings,
});
