import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { TitleBarStrip } from './TitleBarStrip';

describe('TitleBarStrip', () => {
  it('is a drag strip hidden from assistive technology', () => {
    render(<TitleBarStrip />);
    // theme.css gives .title-bar-strip the drag region and the height of the OS window controls.
    const strip = screen.getByTestId('title-bar-strip');
    expect(strip).toHaveClass('title-bar-strip', 'fixed', 'top-0', 'bg-surface');
    expect(strip).toHaveAttribute('aria-hidden', 'true');
  });
});
