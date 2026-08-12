import type { ExpressionSpecification } from "mapbox-gl";

// Every glyph must be drawn nose-up in a 24x24 viewBox: the layer rotates icons by
// heading, so artwork on any other baseline renders permanently skewed.
const FILL = "#63b3ed";
const STROKE = "#1a202c";

function svg(body: string, strokeWidth = 1.5): string {
  return (
    `<svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24">` +
    `<g fill="${FILL}" stroke="${STROKE}" stroke-width="${strokeWidth}" ` +
    `stroke-linejoin="round" stroke-linecap="round">${body}</g></svg>`
  );
}

const AIRLINER = svg(
  `<path d="M21 16V14L13 9V3.5C13 2.67 12.33 2 11.5 2C10.67 2 10 2.67 10 3.5V9L2 14V16L10 13.5V19L8 20.5V22L11.5 21L15 22V20.5L13 19V13.5L21 16Z"/>`,
);

const HEAVY = svg(
  `<path d="M23 18.2V16.2L13.6 9.4V4.2C13.6 2.7 12.9 1.6 12 1.6C11.1 1.6 10.4 2.7 10.4 4.2V9.4L1 16.2V18.2L10.4 15.2V19.2L7.4 21.4V22.6L12 21.5L16.6 22.6V21.4L13.6 19.2V15.2Z"/>`,
);

const LIGHT = svg(
  `<path d="M21 14.6V12.6L13.2 10.2V5.4C13.2 4.2 12.7 3.4 12 3.4C11.3 3.4 10.8 4.2 10.8 5.4V10.2L3 12.6V14.6L10.8 13.4V18L8.6 20V21.2L12 20.4L15.4 21.2V20L13.2 18V13.4Z"/>`,
);

const JET = svg(
  `<path d="M20.5 19.6V17.6L13.4 11V6.6L12 1.8L10.6 6.6V11L3.5 17.6V19.6L10.6 17V19.8L9 21.8V23L12 21.9L15 23V21.8L13.4 19.8V17Z"/>`,
);

// One closed path, blades behind the body, blades on a diagonal — all three are
// legibility fixes at 24px. Separate shapes each get their own outline and fragment
// the silhouette, blades over the body split it, horizontal blades read as wings.
const HELICOPTER = svg(
  `<path d="M4.55 13.08L18.47 3.32L19.45 4.71L5.53 14.47Z"/>` +
    `<path d="M19.45 13.08L5.53 3.32L4.55 4.71L18.47 14.47Z"/>` +
    `<path d="M12 4.6C14.1 4.6 15.5 6.5 15.5 8.9C15.5 11 14.4 12.5 12.85 13L12.7 18.9H14.9V20.6H9.1V18.9H11.3L11.15 13C9.6 12.5 8.5 11 8.5 8.9C8.5 6.5 9.9 4.6 12 4.6Z"/>`,
  1.25,
);

export const AIRCRAFT_ICONS: Readonly<Record<string, string>> = {
  aircraft: AIRLINER,
  "aircraft-heavy": HEAVY,
  "aircraft-light": LIGHT,
  "aircraft-jet": JET,
  "aircraft-heli": HELICOPTER,
};

// Coarse by design — only shape class reads at 24px. Category 3 (15,500-75,000 lb)
// is regional and business jets, so it keeps the airliner icon. Everything unlisted,
// including 0/1 "no information" and a missing field, falls through to AIRLINER.
const CATEGORY_ICONS: ReadonlyArray<readonly [readonly number[], string]> = [
  [[5, 6], "aircraft-heavy"], // high-vortex large, heavy
  [[2, 9, 12], "aircraft-light"], // light, glider, ultralight
  [[7], "aircraft-jet"], // high performance (>5g, >400kt)
  [[8], "aircraft-heli"], // rotorcraft
];

const CATEGORY_SIZES: ReadonlyArray<readonly [readonly number[], number]> = [
  [[5, 6], 1.15],
  [[2, 9, 12], 0.78],
  [[7], 0.85],
  [[8], 0.85],
];

function matchOnCategory<T extends string | number>(
  branches: ReadonlyArray<readonly [readonly number[], T]>,
  fallback: T,
): ExpressionSpecification {
  return [
    "match",
    ["coalesce", ["get", "category"], -1],
    ...branches.flatMap(([categories, value]) => [[...categories], value]),
    fallback,
  ] as unknown as ExpressionSpecification;
}

// Both must go on `aircraft-layer` AND `aircraft-selected`: the selected plane is an
// exact overdraw of its copy in aircraft-layer, so any disagreement becomes visible.
export const iconImageExpression = matchOnCategory(CATEGORY_ICONS, "aircraft");
export const iconSizeExpression = matchOnCategory(CATEGORY_SIZES, 1);

interface IconTarget {
  hasImage(id: string): boolean;
  addImage(
    id: string,
    image: HTMLImageElement,
    options?: { pixelRatio?: number },
  ): void;
}

function decode(source: string): Promise<HTMLImageElement> {
  const image = new Image(48, 48);
  return new Promise((resolve, reject) => {
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error("Failed to load aircraft icon"));
    image.src = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(source)}`;
  });
}

// Must resolve before the layers are added: an `icon-image` naming an image the style
// does not have renders nothing at all, and only surfaces as a console warning.
export async function loadAircraftIcons(map: IconTarget): Promise<void> {
  await Promise.all(
    Object.entries(AIRCRAFT_ICONS).map(async ([id, source]) => {
      if (map.hasImage(id)) return;
      map.addImage(id, await decode(source), { pixelRatio: 2 });
    }),
  );
}
