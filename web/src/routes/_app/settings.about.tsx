import { createFileRoute } from '@tanstack/react-router';
import { AboutSettings } from '../../components/AboutSettings';

export const Route = createFileRoute('/_app/settings/about')({
  component: AboutSettings,
});
