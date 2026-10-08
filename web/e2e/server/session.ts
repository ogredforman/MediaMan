import path from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * The browser state setup.spec.ts saves after signing in through the wizard; the other specs start from it. They
 * share one refresh token, which works once, so a run must stay within the 15-minute access token (it does).
 */
export const sessionFile = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '.e2e-server', 'session.json');
