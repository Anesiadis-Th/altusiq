"use client";

export function ChartTooltip({
  x,
  y,
  children,
}: {
  x: number;
  y: number;
  children: React.ReactNode;
}) {
  return (
    <div
      className="pointer-events-none absolute z-10 -translate-x-1/2 -translate-y-full rounded-control border border-line bg-app px-2 py-1.5 whitespace-nowrap"
      style={{ left: x, top: y - 8 }}
    >
      {children}
    </div>
  );
}

// Identity rides the line key. Never colour the text with the series colour.
export function TooltipRow({
  keyClass,
  value,
  label,
}: {
  keyClass?: string;
  value: string;
  label: string;
}) {
  return (
    <span className="flex items-center gap-1.5 text-xs">
      {keyClass && (
        <span aria-hidden className={`h-0.5 w-2.5 shrink-0 ${keyClass}`} />
      )}
      <span className="font-mono font-medium tabular-nums text-white">
        {value}
      </span>
      <span className="text-gray-400">{label}</span>
    </span>
  );
}

export function Legend({
  items,
}: {
  items: { keyClass: string; label: string }[];
}) {
  return (
    <div className="flex items-center gap-3">
      {items.map((item) => (
        <span
          key={item.label}
          className="flex items-center gap-1.5 text-gray-400 text-xs"
        >
          <span aria-hidden className={`h-2 w-2 rounded-[1px] ${item.keyClass}`} />
          {item.label}
        </span>
      ))}
    </div>
  );
}

// Every value a tooltip shows has to be reachable without a pointer too.
export function DataTable({
  caption,
  columns,
  rows,
}: {
  caption: string;
  columns: string[];
  rows: (string | number)[][];
}) {
  // sr-only goes on the wrapper, never the table. A display:table box sizes to
  // its content and ignores the 1px clamp, so it renders full size and unhidden.
  return (
    <div className="sr-only">
      <table>
        <caption>{caption}</caption>
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column} scope="col">
                {column}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i}>
              {row.map((cell, j) => (
                <td key={j}>{cell}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
