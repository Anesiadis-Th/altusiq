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

  useEffect(() => {
    if (!activePanel) return;

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setActivePanel(null);
    }

    function handlePointerDown(event: PointerEvent) {
      const target = event.target as Node;

      // The TopBar counts as inside. Its buttons toggle the panel themselves,
      // so closing here would let the same click reopen it.
      if (panelRef.current?.contains(target)) return;
      if (topBarRef.current?.contains(target)) return;

      setActivePanel(null);
    }

    // Escape closes anything. Outside-click is only for the anchored rails, since
    // the analytics overlay covers the viewport and would close on its own content.
    const anchored = activePanel === "search" || activePanel === "history";

    document.addEventListener("keydown", handleKeyDown);
    if (anchored) document.addEventListener("pointerdown", handlePointerDown);

    return () => {
      document.removeEventListener("keydown", handleKeyDown);
      document.removeEventListener("pointerdown", handlePointerDown);
    };
  }, [activePanel, panelRef, topBarRef]);

  return { activePanel, toggle, close };
}
