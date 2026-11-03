import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

// The build goes into the agent host, which serves it from the same origin as /auth and /agent (stage 1 plan,
// slice 4). In development, `npm run dev` proxies those paths to the agent host started by the AppHost or by
// `dotnet run` (port 5180, Properties/launchSettings.json).
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../src/Bank.Agent.Host/wwwroot",
    emptyOutDir: true,
  },
  server: {
    proxy: {
      "/auth": "http://localhost:5180",
      "/agent": "http://localhost:5180",
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test-setup.ts"],
  },
});
