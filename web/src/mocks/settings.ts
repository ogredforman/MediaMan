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
  type SetPasswordRequest,
  type SignInOptions,
} from '../api/mediaman.schemas';
import { passwordProblem } from '../lib/passwordRule';

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

const passwordChangeRequired = () =>
  problem({
    title: 'Set a new password first.',
    status: 403,
    code: 'auth.password_change_required',
    detail: "Your password doesn't meet this server's password policy, or your account has none while passwords are required.",
    action: 'Set a password that meets the policy, then carry on.',
  });

const passwordInvalid = (field: 'newPassword' | 'currentPassword', message: string) =>
  problem({
    title: 'Check the password.',
    status: 400,
    code: 'account.password_invalid',
    detail: "The password wasn't changed.",
    action: field === 'newPassword' ? 'Choose a password that meets the policy, then save.' : 'Enter your current password, then save.',
    errors: { [field]: [message] },
  });

const ownerOnly = () =>
  problem({
    title: "You don't have access to this.",
    status: 403,
    code: 'auth.forbidden',
    detail: 'Only the Superadmin can do this.',
    action: 'Ask the server owner.',
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
 * records; `requirePasswordChange` makes the session one that must set a new password (login tokens and `/account` say
 * so, `/profiles` answers `403` until `PUT /account/password` succeeds); `reset` after each test.
 */
export function createSettingsStore() {
  let account: Account = mockAccount;
  let runningMode: ServerMode = 'local';
  let recordedMode: ServerMode = 'local';
  let tokenSource: MetadataSettings['tmdbAccessTokenSource'] = 'shipped';
  let passwordPolicy = defaultPasswordPolicy;
  let mustChangePassword = false;

  const modeState = (): ServerModeState => ({ mode: recordedMode, restartRequired: recordedMode !== runningMode });
  const lacks = (permission: Permission) => !account.permissions.includes(permission);

  const reset = () => {
    account = mockAccount;
    runningMode = recordedMode = 'local';
    tokenSource = 'shipped';
    passwordPolicy = defaultPasswordPolicy;
    mustChangePassword = false;
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
    requirePasswordChange() {
      mustChangePassword = true;
    },
    get passwordChangeRequired() {
      return mustChangePassword;
    },
    /** Ahead of the profile store's handlers: a session that must change its password gets nothing else (SPEC §7.11). */
    passwordChangeHandlers: [
      http.all('*/profiles', () => (mustChangePassword ? passwordChangeRequired() : undefined)),
      http.all('*/profiles/*', () => (mustChangePassword ? passwordChangeRequired() : undefined)),
      http.put('*/auth/session/profile', () => (mustChangePassword ? passwordChangeRequired() : undefined)),
    ],
    handlers: [
      http.get('*/account', () => HttpResponse.json({ ...account, passwordChangeRequired: mustChangePassword })),
      // The server's rule; the current password is asked for except in a session that must change it.
      http.put('*/account/password', async ({ request }) => {
        const { currentPassword, newPassword } = (await request.json()) as SetPasswordRequest;
        if (newPassword === '') return passwordInvalid('newPassword', 'Enter a new password.');
        const failure = passwordProblem(passwordPolicy, newPassword);
        if (failure !== undefined) return passwordInvalid('newPassword', failure);
        if (!mustChangePassword && account.hasPassword && (currentPassword ?? '') === '') {
          return passwordInvalid('currentPassword', 'Enter your current password.');
        }
        mustChangePassword = false;
        return new HttpResponse(null, { status: 204 });
      }),
      // Accounts to pick from are listed only while passwords are off, like the server.
      http.get('*/auth/sign-in-options', () =>
        HttpResponse.json<SignInOptions>(
          passwordPolicy.requirePasswords
            ? { passwordsRequired: true, strongPasswords: passwordPolicy.strongPasswords }
            : {
                passwordsRequired: false,
                strongPasswords: passwordPolicy.strongPasswords,
                accounts: [{ username: account.username }],
              },
        ),
      ),
      // The Superadmin's alone, whatever a role grants (SPEC §9).
      http.get('*/settings/password-policy', () =>
        account.isSuperadmin ? HttpResponse.json(passwordPolicy) : ownerOnly(),
      ),
      http.put('*/settings/password-policy', async ({ request }) => {
        if (!account.isSuperadmin) return ownerOnly();
        passwordPolicy = (await request.json()) as PasswordPolicy;
        return HttpResponse.json(passwordPolicy);
      }),
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
