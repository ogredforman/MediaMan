import { LanguageCode as LanguageCodes, type LanguageCode } from '../api/mediaman.schemas';

/** The contract's `LanguagePreferences` `maxItems` (SPEC §8 playback); the server checks it again. */
export const maximumLanguages = 5;

const names = new Intl.DisplayNames(['en'], { type: 'language' });

/** The language's English name, e.g. `Japanese` for `ja`. */
export function languageName(code: LanguageCode): string {
  return names.of(code) ?? code;
}

/** Every language a profile can prefer, sorted by name. */
export const languagesByName: LanguageCode[] = Object.values(LanguageCodes).sort((a, b) =>
  languageName(a).localeCompare(languageName(b)),
);
