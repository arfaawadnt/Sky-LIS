const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

function isVisible(element: HTMLElement): boolean {
  if (element.hidden || element.closest('[hidden]')) {
    return false;
  }

  const style = getComputedStyle(element);
  return style.display !== 'none' && style.visibility !== 'hidden';
}

/** The focusable elements inside a root, in document order, skipping hidden ones. */
export function focusablesIn(root: HTMLElement): HTMLElement[] {
  return [...root.querySelectorAll<HTMLElement>(FOCUSABLE)].filter(isVisible);
}

/**
 * Moves focus into an overlay: `[data-autofocus]` when present, else the first focusable element of the
 * content (so a form opens on its first field, not on the header's close button), else of the whole root, else
 * the root itself.
 */
export function focusFirst(root: HTMLElement, content: HTMLElement | null = null): void {
  const preferred = root.querySelector<HTMLElement>('[data-autofocus]');
  const target = preferred ?? (content ? focusablesIn(content)[0] : undefined) ?? focusablesIn(root)[0] ?? root;
  target.focus();
}

/** Keeps Tab / Shift+Tab inside the root; call from the root's keydown handler. */
export function trapTab(event: KeyboardEvent, root: HTMLElement): void {
  if (event.key !== 'Tab') {
    return;
  }

  const focusables = focusablesIn(root);
  if (focusables.length === 0) {
    event.preventDefault();
    root.focus();
    return;
  }

  const first = focusables[0];
  const last = focusables[focusables.length - 1];
  const active = document.activeElement;
  if (event.shiftKey && (active === first || active === root)) {
    event.preventDefault();
    last?.focus();
  } else if (!event.shiftKey && active === last) {
    event.preventDefault();
    first?.focus();
  }
}
