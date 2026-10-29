/**
 * The desktop window's title band (SPEC §11): a strip in the header's colour, as tall as the OS window controls it
 * holds, that drags the window. Fixed at the top on screens without the shell header (sign-in, setup, profiles); in the
 * flow above the header in the shell (`inFlow`). It has no height in a browser, where `env(titlebar-area-height)` is
 * unset.
 */
export function TitleBarStrip({ inFlow = false }: { inFlow?: boolean }) {
  const placement = inFlow ? '' : 'fixed inset-x-0 top-0 z-30 ';
  return <div aria-hidden="true" data-testid="title-bar-strip" className={`title-bar-strip ${placement}bg-surface`} />;
}
