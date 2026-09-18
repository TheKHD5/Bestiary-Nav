declare namespace Cloudflare {
  interface Env {
    REPORTING_READ_KEY?: string;
    DB?: D1Database;
    BUCKET?: R2Bucket;
  }
}
