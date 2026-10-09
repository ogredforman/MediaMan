import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState, type ReactNode, type SubmitEvent } from 'react';
import {
  getListProfilesQueryKey,
  useCreateProfile,
  useDeleteProfile,
  useListProfiles,
  useSelectSessionProfile,
  useUpdateProfile,
} from '../api/mediaman';
import { ProfileAvatar as Avatars, type Profile, type ProfileAvatar, type ProfileList } from '../api/mediaman.schemas';
import type { ApiError } from '../lib/http';

/** Matches the server's rules (SPEC §7.11); the server checks them again. */
const maximumProfiles = 10;
const maximumNameLength = 32;

const buttonClass =
  'rounded-card px-4 py-2 font-medium transition-opacity duration-(--motion-fast) hover:opacity-90 disabled:opacity-50';

// Whole class names, so Tailwind finds them.
const avatarColours: Record<ProfileAvatar, string> = {
  violet: 'bg-avatar-violet',
  blue: 'bg-avatar-blue',
  teal: 'bg-avatar-teal',
  green: 'bg-avatar-green',
  amber: 'bg-avatar-amber',
  orange: 'bg-avatar-orange',
  rose: 'bg-avatar-rose',
  slate: 'bg-avatar-slate',
};

const avatarSizes = { small: 'size-8 text-sm', large: 'size-28 text-5xl' } as const;

/** A preset avatar: its colour with the profile's initial. Decorative; the name next to it says who it is. */
export function AvatarTile({
  name,
  avatar,
  size = 'large',
}: {
  name: string;
  avatar: ProfileAvatar;
  size?: keyof typeof avatarSizes;
}) {
  return (
    <span
      aria-hidden="true"
      className={`flex items-center justify-center rounded-card font-bold text-bg ${avatarColours[avatar]} ${avatarSizes[size]}`}
    >
      {name.trim().charAt(0).toUpperCase()}
    </span>
  );
}

/**
 * "Who's watching?" (SPEC §7.11 step 3, §11): pick a profile for this session, or manage the account's profiles
 * (add, rename, change avatar, delete). `onPicked` runs once the server has recorded the pick.
 */
export function ProfilePicker({ onPicked }: { onPicked: () => void }) {
  const queryClient = useQueryClient();
  const profiles = useListProfiles();
  const [managing, setManaging] = useState(false);
  const [editing, setEditing] = useState<Profile | 'new'>();

  const pick = useSelectSessionProfile({
    mutation: {
      onSuccess: (profile) => {
        queryClient.setQueryData<ProfileList>(getListProfilesQueryKey(), (list) =>
          list === undefined ? list : { ...list, activeProfileId: profile.id },
        );
        // Profile-scoped data (watch state, Home rows) follows the pick.
        void queryClient.invalidateQueries({ predicate: (q) => q.queryKey[0] !== getListProfilesQueryKey()[0] });
        onPicked();
      },
    },
  });

  if (profiles.isPending) {
    return (
      <Screen heading="Who's watching?">
        <p role="status" className="text-fg-muted">
          Loading profiles…
        </p>
      </Screen>
    );
  }
  if (profiles.isError) {
    return (
      <Screen heading="Who's watching?">
        <div role="alert" className="flex flex-col items-center gap-3">
          <p>
            {profiles.error.problem.title} {profiles.error.problem.action}
          </p>
          <button type="button" onClick={() => void profiles.refetch()} className={`${buttonClass} bg-highlight text-bg`}>
            Try again
          </button>
        </div>
      </Screen>
    );
  }

  const items = profiles.data.items;
  if (editing !== undefined) {
    return (
      <ProfileForm
        profile={editing === 'new' ? undefined : editing}
        canDelete={items.length > 1}
        defaultAvatar={Object.values(Avatars)[items.length % Object.values(Avatars).length] ?? 'violet'}
        onDone={() => { setEditing(undefined); }}
      />
    );
  }

  return (
    <Screen heading={managing ? 'Manage profiles' : "Who's watching?"}>
      <ul aria-label="Profiles" className="flex flex-wrap justify-center gap-6">
        {items.map((profile) => (
          <li key={profile.id}>
            <button
              type="button"
              aria-label={managing ? `Edit ${profile.name}` : undefined}
              disabled={pick.isPending}
              onClick={() => {
                if (managing) setEditing(profile);
                else pick.mutate({ data: { profileId: profile.id } });
              }}
              className="flex w-32 flex-col items-center gap-2 rounded-card p-2 text-fg-muted transition-colors duration-(--motion-fast) hover:text-fg disabled:opacity-50"
            >
              <AvatarTile name={profile.name} avatar={profile.avatar} />
              <span className="max-w-full truncate">{profile.name}</span>
              {managing && <span className="text-xs text-highlight">Edit</span>}
            </button>
          </li>
        ))}
        {managing && items.length < maximumProfiles && (
          <li>
            <button
              type="button"
              onClick={() => { setEditing('new'); }}
              className="flex w-32 flex-col items-center gap-2 rounded-card p-2 text-fg-muted transition-colors duration-(--motion-fast) hover:text-fg"
            >
              <span aria-hidden="true" className="flex size-28 items-center justify-center rounded-card bg-surface text-5xl">
                +
              </span>
              <span>Add profile</span>
            </button>
          </li>
        )}
      </ul>

      {pick.isError && (
        <p role="alert" className="text-sm">
          {pick.error.problem.title} {pick.error.problem.action}
        </p>
      )}

      <button
        type="button"
        onClick={() => { setManaging(!managing); }}
        className={`${buttonClass} ${managing ? 'bg-highlight text-bg' : 'bg-surface text-fg'}`}
      >
        {managing ? 'Done' : 'Manage profiles'}
      </button>
    </Screen>
  );
}

function Screen({ heading, children }: { heading: string; children: ReactNode }) {
  // The heading changes with the mode, so focus follows it for keyboard and screen-reader users.
  const ref = useRef<HTMLHeadingElement>(null);
  const firstRender = useRef(true);
  useEffect(() => {
    if (firstRender.current) {
      firstRender.current = false;
      return;
    }
    ref.current?.focus();
  }, [heading]);
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-8 p-8 text-center">
      <h1 ref={ref} tabIndex={-1} className="text-3xl font-semibold focus:outline-none">
        {heading}
      </h1>
      {children}
    </main>
  );
}

type FieldErrors = Record<string, string[]>;

/** Adds a profile (`profile` undefined) or edits one: name, preset avatar, and delete with a confirmation. */
function ProfileForm({
  profile,
  canDelete,
  defaultAvatar,
  onDone,
}: {
  profile: Profile | undefined;
  canDelete: boolean;
  defaultAvatar: ProfileAvatar;
  onDone: () => void;
}) {
  const queryClient = useQueryClient();
  const [name, setName] = useState(profile?.name ?? '');
  const [avatar, setAvatar] = useState<ProfileAvatar>(profile?.avatar ?? defaultAvatar);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  // The form replaces the screen, so focus starts where to type.
  const nameInput = useRef<HTMLInputElement>(null);
  useEffect(() => {
    nameInput.current?.focus();
  }, []);

  const done = () => {
    void queryClient.invalidateQueries({ queryKey: getListProfilesQueryKey() });
    onDone();
  };
  // Field problems show on their fields; the rest show below the form with their next action.
  const onError = (error: ApiError) => {
    if (error.problem.code === 'profile.invalid' && error.problem.errors !== undefined) setErrors(error.problem.errors);
    if (error.problem.code === 'profile.name_taken') {
      setErrors({ name: [[error.problem.title, error.problem.action].filter(Boolean).join(' ')] });
    }
  };
  const create = useCreateProfile({ mutation: { onSuccess: done, onError } });
  const update = useUpdateProfile({ mutation: { onSuccess: done, onError } });
  const remove = useDeleteProfile({
    mutation: {
      onSuccess: (_, { profileId }) => {
        // The server clears a deleted profile's pick; drop it here too, so the shell's guard asks again at once.
        queryClient.setQueryData<ProfileList>(getListProfilesQueryKey(), (list) =>
          list === undefined
            ? list
            : {
                items: list.items.filter((p) => p.id !== profileId),
                ...(list.activeProfileId === undefined || list.activeProfileId === profileId
                  ? {}
                  : { activeProfileId: list.activeProfileId }),
              },
        );
        done();
      },
    },
  });

  const save = profile === undefined ? create : update;
  const busy = create.isPending || update.isPending || remove.isPending;
  const failed = [save.error, remove.error].find((e) => e != null);
  const problem =
    failed != null && !(failed.problem.code === 'profile.invalid' && failed.problem.errors !== undefined) &&
    failed.problem.code !== 'profile.name_taken'
      ? failed.problem
      : undefined;

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmed = name.trim();
    if (trimmed.length === 0 || trimmed.length > maximumNameLength) {
      setErrors({ name: [`Enter a name of 1 to ${String(maximumNameLength)} characters.`] });
      return;
    }
    if (profile === undefined) create.mutate({ data: { name: trimmed, avatar } });
    else update.mutate({ profileId: profile.id, data: { name: trimmed, avatar } });
  };

  const heading = profile === undefined ? 'Add profile' : 'Edit profile';
  return (
    <main className="flex min-h-screen items-center justify-center p-8">
      <form
        aria-labelledby="profile-form-heading"
        noValidate
        onSubmit={submit}
        className="flex w-full max-w-md flex-col gap-4 rounded-card bg-surface p-8 shadow-soft"
      >
        <div className="flex items-center gap-4">
          <AvatarTile name={name === '' ? '?' : name} avatar={avatar} size="small" />
          <h1 id="profile-form-heading" className="text-2xl font-semibold">
            {heading}
          </h1>
        </div>

        <div className="flex flex-col gap-1">
          <label className="flex flex-col gap-1 text-sm text-fg-muted">
            Name
            <input
              name="name"
              ref={nameInput}
              maxLength={maximumNameLength}
              value={name}
              onChange={(e) => {
                setName(e.target.value);
                setErrors(({ name: _, ...rest }) => rest);
              }}
              className="rounded-card bg-bg px-3 py-2 text-fg"
            />
          </label>
          <FieldError messages={errors.name} />
        </div>

        <fieldset className="flex flex-col gap-2">
          <legend className="mb-2 text-sm text-fg-muted">Avatar</legend>
          <div className="flex flex-wrap gap-2">
            {Object.values(Avatars).map((option) => (
              <label key={option} className="cursor-pointer">
                <input
                  type="radio"
                  name="avatar"
                  value={option}
                  checked={avatar === option}
                  onChange={() => {
                    setAvatar(option);
                    setErrors(({ avatar: _, ...rest }) => rest);
                  }}
                  className="peer sr-only"
                />
                <span className="sr-only">{option.charAt(0).toUpperCase() + option.slice(1)}</span>
                <span
                  aria-hidden="true"
                  className={`block size-10 rounded-card ${avatarColours[option]} outline-offset-2 peer-checked:outline-2 peer-checked:outline-fg peer-focus-visible:outline-2 peer-focus-visible:outline-highlight`}
                />
              </label>
            ))}
          </div>
          <FieldError messages={errors.avatar} />
        </fieldset>

        {problem !== undefined && (
          <p role="alert" className="text-sm">
            {problem.title} {problem.action}
          </p>
        )}

        {confirmingDelete && profile !== undefined ? (
          <div className="flex flex-col gap-3 rounded-card bg-bg p-4">
            <p className="text-sm">Delete {profile.name}? Its watch history goes too, and this can't be undone.</p>
            <div className="flex justify-end gap-2">
              <button type="button" onClick={() => { setConfirmingDelete(false); }} className={`${buttonClass} bg-surface text-fg`}>
                Keep profile
              </button>
              <button
                type="button"
                disabled={busy}
                onClick={() => { remove.mutate({ profileId: profile.id }); }}
                className={`${buttonClass} bg-highlight text-bg`}
              >
                Delete profile
              </button>
            </div>
          </div>
        ) : (
          <div className="flex justify-between gap-2">
            {profile !== undefined && canDelete ? (
              <button type="button" onClick={() => { setConfirmingDelete(true); }} className={`${buttonClass} bg-bg text-fg`}>
                Delete
              </button>
            ) : (
              <span />
            )}
            <div className="flex gap-2">
              <button type="button" onClick={onDone} className={`${buttonClass} bg-bg text-fg`}>
                Cancel
              </button>
              <button type="submit" disabled={busy} className={`${buttonClass} bg-highlight text-bg`}>
                {busy ? 'Saving…' : 'Save'}
              </button>
            </div>
          </div>
        )}
      </form>
    </main>
  );
}

function FieldError({ messages }: { messages: string[] | undefined }) {
  if (messages === undefined || messages.length === 0) return null;
  return (
    <p role="alert" className="text-sm text-highlight">
      {messages.join(' ')}
    </p>
  );
}
