/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// Under Aspire, AddViteApp sets PORT and WithReference(api) passes the API's address.
const apiUrl = process.env.API_HTTP ?? process.env.services__api__http__0 ?? 'http://localhost:5080';

export default defineConfig({
  plugins: [react()],
  server: {
    host: 'localhost',
    port: Number(process.env.PORT ?? 5173),
    strictPort: true,
    allowedHosts: [], // localhost and IP addresses only (Vite's default, pinned)
    cors: false, // the dashboard calls /api on its own origin; no other website needs to read this server
    proxy: { '/api': { target: apiUrl, changeOrigin: true } },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
  },
});
