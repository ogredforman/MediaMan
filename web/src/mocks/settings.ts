import { http, HttpResponse } from 'msw';
import {
  Permission as Permissions,
  type Account,
  type MetadataSettings,
  type MetadataSettingsRequest,
  type PasswordPolicy,
  type Permission,
  type ServerMode,
  type ServerModeRequest,
  type ServerModeState,
  type SignInOptions,
} from '../api/mediaman.schemas';

const problem = (body: Record<string, unknown> & { status: number }) =>
  HttpResponse.json(body, { status: body.status, headers: { 'Content-Type': 'application/problem+json' } });

const forbidden = (permission: Permission) =>
  problem({
    title: "You don't have access to this.",
    status: 403,
    code: 'auth.forbidden',
    detail: `Your account doesn't have the '${permission}' permission this needs.`,
    action: 'Ask an admin to give your role this permission.',
  });

const invalidToken = (message: string) =>
  problem({
    title: 'Check the TMDB access token.',
    status: 400,
    code: 'bad_request',
    detail: "The TMDB access token can't be used.",
    action: 'Paste the API Read Access Token from your TMDB account, then save.',
    errors: { tmdbAccessToken: [message] },
  });

/** The server's token rules (`MetadataSettingsService.Validate`); undefined when the token can be saved. */
function tokenProblem(token: string): string | undefined {
  if (token === '') return undefined;
  if (token.length > 1000 || !/^[A-Za-z0-9._~+/=-]+$/.test(token)) {
    return "That isn't a TMDB read-access token. Copy the API Read Access Token from your TMDB account's API page.";
  }
  return /^[0-9a-fA-F]{32}$/.test(token)
    ? "That's the short API key. Paste the longer API Read Access Token from the same TMDB page instead."
    : undefined;
}

/** The signed-in Superadmin, holding every permission. */
export const mockAccount: Account = {
  id: '01920000-0000-7000-8000-0000000000b1',
  username: 'owner',
  isSuperadmin: true,
  permissions: Object.values(Permissions),
  hasPassword: true,
  passwordChangeRequired: false,
};

/** A new install's password policy (SPEC §7.11). */
export const defaultPasswordPolicy: PasswordPolicy = { requirePasswords: true, strongPasswords: false };

/**
 * The signed-in account, the recorded server mode, the TMDB token source and the password policy, with the server's
 * permission checks and rules. `setPermissions` makes the account an ordinary user; `setPasswordPolicy` is what setup
 * records; `reset` after each test.
 */
export function createSettingsStore() {
  let account: Account = mockAccount;
  let runningMode: ServerMode = 'local';
  let recordedMode: ServerMode = 'local';
  let tokenSource: MetadataSettings['tmdbAccessTokenSource'] = 'shipped';
  let passwordPolicy = defaultPasswordPolicy;

  const modeState = (): ServerModeState => ({ mode: recordedMode, restartRequired: recordedMode !== runningMode });
  const lacks = (permission: Permission) => !account.permissions.includes(permission);

  const reset = () => {
    account = mockAccount;
    runningMode = recordedMode = 'local';
    tokenSource = 'shipped';
    passwordPolicy = defaultPasswordPolicy;
  };
  reset();

  return {
    reset,
    setPermissions(permissions: Permission[]) {
      account = { ...mockAccount, isSuperadmin: false, permissions };
    },
    setPasswordPolicy(policy: PasswordPolicy) {
      passwordPolicy = policy;
    },
    handlers: [
      http.get('*/account', () => HttpResponse.json(account)),
      // Accounts to pick from are listed only while passwords are off, like the server.
      http.get('*/auth/sign-in-options', () =>
        HttpResponse.json<SignInOptions>(
          passwordPolicy.requirePasswords
            ? { passwordsRequired: true }
            : { passwordsRequired: false, accounts: [{ username: account.username }] },
        ),
      ),
      http.get('*/server/mode', () => (lacks('server.control') ? forbidden('server.control') : HttpResponse.json(modeState()))),
      http.put('*/server/mode', async ({ request }) => {
        if (lacks('server.control')) return forbidden('server.control');
        recordedMode = ((await request.json()) as ServerModeRequest).mode;
        return HttpResponse.json(modeState());
      }),
      http.get('*/settings/metadata', () =>
        lacks('settings.manage') ? forbidden('settings.manage') : HttpResponse.json({ tmdbAccessTokenSource: tokenSource }),
      ),
      http.put('*/settings/metadata', async ({ request }) => {
        if (lacks('settings.manage')) return forbidden('settings.manage');
        const token = ((await request.json()) as MetadataSettingsRequest).tmdbAccessToken.trim();
        const message = tokenProblem(token);
        if (message !== undefined) return invalidToken(message);
        tokenSource = token === '' ? 'shipped' : 'settings';
        return HttpResponse.json({ tmdbAccessTokenSource: tokenSource });
      }),
    ],
  };
}

export const mockSettings = createSettingsStore();
