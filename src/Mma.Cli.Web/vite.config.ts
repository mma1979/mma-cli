import path from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

const rootDir = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(rootDir, "src")
    }
  },
  server: {
    host: "127.0.0.1",
    port: 5173,
    strictPort: true,
    proxy: {
      "/api": {
        target: "http://127.0.0.1:5180",
        changeOrigin: false
      },
      "/openapi": {
        target: "http://127.0.0.1:5180",
        changeOrigin: false
      }
    }
  },
  build: {
    outDir: "dist",
    sourcemap: false,
    rollupOptions: {
      output: {
        manualChunks: {
          flow: ["@xyflow/react", "@dagrejs/dagre"],
          diff: ["diff"],
          shiki: ["shiki"]
        }
      }
    }
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    css: false,
    environmentOptions: {
      jsdom: {
        url: "http://127.0.0.1:5173/"
      }
    },
    exclude: ["e2e/**", "node_modules/**", "dist/**"]
  }
});
