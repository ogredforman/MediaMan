import { createFileRoute } from '@tanstack/react-router';
import { ServerSettings } from '../../components/ServerSettings';
import { SettingsGate } from '../../components/SettingsGate';

export const Route = createFileRoute('/_app/settings/server')({
  component: () => (
    <SettingsGate permission="server.control" name="Server">
      <ServerSettings />
    </SettingsGate>
  ),
});
