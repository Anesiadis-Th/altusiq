// One height, one radius, one set of states for every control in the app.

export const focusRing =
  "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-500";

// For controls inside a clipped container, otherwise overflow-hidden eats the ring.
export const focusRingInset =
  "focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-blue-500";

const variants = {
  ghost: "bg-surface text-gray-300 hover:bg-raised hover:text-white",
  primary: "bg-blue-500 text-white hover:bg-blue-400 active:bg-blue-500",
  quiet: "text-gray-400 hover:bg-raised hover:text-white",
} as const;

export type ButtonVariant = keyof typeof variants;

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
}

export function IconButton({
  variant = "quiet",
  className = "",
  ...props
}: ButtonProps) {
  return (
    <button
      {...props}
      className={`inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-control transition-colors ${focusRing} ${variants[variant]} ${className}`}
    />
  );
}

// Segmented toolbar: one surface with hairline dividers, not N floating pills.
export function ControlGroup({
  children,
  className = "",
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div
      className={`inline-flex h-8 items-center rounded-control border border-line bg-surface ${className}`}
    >
      {children}
    </div>
  );
}

export function GroupDivider() {
  return <span aria-hidden className="h-4 w-px shrink-0 bg-line" />;
}

interface GroupButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  active?: boolean;
}

export const groupItemClasses = (active = false, className = ""): string =>
  `inline-flex h-full items-center justify-center gap-1.5 px-2.5 text-[13px] font-medium whitespace-nowrap transition-colors first:rounded-l-control last:rounded-r-control ${focusRingInset} ${
    active ? "bg-raised text-white" : "text-gray-400 hover:bg-raised hover:text-white"
  } ${className}`;

export function GroupButton({
  active = false,
  className = "",
  ...props
}: GroupButtonProps) {
  return (
    <button
      {...props}
      aria-pressed={active}
      className={groupItemClasses(active, className)}
    />
  );
}
