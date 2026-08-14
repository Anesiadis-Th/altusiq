"use client";

import { useEffect, useState, type RefObject } from "react";

// Returns 0 until the first measurement lands, so callers have to guard on it.
export function useElementWidth(ref: RefObject<HTMLElement | null>): number {
  const [width, setWidth] = useState(0);

  useEffect(() => {
    const element = ref.current;
    if (!element) return;

    const observer = new ResizeObserver(([entry]) => {
      setWidth(entry.contentRect.width);
    });

    observer.observe(element);
    return () => observer.disconnect();
  }, [ref]);

  return width;
}
