import type { ReactNode } from 'react';

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
  return (
    <div className="flex flex-col gap-1">
      {/* The label wraps the control, so the control is named by it; an error is announced when it appears. */}
      <label className="flex flex-col gap-1 text-sm text-fg-muted">
        {label}
        {children}
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
