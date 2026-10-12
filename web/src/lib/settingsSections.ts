import type { Account, Permission } from '../api/mediaman.schemas';
import { hasPermission } from './account';

export interface SettingsSection {
  to: '/settings/libraries' | '/settings/playback' | '/settings/metadata' | '/settings/server' | '/settings/about';
  label: string;
  /** What the section needs; undefined means every signed-in user sees it. */
  permission?: Permission;
}

/** Settings, in nav order (SPEC §11); more sections join as their stages land. */
export const settingsSections: readonly SettingsSection[] = [
  { to: '/settings/libraries', label: 'Libraries', permission: 'library.manage' },
  { to: '/settings/playback', label: 'Playback' },
  { to: '/settings/metadata', label: 'Metadata', permission: 'settings.manage' },
  { to: '/settings/server', label: 'Server', permission: 'server.control' },
  { to: '/settings/about', label: 'About and licences' },
];

/** The sections `account` may use; while it loads, only the ones that need no permission. */
export function visibleSections(account: Account | undefined): SettingsSection[] {
  return settingsSections.filter((section) => hasPermission(account, section.permission));
}
