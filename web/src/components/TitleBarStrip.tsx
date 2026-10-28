/**
 * The window's title bar on screens without the shell header (sign-in, setup, profiles): a strip in the header's
 * colour that drags the desktop app's window, under the OS window controls. It has no height in a browser, where
 * `env(titlebar-area-height)` is unset (SPEC §11).
 */
export function TitleBarStrip() {
  return <div aria-hidden="true" data-testid="title-bar-strip" className="title-bar-strip fixed inset-x-0 top-0 z-30 bg-surface" />;
}
