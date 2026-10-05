import { QueryClient } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { createAppRouter } from './router';
import './theme/theme.css';

async function enableMocking(): Promise<void> {
  if (import.meta.env.VITE_API_MOCK !== 'true') return;
  const { worker } = await import('./mocks/browser');
  await worker.start({ onUnhandledRequest: 'bypass' });
}

const queryClient = new QueryClient();
const router = createAppRouter(queryClient);

void enableMocking().then(() => {
  const root = document.getElementById('root');
  if (!root) throw new Error('index.html is missing the #root element.');
  createRoot(root).render(
    <StrictMode>
      <App queryClient={queryClient} router={router} />
    </StrictMode>,
  );
});
