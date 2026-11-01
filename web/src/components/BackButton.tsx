import { useCanGoBack, useNavigate, useRouter } from '@tanstack/react-router';

/**
 * Back to the previous page in the app's history (Home, a genre grid, search with its query, the inbox), which the
 * router restores with its scroll position; opened directly, with no earlier in-app page, it goes to Home (SPEC §11).
 * `className` places it.
 */
export function BackButton({ className }: { className: string }) {
  const router = useRouter();
  const canGoBack = useCanGoBack();
  const navigate = useNavigate();
  return (
    <button
      type="button"
      aria-label="Back"
      onClick={() => {
        if (canGoBack) router.history.back();
        else void navigate({ to: '/' });
      }}
      className={`${className} flex h-10 w-10 items-center justify-center rounded-full bg-bg/60 text-fg transition-colors duration-(--motion-fast) outline-none hover:bg-surface hover:text-highlight focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-highlight`}
    >
      <svg aria-hidden="true" viewBox="0 0 24 24" className="h-6 w-6" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M19 12H5M12 19l-7-7 7-7" />
      </svg>
    </button>
  );
}
