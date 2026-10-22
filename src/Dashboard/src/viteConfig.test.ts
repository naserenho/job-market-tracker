// @vitest-environment node
import { describe, expect, it } from 'vitest';
import config from '../vite.config';

// Only this PC's browser, on the dashboard's own address, may talk to the dev server: no other host name (DNS
// rebinding) and no other website (CORS). Pinned here so a Vite upgrade can't change it silently.
describe('dev server', () => {
  it('pins host, allowed hosts and CORS', () => {
    expect(config.server).toMatchObject({ host: 'localhost', allowedHosts: [], cors: false, strictPort: true });
  });
});
