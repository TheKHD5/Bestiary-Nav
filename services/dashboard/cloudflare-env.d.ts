declare namespace Cloudflare {
  interface Env {
    COLLECTOR_URL?: string;
    REPORTING_READ_KEY?: string;
    DB?: D1Database;
    BUCKET?: R2Bucket;
  }
}
