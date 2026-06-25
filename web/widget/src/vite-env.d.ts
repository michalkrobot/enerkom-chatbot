/// <reference types="vite/client" />

// PNG imported with ?inline resolves to a base64 data: URI string at build time.
declare module "*.png?inline" {
  const src: string;
  export default src;
}
