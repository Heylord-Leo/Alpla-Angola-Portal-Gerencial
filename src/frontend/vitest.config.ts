import { defineConfig } from 'vitest/config';

// Minimal Vitest setup (Phase 2). Scope: pure, framework-free view logic only (buyerQueueView.ts).
// No jsdom / Testing Library — component-render tests are intentionally out of scope for this phase.
export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
    globals: false,
  },
});
