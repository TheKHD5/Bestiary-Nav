import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Bestiary Nav · Private Insights",
  description: "Private usage statistics for the Bestiary Nav maintainer.",
  robots: { index: false, follow: false },
  other: {
    "codex-preview": "development",
  },
  icons: {
    icon: "/favicon.svg",
    shortcut: "/favicon.svg",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body className="antialiased">{children}</body>
    </html>
  );
}
