import type { LanguageCode } from '../api/mediaman.schemas';
import { languageName, languagesByName, maximumLanguages } from '../lib/languages';
import { FieldError } from './FormField';
import { buttonClass, inputClass } from './formClasses';

/**
 * An ordered list of preferred languages (audio or subtitles), most preferred first: add from a list, move up, remove.
 * MPV plays the first one a file has.
 */
export function LanguagePreferences({
  kind,
  value,
  onChange,
  errors,
}: {
  kind: 'audio' | 'subtitle';
  value: LanguageCode[];
  onChange: (value: LanguageCode[]) => void;
  errors: string[] | undefined;
}) {
  const legend = kind === 'audio' ? 'Audio languages' : 'Subtitle languages';
  const unpicked = languagesByName.filter((code) => !value.includes(code));
  const moveUp = (index: number) => {
    onChange([...value.slice(0, index - 1), ...value.slice(index - 1, index + 1).reverse(), ...value.slice(index + 1)]);
  };

  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="mb-2 text-sm text-fg-muted">{legend}</legend>
      {value.length === 0 ? (
        <p className="text-sm text-fg-muted">
          {kind === 'audio' ? 'No preference: the file’s default audio plays.' : 'No preference: the file’s default subtitles show, if any.'}
        </p>
      ) : (
        <ol aria-label={legend} className="flex flex-col gap-1">
          {value.map((code, index) => (
            <li key={code} className="flex items-center justify-between gap-2 rounded-card bg-bg px-3 py-1 text-sm">
              <span>
                {index + 1}. {languageName(code)}
              </span>
              <span className="flex gap-1">
                {index > 0 && (
                  <button
                    type="button"
                    aria-label={`Move ${languageName(code)} up`}
                    onClick={() => { moveUp(index); }}
                    className={`${buttonClass} bg-surface text-fg`}
                  >
                    Up
                  </button>
                )}
                <button
                  type="button"
                  aria-label={`Remove ${languageName(code)}`}
                  onClick={() => { onChange(value.filter((c) => c !== code)); }}
                  className={`${buttonClass} bg-surface text-fg`}
                >
                  Remove
                </button>
              </span>
            </li>
          ))}
        </ol>
      )}
      {value.length < maximumLanguages ? (
        <label className="flex flex-col gap-1 text-sm text-fg-muted">
          {kind === 'audio' ? 'Add an audio language' : 'Add a subtitle language'}
          <select
            value=""
            onChange={(e) => {
              if (e.target.value !== '') onChange([...value, e.target.value as LanguageCode]);
            }}
            className={inputClass}
          >
            <option value="">Choose a language…</option>
            {unpicked.map((code) => (
              <option key={code} value={code}>
                {languageName(code)}
              </option>
            ))}
          </select>
        </label>
      ) : (
        <p className="text-xs text-fg-muted">
          That’s the most you can pick ({maximumLanguages}). Remove one to add another.
        </p>
      )}
      <FieldError messages={errors} />
    </fieldset>
  );
}
