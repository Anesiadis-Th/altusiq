import { X } from "lucide-react";
import { IconButton } from "./Button";

// Docked flush to the left edge on sm+, an anchored overlay card on mobile.
export const RAIL_WIDTH_CLASS = "sm:w-80";

export function Rail({
  children,
  label,
}: {
  children: React.ReactNode;
  label: string;
}) {
  return (
    <aside
      aria-label={label}
      className={`absolute left-4 right-4 top-16 z-10 flex max-h-[calc(100dvh-6rem)] flex-col overflow-hidden rounded-panel border border-line bg-surface sm:bottom-0 sm:left-0 sm:right-auto sm:top-0 sm:max-h-none sm:rounded-none sm:border-0 sm:border-r sm:border-line ${RAIL_WIDTH_CLASS}`}
    >
      {children}
    </aside>
  );
}

export function RailHeader({
  children,
  onClose,
  closeLabel,
}: {
  children: React.ReactNode;
  onClose: () => void;
  closeLabel: string;
}) {
  return (
    <div className="flex h-11 shrink-0 items-center gap-2 border-b border-line px-2 pl-3">
      {children}
      <IconButton onClick={onClose} aria-label={closeLabel}>
        <X size={16} />
      </IconButton>
    </div>
  );
}

export function RailBody({
  children,
  className = "",
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={`min-h-0 flex-1 overflow-y-auto ${className}`}>
      {children}
    </div>
  );
}
