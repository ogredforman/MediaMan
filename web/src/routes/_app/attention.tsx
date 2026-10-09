import { createFileRoute } from '@tanstack/react-router';
import { AttentionInbox } from '../../components/AttentionInbox';

export const Route = createFileRoute('/_app/attention')({
  component: AttentionInbox,
});
