import { sqliteTable, text, integer, primaryKey, index } from 'drizzle-orm/sqlite-core';
export const installations = sqliteTable('installations', {
  id: text('id').primaryKey(), firstSeen: integer('first_seen').notNull(), lastSeen: integer('last_seen').notNull(), version: text('version').notNull(),
}, t => [index('installations_last_seen').on(t.lastSeen)]);
export const activity = sqliteTable('activity', { day: text('day').notNull(), installation: text('installation').notNull() },
  t => [primaryKey({ columns: [t.day, t.installation] }), index('activity_installation').on(t.installation)]);
export const limits = sqliteTable('request_limits', { bucket: text('bucket').primaryKey(), hits: integer('hits').notNull(), expires: integer('expires').notNull() }, t => [index('limits_expires').on(t.expires)]);
export const serviceCache = sqliteTable('service_cache', { key: text('key').primaryKey(), value: text('value').notNull(), updated: integer('updated').notNull() });
