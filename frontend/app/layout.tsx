import type { Metadata } from "next";
import Providers from "@/lib/providers";
import "./globals.css";

export const metadata: Metadata = {
  title: "AltusIQ",
  description: "Live aviation analytics",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body className="bg-gray-950 text-white">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
