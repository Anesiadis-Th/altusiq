"use client";

import { useEffect, useState, type RefObject } from "react";

export type Panel = "search" | "history" | "analytics";

interface UseActivePanelOptions {
  panelRef: RefObject<HTMLElement | null>;
  topBarRef: RefObject<HTMLElement | null>;
}

export function useActivePanel({
  panelRef,
  topBarRef,
}: UseActivePanelOptions) {
  const [activePanel, setActivePanel] = useState<Panel | null>(null);

  function toggle(panel: Panel) {
    setActivePanel((current) => (current === panel ? null : panel));
  }

  function close() {
    setActivePanel(null);
  }

  // Dismissal for the two anchored panels. Analytics is excluded: it is a
  // full-screen overlay, so every click lands inside it anyway.
  useEffect(() => {
    if (activePanel !== "search" && activePanel !== "history") return;

    function handlePointerDown(event: PointerEvent) {
      const target = event.target as Node;

      // The TopBar is not "outside": its buttons toggle the panel themselves
      // and closing here first would let the click immediately reopen it.
      if (panelRef.current?.contains(target)) return;
      if (topBarRef.current?.contains(target)) return;

      setActivePanel(null);
    }

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setActivePanel(null);
    }

    document.addEventListener("pointerdown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);

    return () => {
      document.removeEventListener("pointerdown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [activePanel, panelRef, topBarRef]);

  return { activePanel, toggle, close };
}
