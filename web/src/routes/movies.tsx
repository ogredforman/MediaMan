import { createFileRoute } from '@tanstack/react-router';
import { PosterGrid } from '../components/PosterGrid';

export const Route = createFileRoute('/movies')({
  component: () => <PosterGrid kind="movie" />,
});
