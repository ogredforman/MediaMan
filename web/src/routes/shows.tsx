import { createFileRoute } from '@tanstack/react-router';
import { PosterGrid } from '../components/PosterGrid';

export const Route = createFileRoute('/shows')({
  component: () => <PosterGrid kind="show" />,
});
