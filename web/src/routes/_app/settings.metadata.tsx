import { createFileRoute } from '@tanstack/react-router';
import { MetadataSettings } from '../../components/MetadataSettings';
import { SettingsGate } from '../../components/SettingsGate';

export const Route = createFileRoute('/_app/settings/metadata')({
  component: () => (
    <SettingsGate permission="settings.manage" name="Metadata">
      <MetadataSettings />
    </SettingsGate>
  ),
});
