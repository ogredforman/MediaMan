import { createFileRoute } from '@tanstack/react-router';
import { LibrarySettings } from '../../components/LibrarySettings';
import { SettingsGate } from '../../components/SettingsGate';

export const Route = createFileRoute('/_app/settings/libraries')({
  component: () => (
    <SettingsGate permission="library.manage" name="Libraries">
      <LibrarySettings />
    </SettingsGate>
  ),
});
