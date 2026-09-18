CREATE TABLE `activity` (
	`day` text NOT NULL,
	`installation` text NOT NULL,
	PRIMARY KEY(`day`, `installation`)
);
--> statement-breakpoint
CREATE INDEX `activity_installation` ON `activity` (`installation`);--> statement-breakpoint
CREATE TABLE `installations` (
	`id` text PRIMARY KEY NOT NULL,
	`first_seen` integer NOT NULL,
	`last_seen` integer NOT NULL,
	`version` text NOT NULL
);
--> statement-breakpoint
CREATE INDEX `installations_last_seen` ON `installations` (`last_seen`);--> statement-breakpoint
CREATE TABLE `request_limits` (
	`bucket` text PRIMARY KEY NOT NULL,
	`hits` integer NOT NULL,
	`expires` integer NOT NULL
);
--> statement-breakpoint
CREATE INDEX `limits_expires` ON `request_limits` (`expires`);--> statement-breakpoint
CREATE TABLE `service_cache` (
	`key` text PRIMARY KEY NOT NULL,
	`value` text NOT NULL,
	`updated` integer NOT NULL
);
