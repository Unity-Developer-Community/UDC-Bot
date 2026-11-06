import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { defineConfig } from 'vitest/config';

const brand = JSON.parse(readFileSync(resolve(import.meta.dirname, 'brand.json'), 'utf8')) as { name: string };

const kestrel = { target: 'http://localhost:5081' };

export default defineConfig({
  plugins: [
    {
      name: 'inject-brand',
      transformIndexHtml: (html: string) => html.replaceAll('%APP_NAME%', () => brand.name),
    },
  ],
  resolve: {
    // Resolves the `@/*` alias from tsconfig.json `paths`, the single source of truth.
    tsconfigPaths: true,
  },
  build: {
    // Kestrel serves the built files from wwwroot in production.
    emptyOutDir: true,
    // Phaser alone is about 1.4 MB; it is a separate chunk that loads only when a game opens.
    chunkSizeWarningLimit: 1500,
    outDir: '../wwwroot',
  },
  server: {
    // Browsers open this address, which is also the casino's public origin and the Discord redirect URL, so the
    // OAuth callback, the cookie and the Origin check all see one site. Kestrel listens one port above.
    port: 5080,
    strictPort: true,
    // The slot pictures stay in the bot's asset folder, the single copy, which is outside this project.
    fs: { allow: ['.', '../../DiscordBot/Assets/slots'] },
    proxy: {
      '/api': kestrel,
      '/hub': { ...kestrel, ws: true },
      // Not a bare '/login' prefix: it would also catch the client's own /login-failed page.
      '^/login(\\?.*)?$': kestrel,
      '/signin-discord': kestrel,
    },
  },
  test: {
    projects: [
      {
        extends: true,
        test: {
          name: 'unit',
          environment: 'jsdom',
          include: ['src/**/*.test.ts'],
          exclude: ['src/**/*.browser.test.ts'],
        },
      },
    ],
    coverage: {
      exclude: ['src/**/*.test.ts', 'src/main.ts'],
      include: ['src/**/*.ts'],
      provider: 'v8',
    },
  },
});
