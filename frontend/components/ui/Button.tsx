// One control system: a single height (h-8), a single radius token, and real
// hover / active / focus-visible states. Ghost is the default voice; the filled
// treatment is reserved for the one primary action in a view.

// Standalone controls draw the focus outline outside their box.
export const focusRing =
  "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-500";

// Controls inside a clipped container (list rows, grouped buttons) draw it
// inside, otherwise overflow-hidden eats it.
export const focusRingInset =
  "focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-blue-500";

const control = `inline-flex h-8 items-center justify-center gap-1.5 rounded-control px-3 text-[13px] font-medium whitespace-nowrap transition-colors ${focusRing}`;

const variants = {
  ghost: "bg-surface text-gray-300 hover:bg-raised hover:text-white",
  primary: "bg-blue-500 text-white hover:bg-blue-400 active:bg-blue-500",
  quiet: "text-gray-400 hover:bg-raised hover:text-white",
} as const;

export type ButtonVariant = keyof typeof variants;

export function buttonClasses(
  variant: ButtonVariant = "ghost",
  className = "",
): string {
  return `${control} ${variants[variant]} ${className}`;
}

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
}

export function Button({
  variant = "ghost",
  className = "",
  ...props
}: ButtonProps) {
  return <button {...props} className={buttonClasses(variant, className)} />;
}

// Square icon-only control — close buttons and compact chrome.
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

// A segmented toolbar: one surface, one border, hairline dividers between
// items — instead of N identical free-floating pills.
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
