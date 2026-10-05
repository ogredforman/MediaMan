import { createFileRoute } from '@tanstack/react-router';

export const Route = createFileRoute('/')({
  component: Home,
});

function Home() {
  return (
    <section aria-labelledby="home-heading" className="p-8">
      <h1 id="home-heading" className="text-2xl font-semibold">
        Home
      </h1>
      <p className="mt-2 text-fg-muted">Your library will show up here.</p>
    </section>
  );
}
