// `count` is the number of intervals, not the number of labels.
export function niceAxis(
  rawMax: number,
  count = 3,
): { max: number; ticks: number[] } {
  if (!Number.isFinite(rawMax) || rawMax <= 0) return { max: 1, ticks: [0, 1] };

  const rough = rawMax / count;
  const magnitude = 10 ** Math.floor(Math.log10(rough));
  const step =
    [1, 2, 2.5, 5, 10].map((m) => m * magnitude).find((s) => s >= rough) ??
    10 * magnitude;

  const max = Math.ceil(rawMax / step) * step;

  const ticks: number[] = [];
  for (let v = 0; v <= max + step / 1000; v += step) ticks.push(v);

  return { max, ticks };
}

// Lines only. Bars and columns encode magnitude as length, so they have to keep
// a zero baseline and want niceAxis instead.
export function niceRange(
  min: number,
  max: number,
  count = 3,
): { min: number; max: number; ticks: number[] } {
  if (!Number.isFinite(min) || !Number.isFinite(max) || max <= min) {
    const fallback = niceAxis(max, count);
    return { min: 0, ...fallback };
  }

  const rough = (max - min) / count;
  const magnitude = 10 ** Math.floor(Math.log10(rough));
  const step =
    [1, 2, 2.5, 5, 10].map((m) => m * magnitude).find((s) => s >= rough) ??
    10 * magnitude;

  const low = Math.floor(min / step) * step;
  const high = Math.ceil(max / step) * step;

  const ticks: number[] = [];
  for (let v = low; v <= high + step / 1000; v += step) ticks.push(v);

  return { min: low, max: high, ticks };
}

// SVG has no per-corner radius, so a rounded cap has to be drawn as a path.
export function roundedTopBar(
  x: number,
  y: number,
  width: number,
  height: number,
  radius = 4,
): string {
  const r = Math.max(0, Math.min(radius, width / 2, height));
  const right = x + width;
  const bottom = y + height;

  return [
    `M${x},${bottom}`,
    `L${x},${y + r}`,
    `Q${x},${y} ${x + r},${y}`,
    `L${right - r},${y}`,
    `Q${right},${y} ${right},${y + r}`,
    `L${right},${bottom}`,
    "Z",
  ].join(" ");
}

export function formatCount(value: number): string {
  return value.toLocaleString("en-US");
}

// Day buckets parse as UTC midnight, so rendering one in the viewer's zone
// shifts the label a day west of Greenwich.
export function formatUtcDay(
  date: string,
  month: "short" | "long" = "short",
): string {
  return new Date(`${date}T00:00:00Z`).toLocaleDateString(undefined, {
    day: "numeric",
    month,
    timeZone: "UTC",
  });
}
