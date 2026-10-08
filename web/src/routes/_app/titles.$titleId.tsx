import { createFileRoute } from '@tanstack/react-router';
import { TitleDetail } from '../../components/TitleDetail';

export const Route = createFileRoute('/_app/titles/$titleId')({
  component: TitleRoute,
});

function TitleRoute() {
  const { titleId } = Route.useParams();
  // Keyed so moving between titles resets the selected season.
  return <TitleDetail key={titleId} titleId={titleId} />;
}
