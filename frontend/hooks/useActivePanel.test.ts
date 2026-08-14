import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { useActivePanel } from "./useActivePanel";

// Stand-ins for the open panel, the TopBar, and the map behind them.
let panel: HTMLDivElement;
let topBar: HTMLDivElement;
let map: HTMLDivElement;

beforeEach(() => {
  panel = document.createElement("div");
  topBar = document.createElement("div");
  map = document.createElement("div");
  document.body.append(panel, topBar, map);
});

afterEach(() => {
  document.body.innerHTML = "";
});

function setup() {
  return renderHook(() =>
    useActivePanel({
      panelRef: { current: panel },
      topBarRef: { current: topBar },
    }),
  );
}

function pointerDownOn(node: HTMLElement) {
  act(() => {
    node.dispatchEvent(
      new PointerEvent("pointerdown", { bubbles: true, cancelable: true }),
    );
  });
}

function pressKey(key: string) {
  act(() => {
    document.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true }));
  });
}

describe("useActivePanel", () => {
  it("starts with every panel closed", () => {
    expect(setup().result.current.activePanel).toBeNull();
  });

  it("opens a panel", () => {
    const { result } = setup();
    act(() => result.current.toggle("search"));
    expect(result.current.activePanel).toBe("search");
  });

  it("closes a panel when its own button is clicked again", () => {
    const { result } = setup();
    act(() => result.current.toggle("search"));
    act(() => result.current.toggle("search"));
    expect(result.current.activePanel).toBeNull();
  });

  // The bug this hook exists to prevent: Search and History share an anchor and
  // a z-index, so both open renders one on top of the other.
  it("replaces the open panel instead of stacking a second one", () => {
    const { result } = setup();
    act(() => result.current.toggle("search"));
    act(() => result.current.toggle("history"));
    expect(result.current.activePanel).toBe("history");
  });

  it("never leaves two panels open across every ordering", () => {
    const panels = ["search", "history", "analytics"] as const;
    for (const first of panels) {
      for (const second of panels) {
        const { result } = setup();
        act(() => result.current.toggle(first));
        act(() => result.current.toggle(second));
        // A single value cannot represent two open panels, which is the point.
        expect(result.current.activePanel).toBe(first === second ? null : second);
      }
    }
  });

  it("closes on close()", () => {
    const { result } = setup();
    act(() => result.current.toggle("history"));
    act(() => result.current.close());
    expect(result.current.activePanel).toBeNull();
  });

  describe("dismissal", () => {
    it("closes when the click lands outside", () => {
      const { result } = setup();
      act(() => result.current.toggle("search"));
      pointerDownOn(map);
      expect(result.current.activePanel).toBeNull();
    });

    it("stays open when the click lands inside the panel", () => {
      const { result } = setup();
      act(() => result.current.toggle("search"));
      pointerDownOn(panel);
      expect(result.current.activePanel).toBe("search");
    });

    it("stays open when the click lands inside a descendant of the panel", () => {
      const child = document.createElement("input");
      panel.append(child);
      const { result } = setup();
      act(() => result.current.toggle("search"));
      pointerDownOn(child);
      expect(result.current.activePanel).toBe("search");
    });

    // Otherwise clicking Search while it is open closes it here and the
    // button's own toggle reopens it.
    it("treats the TopBar as inside so its buttons keep working", () => {
      const { result } = setup();
      act(() => result.current.toggle("search"));
      pointerDownOn(topBar);
      expect(result.current.activePanel).toBe("search");
    });

    it("closes on Escape", () => {
      const { result } = setup();
      act(() => result.current.toggle("history"));
      pressKey("Escape");
      expect(result.current.activePanel).toBeNull();
    });

    it("ignores other keys", () => {
      const { result } = setup();
      act(() => result.current.toggle("history"));
      pressKey("a");
      expect(result.current.activePanel).toBe("history");
    });

    // The analytics overlay covers the viewport, so a click that looks outside
    // is really on its own scrim — dismissing there would close it on contact.
    it("ignores outside clicks while analytics is open", () => {
      const { result } = setup();
      act(() => result.current.toggle("analytics"));
      pointerDownOn(map);
      expect(result.current.activePanel).toBe("analytics");
    });

    it("closes analytics on Escape", () => {
      const { result } = setup();
      act(() => result.current.toggle("analytics"));
      pressKey("Escape");
      expect(result.current.activePanel).toBeNull();
    });

    it("stops listening once closed", () => {
      const { result } = setup();
      act(() => result.current.toggle("search"));
      pointerDownOn(map);
      expect(result.current.activePanel).toBeNull();
      // A second stray click must not throw or resurrect anything.
      pointerDownOn(map);
      expect(result.current.activePanel).toBeNull();
    });

    it("removes its listeners on unmount", () => {
      const { result, unmount } = setup();
      act(() => result.current.toggle("search"));
      unmount();
      // Would throw on a state update after unmount if listeners leaked.
      pointerDownOn(map);
      pressKey("Escape");
    });
  });
});
