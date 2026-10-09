import { http, HttpResponse } from 'msw';
import {
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
const avatars = Object.values(Avatars);

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
export const mockProfile: Profile = { id: '01920000-0000-7000-8000-0000000000a1', name: 'owner', avatar: 'violet' };

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
  const check = (name: string | undefined, avatar: string | undefined, nameRequired: boolean, ownId?: string) => {
    const errors: Record<string, string[]> = {};
    const trimmed = name?.trim();
    if (trimmed === undefined ? nameRequired : trimmed.length === 0 || trimmed.length > maximumNameLength) {
      errors.name = [`Enter a name of 1 to ${String(maximumNameLength)} characters.`];
    }
    if (avatar !== undefined && !avatars.includes(avatar as ProfileAvatar)) {
      errors.avatar = [`Pick one of the preset avatars: ${avatars.join(', ')}.`];
    }
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
      const invalid = check(body.name, body.avatar, true);
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
      };
      profiles = [...profiles, profile];
      return HttpResponse.json(profile, { status: 201 });
    }),
    http.patch('*/profiles/:profileId', async ({ params, request }) => {
      const existing = profiles.find((p) => p.id === params.profileId);
      if (existing === undefined) return notFound();
      const body = (await request.json()) as UpdateProfileRequest;
      const invalid = check(body.name, body.avatar, false, existing.id);
      if (invalid !== undefined) return invalid;
      const updated: Profile = { ...existing, name: body.name?.trim() ?? existing.name, avatar: body.avatar ?? existing.avatar };
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
