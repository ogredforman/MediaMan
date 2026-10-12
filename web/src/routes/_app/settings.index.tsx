import { createFileRoute, redirect } from '@tanstack/react-router';
import { accountQueryOptions } from '../../lib/account';
import { visibleSections } from '../../lib/settingsSections';

// The first section the account may use; if the account can't be loaded, Playback, which every user has.
export const Route = createFileRoute('/_app/settings/')({
  beforeLoad: async ({ context }) => {
    const account = await context.queryClient.query({ ...accountQueryOptions(), retry: false }).catch(() => undefined);
    const first = visibleSections(account)[0]?.to ?? '/settings/playback';
    // eslint-disable-next-line @typescript-eslint/only-throw-error -- the router's redirect is thrown by design.
    throw redirect({ to: first, replace: true });
  },
});
