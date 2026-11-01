import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { Field } from './FormField';
import { PasswordInput } from './PasswordInput';

function Form({ onSubmit = () => undefined }: { onSubmit?: () => void }) {
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  return (
    <form onSubmit={(e) => { e.preventDefault(); onSubmit(); }}>
      <Field label="Password" field="password" errors={{}}>
        <PasswordInput name="password" value={password} onChange={(e) => { setPassword(e.target.value); }} />
      </Field>
      <Field label="Confirm password" field="confirm" errors={{}}>
        <PasswordInput name="confirm" value={confirm} onChange={(e) => { setConfirm(e.target.value); }} />
      </Field>
    </form>
  );
}

// Two password fields, so two toggles; the tuple satisfies noUncheckedIndexedAccess.
const toggles = () => screen.getAllByRole('button', { name: /password/ }) as [HTMLElement, HTMLElement];

describe('PasswordInput', () => {
  it('starts hidden and the eye button shows and hides what was typed', async () => {
    const user = userEvent.setup();
    render(<Form />);
    const field = screen.getByLabelText('Password');
    await user.type(field, 'secret');

    expect(field).toHaveAttribute('type', 'password');
    // Named by the label text alone, not the eye button inside the label too.
    expect(field).toHaveAccessibleName('Password');
    expect(field).toHaveAttribute('aria-labelledby');
    const [toggle] = toggles();
    expect(toggle).toHaveAccessibleName('Show password');
    expect(toggle).toHaveAttribute('aria-pressed', 'false');

    await user.click(toggle);
    expect(field).toHaveAttribute('type', 'text');
    expect(field).toHaveValue('secret');
    expect(toggle).toHaveAccessibleName('Hide password');
    expect(toggle).toHaveAttribute('aria-pressed', 'true');

    await user.click(toggle);
    expect(field).toHaveAttribute('type', 'password');
    expect(field).toHaveValue('secret');
  });

  it('is reached with Tab after its field and toggles with Enter and Space', async () => {
    const user = userEvent.setup();
    render(<Form />);
    await user.click(screen.getByLabelText('Password'));
    await user.tab();
    const [toggle] = toggles();
    expect(toggle).toHaveFocus();

    await user.keyboard('{Enter}');
    expect(screen.getByLabelText('Password')).toHaveAttribute('type', 'text');
    await user.keyboard(' ');
    expect(screen.getByLabelText('Password')).toHaveAttribute('type', 'password');

    await user.tab();
    expect(screen.getByLabelText('Confirm password')).toHaveFocus();
  });

  it("doesn't submit the form", async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    render(<Form onSubmit={onSubmit} />);
    await user.click(toggles()[0]);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('shows each field on its own and keeps the caret where it was', async () => {
    const user = userEvent.setup();
    render(<Form />);
    const field = screen.getByLabelText<HTMLInputElement>('Password');
    await user.type(field, 'secret');
    field.setSelectionRange(2, 4);

    await user.click(toggles()[0]);
    expect(field.selectionStart).toBe(2);
    expect(field.selectionEnd).toBe(4);
    expect(screen.getByLabelText('Confirm password')).toHaveAttribute('type', 'password');
  });

  it("doesn't block paste, copy or cut, shown or hidden", async () => {
    const user = userEvent.setup();
    render(<Form />);
    const password = screen.getByLabelText('Password');
    const confirm = screen.getByLabelText('Confirm password');

    await user.click(password);
    await user.paste('pasted hidden');
    expect(password).toHaveValue('pasted hidden');

    await user.click(toggles()[1]);
    await user.click(confirm);
    await user.paste('pasted shown');
    expect(confirm).toHaveValue('pasted shown');

    for (const field of [password, confirm]) {
      for (const event of [fireEvent.paste, fireEvent.copy, fireEvent.cut]) {
        // fireEvent returns false when a handler called preventDefault.
        expect(event(field)).toBe(true);
      }
    }
  });
});
