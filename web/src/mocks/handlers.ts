import { getGetHealthMockHandler, getMediaManAPIMock } from '../api/mediaman.msw';

export const mockHealth = { status: 'healthy', version: '0.0.0-mock' } as const;

// Deterministic overrides first (MSW uses the first matching handler), then the generated defaults.
export const handlers = [getGetHealthMockHandler(mockHealth), ...getMediaManAPIMock()];
