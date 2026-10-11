import { http, HttpResponse } from 'msw';
import {
  LanguageCode as LanguageCodes,
  ProfileAvatar as Avatars,
  type CreateProfileRequest,
  type Profile,
  type ProfileAvatar,
  type ProfileList,
  type SelectProfileRequest,
  type UpdateProfileRequest,
} from '../api/mediaman.schemas';

const maximumProfiles = 10;
const maximumNameLength = 32;
const maximumLanguages = 5;
const avatars = Object.values(Avatars);
const languages: string[] = Object.values(LanguageCodes);

/** The server's rule for a language list: known codes, no repeats, at most five. */
function languageProblem(list: string[] | undefined): string | undefined {
  if (list === undefined) return undefined;
  if (list.length > maximumLanguages) return `Pick at most ${String(maximumLanguages)} languages.`;
  const unknown = list.find((code) => !languages.includes(code));
  if (unknown !== undefined) return `'${unknown}' isn't a language you can pick.`;
  return new Set(list).size === list.length ? undefined : 'List each language once.';
}

const problem = (body: Record<string, unknown> & { status: number }) =>
  HttpResponse.json(body, { status: body.status, headers: { 'Content-Type': 'application/problem+json' } });

const notFound = () =>
  problem({
    title: 'Profile not found.',
    status: 404,
    code: 'profile.not_found',
    detail: "The profile doesn't exist, or it was deleted.",
    action: 'Reload the profile list.',
  });

/** The first mock profile, picked until a sign-in or setup starts a session with none. */
export const mockProfile: Profile = {
  id: '01920000-0000-7000-8000-0000000000a1',
  name: 'owner',
  avatar: 'violet',
  audioLanguages: [],
  subtitleLanguages: [],
};

/**
 * The signed-in account's profiles, kept in memory with the server's rules (SPEC §7.11): names unique ignoring case,
 * at most 10, the last one kept, and deleting the picked profile clears the pick. `reset` restores one picked profile.
 */
export function createProfileStore() {
  let profiles: Profile[] = [];
  let activeProfileId: string | undefined;
  let nextId = 0;

  const reset = () => {
    profiles = [mockProfile];
    activeProfileId = mockProfile.id;
    nextId = 0;
  };
  reset();

  const list = (): ProfileList => ({ items: profiles, ...(activeProfileId === undefined ? {} : { activeProfileId }) });

  /** The `profile.invalid` or `profile.name_taken` answer for these fields, if they break a rule. */
  const check = (
    // The body as sent, which may break the contract: that is what the checks are for.
    {
      name,
      avatar,
      audioLanguages,
      subtitleLanguages,
    }: { name?: string; avatar?: string; audioLanguages?: string[]; subtitleLanguages?: string[] },
    nameRequired: boolean,
    ownId?: string,
  ) => {
    const errors: Record<string, string[]> = {};
    const trimmed = name?.trim();
    if (trimmed === undefined ? nameRequired : trimmed.length === 0 || trimmed.length > maximumNameLength) {
      errors.name = [`Enter a name of 1 to ${String(maximumNameLength)} characters.`];
    }
    if (avatar !== undefined && !avatars.includes(avatar as ProfileAvatar)) {
      errors.avatar = [`Pick one of the preset avatars: ${avatars.join(', ')}.`];
    }
    const audioProblem = languageProblem(audioLanguages);
    if (audioProblem !== undefined) errors.audioLanguages = [audioProblem];
    const subtitleProblem = languageProblem(subtitleLanguages);
    if (subtitleProblem !== undefined) errors.subtitleLanguages = [subtitleProblem];
    if (Object.keys(errors).length > 0) {
      return problem({
        title: 'Some profile details need fixing.',
        status: 400,
        code: 'profile.invalid',
        action: 'Fix the highlighted fields, then save again.',
        errors,
      });
    }
    if (trimmed !== undefined && profiles.some((p) => p.id !== ownId && p.name.toLowerCase() === trimmed.toLowerCase())) {
      return problem({
        title: 'That name is taken.',
        status: 409,
        code: 'profile.name_taken',
        detail: 'Another profile on this account already has that name.',
        action: 'Choose a different name.',
      });
    }
    return undefined;
  };

  const handlers = [
    http.get('*/profiles', () => HttpResponse.json(list())),
    http.post('*/profiles', async ({ request }) => {
      const body = (await request.json()) as CreateProfileRequest;
      const invalid = check(body, true);
      if (invalid !== undefined) return invalid;
      if (profiles.length >= maximumProfiles) {
        return problem({
          title: 'No room for another profile.',
          status: 409,
          code: 'profile.limit',
          detail: `An account can have at most ${String(maximumProfiles)} profiles.`,
          action: "Delete a profile you don't use, then add this one.",
        });
      }
      nextId += 1;
      const profile: Profile = {
        id: `01920000-0000-7000-8000-${String(nextId).padStart(12, '0')}`,
        name: body.name.trim(),
        avatar: body.avatar ?? avatars[profiles.length % avatars.length] ?? 'violet',
        audioLanguages: [],
        subtitleLanguages: [],
      };
      profiles = [...profiles, profile];
      return HttpResponse.json(profile, { status: 201 });
    }),
    http.patch('*/profiles/:profileId', async ({ params, request }) => {
      const existing = profiles.find((p) => p.id === params.profileId);
      if (existing === undefined) return notFound();
      const body = (await request.json()) as UpdateProfileRequest;
      const invalid = check(body, false, existing.id);
      if (invalid !== undefined) return invalid;
      const updated: Profile = {
        ...existing,
        name: body.name?.trim() ?? existing.name,
        avatar: body.avatar ?? existing.avatar,
        audioLanguages: body.audioLanguages ?? existing.audioLanguages,
        subtitleLanguages: body.subtitleLanguages ?? existing.subtitleLanguages,
      };
      profiles = profiles.map((p) => (p.id === existing.id ? updated : p));
      return HttpResponse.json(updated);
    }),
    http.delete('*/profiles/:profileId', ({ params }) => {
      if (!profiles.some((p) => p.id === params.profileId)) return notFound();
      if (profiles.length === 1) {
        return problem({
          title: 'This is the only profile.',
          status: 409,
          code: 'profile.last',
          detail: "An account always keeps at least one profile, so its last one can't be deleted.",
          action: 'Add another profile first, or rename this one instead.',
        });
      }
      profiles = profiles.filter((p) => p.id !== params.profileId);
      if (activeProfileId === params.profileId) activeProfileId = undefined;
      return new HttpResponse(null, { status: 204 });
    }),
    http.put('*/auth/session/profile', async ({ request }) => {
      const { profileId } = (await request.json()) as SelectProfileRequest;
      const profile = profiles.find((p) => p.id === profileId);
      if (profile === undefined) return notFound();
      activeProfileId = profile.id;
      return HttpResponse.json(profile);
    }),
  ];

  return {
    handlers,
    reset,
    /** A new session (sign-in, setup) starts with no profile picked. */
    clearPick: () => {
      activeProfileId = undefined;
    },
  };
}

export const mockProfiles = createProfileStore();
