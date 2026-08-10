import { MicroLabel } from "./Label";

export function Card({
  children,
  className = "",
}: {
  children: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={`bg-surface rounded-panel ${className}`}>{children}</div>
  );
}

export function CardHeader({
  title,
  subtitle,
}: {
  title: string;
  subtitle?: string;
}) {
  return (
    <div className="border-b border-line px-5 py-4">
      <h2 className="text-sm font-medium text-white">{title}</h2>
      {subtitle && <p className="text-gray-500 text-xs mt-0.5">{subtitle}</p>}
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

// A headerless card: micro-label, one big mono figure, optional hint.
export function StatCard({
  label,
  value,
  hint,
}: {
  label: string;
  value: string;
  hint?: string;
}) {
  return (
    <Card className="px-5 py-4">
      <MicroLabel>{label}</MicroLabel>
      <p className="text-white text-2xl font-semibold font-mono tabular-nums mt-1.5">
        {value}
      </p>
      {hint && <p className="text-gray-600 text-xs mt-1.5">{hint}</p>}
    </Card>
  );
}
