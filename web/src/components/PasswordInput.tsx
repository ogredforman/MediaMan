import { useContext, useLayoutEffect, useRef, useState, type InputHTMLAttributes } from 'react';
import { FieldLabelId } from './fieldLabelId';

/**
 * A password input with an eye button that shows or hides what was typed (SPEC §11). Nothing blocks paste,
 * copy or cut; a shown field is a text field, so copying out of it works too. Inside a `Field` the input is
 * named by the label text alone, not the eye button's label too.
 */
export function PasswordInput({ className = '', ...props }: Omit<InputHTMLAttributes<HTMLInputElement>, 'type'>) {
  const labelId = useContext(FieldLabelId);
  const [shown, setShown] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  // Changing the type can drop the caret; put the selection back where it was.
  const selection = useRef<[number, number] | null>(null);

  useLayoutEffect(() => {
    const range = selection.current;
    selection.current = null;
    if (range !== null) input.current?.setSelectionRange(range[0], range[1]);
  }, [shown]);

  const toggle = () => {
    const field = input.current;
    if (field?.selectionStart != null && field.selectionEnd !== null) {
      selection.current = [field.selectionStart, field.selectionEnd];
    }
    setShown((value) => !value);
  };

  return (
    <span className="relative flex">
      <input aria-labelledby={labelId} {...props} ref={input} type={shown ? 'text' : 'password'} className={`${className} w-full pr-10`} />
      <button
        type="button"
        aria-label={shown ? 'Hide password' : 'Show password'}
        aria-pressed={shown}
        onClick={toggle}
        className="absolute inset-y-0 right-0 flex w-10 items-center justify-center rounded-card text-fg-muted transition-colors duration-(--motion-fast) hover:text-fg focus-visible:outline-2 focus-visible:outline-highlight"
      >
        {shown ? <EyeOffIcon /> : <EyeIcon />}
      </button>
    </span>
  );
}

function EyeIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z" />
      <circle cx="12" cy="12" r="3" />
    </svg>
  );
}

function EyeOffIcon() {
  return (
    <svg aria-hidden="true" viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M10.6 5.1A10.8 10.8 0 0 1 12 5c6.5 0 10 7 10 7a17.6 17.6 0 0 1-3.2 4.2M6.6 6.6C3.7 8.4 2 12 2 12s3.5 7 10 7a10.4 10.4 0 0 0 5.4-1.6" />
      <path d="M9.9 9.9a3 3 0 0 0 4.2 4.2" />
      <path d="M2 2l20 20" />
    </svg>
  );
}
