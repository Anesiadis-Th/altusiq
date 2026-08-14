export function Card({
  children,
  className = "",
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div
      className={`rounded-panel border border-line bg-surface ${className}`}
    >
      {children}
    </div>
  );
}

// `subtitle` is for what the title doesn't say: a denominator, a unit, a caveat.
export function CardHeader({
  title,
  subtitle,
  right,
}: {
  title: string;
  subtitle?: string;
  right?: React.ReactNode;
}) {
  return (
    <div className="flex items-start gap-4 border-b border-line px-5 py-4">
      <div className="min-w-0 flex-1">
        <h2 className="text-sm font-medium text-white">{title}</h2>
        {subtitle && <p className="text-gray-500 text-xs mt-0.5">{subtitle}</p>}
      </div>
      {right}
    </div>
  );
}

export function CardBody({
  children,
  className = "",
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return <div className={`p-5 ${className}`}>{children}</div>;
}
