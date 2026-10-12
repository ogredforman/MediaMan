import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { server } from '../mocks/node';
import { mockAttention } from '../mocks/attention';
import { mockLibraries } from '../mocks/libraries';
import { mockProfiles } from '../mocks/profiles';
import { mockSettings } from '../mocks/settings';
import { mockVersions, mockWatched } from '../mocks/handlers';

// The whole-app renders are slow under a full parallel run; findBy* waits up to 5 s instead of 1 s.
configure({ asyncUtilTimeout: 5000 });

// jsdom doesn't implement scrolling; the router's scroll restoration calls it on every navigation.
window.scrollTo = () => undefined;

beforeAll(() => {
  server.listen({ onUnhandledRequest: 'error' });
});
afterEach(() => {
  cleanup();
  localStorage.clear();
  server.resetHandlers();
  mockProfiles.reset();
  mockSettings.reset();
  mockLibraries.reset();
  mockAttention.reset();
  mockVersions.reset();
  mockWatched.reset();
});
afterAll(() => {
  server.close();
});
