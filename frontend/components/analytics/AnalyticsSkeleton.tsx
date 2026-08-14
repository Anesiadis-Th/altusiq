// Ships eagerly as the fallback for the lazy content chunk, so keep the chart
// and airport-table imports out of here.
function Block({ className = "" }: { className?: string }) {
  return <div className={`animate-pulse rounded-control bg-raised ${className}`} />;
}

function CardBlock({ className = "" }: { className?: string }) {
  return (
    <div className={`rounded-panel border border-line bg-surface p-5 ${className}`}>
      <Block className="h-3 w-32" />
      <Block className="mt-4 h-44 w-full" />
    </div>
  );
}

export default function AnalyticsSkeleton() {
  return (
    <div aria-hidden>
      <Block className="h-12 w-48" />
      <Block className="mt-3 h-3 w-72" />

      <div className="mt-6 flex flex-col gap-4">
        <CardBlock />
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <CardBlock />
          <CardBlock />
        </div>
        <CardBlock />
      </div>
    </div>
  );
}
