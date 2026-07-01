/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Aspire injects the BFF URL via services__webbff__https__0 (or http__0) when referenced.
const bffUrl =
  process.env["services__webbff__https__0"] ??
  process.env["services__webbff__http__0"] ??
  "http://localhost:5100";

export default defineConfig({
  plugins: [react()],
  server: {
    port: Number(process.env.PORT) || 5173,
    proxy: {
      // xfwd forwards X-Forwarded-Host/Proto so the BFF builds its OIDC
      // redirect_uri on this SPA origin and login returns here (not the BFF).
      // The OIDC callback paths must be proxied too, or Vite's SPA fallback
      // would swallow them.
      "/bff": { target: bffUrl, changeOrigin: true, secure: false, xfwd: true },
      "/api": { target: bffUrl, changeOrigin: true, secure: false, xfwd: true },
      "/signin-oidc": { target: bffUrl, changeOrigin: true, secure: false, xfwd: true },
      "/signout-callback-oidc": { target: bffUrl, changeOrigin: true, secure: false, xfwd: true },
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: [],
  },
});
