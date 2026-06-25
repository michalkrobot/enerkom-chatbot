/// <reference types="vite/client" />

// Assets imported with ?inline resolve to a base64 data: URI string at build time.
declare module "*.png?inline" {
  const src: string;
  export default src;
}
declare module "*.woff2?inline" {
  const src: string;
  export default src;
}
