import { defineConfig } from "vite";

// Library build → a single, self-contained IIFE bundle at dist/widget.js.
// Everything (lottie-web, elektron.json, the launcher PNG, all CSS) is inlined
// so the host page only needs one <script> tag with no external runtime deps.
export default defineConfig({
  build: {
    outDir: "dist",
    emptyOutDir: true,
    // No CSS code-splitting — any imported CSS is inlined into the JS.
    cssCodeSplit: false,
    // Inline assets up to ~1 MB as data URIs (elektron-head.png is ~256 KB).
    assetsInlineLimit: 1024 * 1024,
    lib: {
      entry: "src/widget.ts",
      name: "EnerkomChatbotWidget",
      formats: ["iife"],
      // Stable URL for <script src> — no content hash in the filename.
      fileName: () => "widget.js",
    },
    rollupOptions: {
      output: {
        // Keep a single, predictable output file; no external dependencies.
        inlineDynamicImports: true,
        entryFileNames: "widget.js",
        assetFileNames: "widget.[ext]",
      },
    },
  },
});
