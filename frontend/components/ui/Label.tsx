// The one label style in the app. Use it everywhere so the UI reads as one system.
export function MicroLabel({
  children,
  className = "",
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <span
      className={`block text-gray-500 text-[11px] font-medium uppercase tracking-wider ${className}`}
    >
      {children}
    </span>
  );
}
