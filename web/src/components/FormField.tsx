import { useId, type ChangeEvent, type ReactNode } from 'react';
import { FieldLabelId } from './fieldLabelId';

/** Server validation errors (`*.invalid` problems), keyed by field. */
export type FieldErrors = Record<string, string[]>;

/** A labelled control with an optional hint and the errors for `field`. */
export function Field({
  label,
  field,
  errors,
  hint,
  children,
}: {
  label: string;
  field: string;
  errors: FieldErrors;
  hint?: string;
  children: ReactNode;
}) {
  const labelId = useId();
  return (
    <div className="flex flex-col gap-1">
      {/* The label wraps the control, so the control is named by it; an error is announced when it appears. */}
      <label className="flex flex-col gap-1 text-sm text-fg-muted">
        <span id={labelId}>{label}</span>
        <FieldLabelId value={labelId}>{children}</FieldLabelId>
      </label>
      {hint !== undefined && <p className="text-xs text-fg-muted">{hint}</p>}
      <FieldError messages={errors[field]} />
    </div>
  );
}

export function FieldError({ messages }: { messages: string[] | undefined }) {
  if (messages === undefined || messages.length === 0) return null;
  return (
    <p role="alert" className="text-sm text-highlight">
      {messages.join(' ')}
    </p>
  );
}

/** A checkbox with its label and a line on what it does, as a card (the password policy in setup and Settings). */
export function CheckboxOption({
  name,
  checked,
  onChange,
  label,
  description,
}: {
  name: string;
  checked: boolean;
  onChange: (e: ChangeEvent<HTMLInputElement>) => void;
  label: string;
  description: string;
}) {
  return (
    <label className="grid cursor-pointer grid-cols-[auto_1fr] items-start gap-x-3 rounded-card bg-bg p-3">
      <input type="checkbox" name={name} checked={checked} onChange={onChange} className="row-span-2 mt-1" />
      <span className="font-medium">{label}</span>
      <span className="text-sm text-fg-muted">{description}</span>
    </label>
  );
}
