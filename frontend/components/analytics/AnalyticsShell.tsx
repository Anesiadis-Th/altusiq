"use client";

import { X } from "lucide-react";
import { IconButton } from "@/components/ui/Button";
import { MicroLabel } from "@/components/ui/Label";

// Keep this eager. It is the frame and close button that have to be on screen
// while the lazy content chunk inside it is still loading.
export default function AnalyticsShell({
  onClose,
  children,
}: {
  onClose: () => void;
  children: React.ReactNode;
}) {
  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Analytics"
      className="absolute inset-0 z-40 flex flex-col bg-app/95 backdrop-blur-xl"
    >
      <header className="flex h-14 shrink-0 items-center gap-2 px-4 sm:px-6">
        <MicroLabel className="flex-1">Analytics</MicroLabel>
        <IconButton onClick={onClose} aria-label="Close analytics">
          <X size={16} />
        </IconButton>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto px-4 pb-10 sm:px-6">
        <div className="mx-auto max-w-6xl">{children}</div>
      </div>
    </div>
  );
}
