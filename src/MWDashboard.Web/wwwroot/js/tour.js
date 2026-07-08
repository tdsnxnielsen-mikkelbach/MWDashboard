// Guided product tour helpers built on driver.js (https://driverjs.com).
// The driver.js IIFE bundle (loaded in App.razor) exposes window.driver.js.driver.
// Steps are supplied from Blazor as { element, title, description, side, align } objects.
// Per-page "seen" state is remembered in localStorage so first-visit tours auto-start once.

const STORAGE_PREFIX = "mwd-tour-seen:";

function storageKey(pageKey) {
    return STORAGE_PREFIX + (pageKey || "");
}

// Returns true when the page's tour has not been shown before (first visit).
export function shouldAutoStart(pageKey) {
    try {
        return !localStorage.getItem(storageKey(pageKey));
    } catch {
        // Private mode / storage disabled: don't auto-nag.
        return false;
    }
}

export function markSeen(pageKey) {
    try {
        localStorage.setItem(storageKey(pageKey), "1");
    } catch {
        /* ignore storage failures */
    }
}

// Maps a Blazor step to a driver.js step. Steps whose target element is not currently
// in the DOM are dropped so the tour never highlights something that isn't rendered
// (e.g. an Export button on a page that doesn't have one, or a hidden loading section).
function toDriverSteps(steps) {
    const result = [];
    for (const s of steps || []) {
        const hasSelector = s.element && s.element.length > 0;
        if (hasSelector && !document.querySelector(s.element)) {
            continue;
        }
        result.push({
            element: hasSelector ? s.element : undefined,
            popover: {
                title: s.title || "",
                description: s.description || "",
                side: s.side || "bottom",
                align: s.align || "start"
            }
        });
    }
    return result;
}

// Starts the guided tour for a page. `force` distinguishes a manual restart (button click)
// from the first-visit auto-start; either way the page is marked seen so it won't auto-open again.
export function startTour(pageKey, steps) {
    const factory = window.driver && window.driver.js && window.driver.js.driver;
    if (!factory) {
        return false;
    }

    const driverSteps = toDriverSteps(steps);
    if (driverSteps.length === 0) {
        markSeen(pageKey);
        return false;
    }

    const isDark = document.documentElement.classList.contains("mud-theme-dark") ||
        document.body.classList.contains("mud-theme-dark") ||
        window.matchMedia?.("(prefers-color-scheme: dark)")?.matches;

    const drv = factory({
        showProgress: true,
        allowClose: true,
        smoothScroll: true,
        stagePadding: 6,
        stageRadius: 6,
        overlayColor: isDark ? "#000000" : "#1e1e2e",
        overlayOpacity: 0.65,
        popoverClass: isDark ? "mwd-tour-popover mwd-tour-dark" : "mwd-tour-popover",
        nextBtnText: "Next",
        prevBtnText: "Back",
        doneBtnText: "Done",
        steps: driverSteps,
        onDestroyed: () => markSeen(pageKey)
    });

    drv.drive();
    markSeen(pageKey);
    return true;
}
