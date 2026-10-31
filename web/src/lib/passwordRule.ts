import type { PasswordPolicy } from '../api/mediaman.schemas';

// The server's rule (`MediaMan.Auth/PasswordPolicy.cs`, SPEC §7.11); it checks every password again.
const minimumLength = 8;
const strongMinimumLength = 12;
const strongKindsNeeded = 3;
const maximumLength = 1024;

const kindsNeeded = `at least ${String(strongKindsNeeded)} of: uppercase letters, lowercase letters, digits and symbols`;

/** Counts characters as people see them, so an emoji or an accented letter counts once, like the server. */
function visibleLength(password: string): number {
  return [...new Intl.Segmenter().segment(password)].length;
}

/** How many of uppercase, lowercase, digit and symbol (anything that isn't a letter or a digit) `password` mixes. */
function kindsIn(password: string): number {
  return [/\p{Lu}/u, /\p{Ll}/u, /\p{Nd}/u, /[^\p{L}\p{Nd}]/u].filter((kind) => kind.test(password)).length;
}

/** What a password must be under `policy`, for a field's hint. */
export function passwordHint(policy: PasswordPolicy): string {
  const rule = policy.strongPasswords
    ? `At least ${String(strongMinimumLength)} characters, mixing ${kindsNeeded}.`
    : `At least ${String(minimumLength)} characters.`;
  return policy.requirePasswords ? rule : `Optional. If you set one: ${rule.charAt(0).toLowerCase()}${rule.slice(1)}`;
}

/** Why `password` can't be set under `policy`, as the server words it; undefined when it can. */
export function passwordProblem(policy: PasswordPolicy, password: string): string | undefined {
  if (password === '') return policy.requirePasswords ? 'Enter a password.' : undefined;
  const minimum = policy.strongPasswords ? strongMinimumLength : minimumLength;
  if (visibleLength(password) < minimum) {
    return policy.strongPasswords
      ? `Use a password of at least ${String(strongMinimumLength)} characters, mixing ${kindsNeeded}.`
      : `Use a password of at least ${String(minimumLength)} characters.`;
  }
  if (password.length > maximumLength) return `Use a password of at most ${String(maximumLength)} characters.`;
  if (policy.strongPasswords && kindsIn(password) < strongKindsNeeded) return `Mix ${kindsNeeded}.`;
  return undefined;
}
