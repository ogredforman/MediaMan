import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { TitleBarStrip } from './TitleBarStrip';

describe('TitleBarStrip', () => {
  it('is a drag strip hidden from assistive technology', () => {
    render(<TitleBarStrip />);
    // theme.css gives .title-bar-strip the drag region and the height of the OS window controls (32 px).
    const strip = screen.getByTestId('title-bar-strip');
    expect(strip).toHaveClass('title-bar-strip', 'fixed', 'top-0', 'bg-surface');
    expect(strip).toHaveAttribute('aria-hidden', 'true');
  });

  it('stays in the flow above the shell header when asked to', () => {
    render(<TitleBarStrip inFlow />);
    const strip = screen.getByTestId('title-bar-strip');
    expect(strip).toHaveClass('title-bar-strip', 'bg-surface');
    expect(strip).not.toHaveClass('fixed');
  });
});
