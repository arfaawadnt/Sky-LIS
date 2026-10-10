import { Injectable } from '@angular/core';

interface Registration {
  readonly element: HTMLInputElement;
  readonly fallback: boolean;
}

/**
 * Where Ctrl+K goes (SRS keyboard rules): the first search box of the page in document order; when the page has
 * none, the shell's global search box (registered as `fallback`). Search boxes register themselves on creation.
 */
@Injectable({ providedIn: 'root' })
export class SearchFocusService {
  private readonly boxes = new Set<Registration>();

  register(element: HTMLInputElement, fallback: boolean): () => void {
    const registration: Registration = { element, fallback };
    this.boxes.add(registration);
    return () => this.boxes.delete(registration);
  }

  /** Focuses (and selects) the target box; returns false when none is registered. */
  focusFirst(): boolean {
    const candidates = [...this.boxes].filter((box) => box.element.isConnected);
    const pageBoxes = candidates.filter((box) => !box.fallback).sort(byDocumentOrder);
    const target = pageBoxes[0] ?? candidates.find((box) => box.fallback);
    if (!target) {
      return false;
    }

    target.element.focus();
    target.element.select();
    return true;
  }
}

function byDocumentOrder(a: Registration, b: Registration): number {
  const position = a.element.compareDocumentPosition(b.element);
  if (position & Node.DOCUMENT_POSITION_FOLLOWING) {
    return -1;
  }

  if (position & Node.DOCUMENT_POSITION_PRECEDING) {
    return 1;
  }

  return 0;
}
