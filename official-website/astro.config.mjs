// @ts-check
import { defineConfig } from 'astro/config';

// https://astro.build/config
export default defineConfig({
  site: 'https://markblocks.app',
  server: {
    host: true,
    port: 4321
  },
  devToolbar: {
    enabled: false
  }
});
