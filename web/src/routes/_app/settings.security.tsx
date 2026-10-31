import { createFileRoute } from '@tanstack/react-router';
import { SecuritySettings } from '../../components/SecuritySettings';
import { SettingsGate } from '../../components/SettingsGate';

export const Route = createFileRoute('/_app/settings/security')({
  component: () => (
    <SettingsGate superadminOnly name="Security">
      <SecuritySettings />
    </SettingsGate>
  ),
});
