import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

// Minimal Vitest config for the Web frontend.
// The normalized-config logic under test is pure (no DOM), so we run in the
// Node environment — no jsdom/happy-dom dependency required.
//
// The app uses the `@` path alias (→ src/); mirror it here so the permanent
// test can import the real `normalizeLegacyConfigKeys` straight from the page
// module without replicating its logic.
export default defineConfig({
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
});
