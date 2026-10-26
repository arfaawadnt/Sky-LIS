import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, ViewEncapsulation, afterNextRender, computed, inject, input, output, signal, viewChild } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../icon/icon.component';

let nextChooserId = 0;

/** One optional column as the chooser lists it: key, translated header, shown or hidden. */
export interface ColumnChoice {
  readonly key: string;
  readonly label: string;
  readonly visible: boolean;
}

const FOCUSABLE = 'input:not([disabled]), button:not([disabled])';

/**
 * LIS-CR-014 T2: the "⋯" at the end of a table's header row — a small popover listing the optional columns with a
 * checkbox each and *Reset* (every column back). Same pattern as `app-row-menu`: the panel sits in the top layer (never
 * clipped by the table's scroll box) under the trigger, aligned to its inline end. Keyboard: Enter / Space / Arrow Down
 * open on the first checkbox; Arrow Up / Down move, Space toggles, Escape closes back to the trigger (and never reaches a
 * surrounding drawer); Tab past the panel, a click outside, a scroll or a resize close it. The table decides what a toggle
 * does (save per user through `AuthStore.updateListPreferences` when it has a `listKey`).
 */
@Component({
  selector: 'app-column-chooser',
  imports: [IconComponent],
  template: `
    <button #trigger type="button" class="row-btn" aria-haspopup="dialog" [attr.aria-expanded]="open()" [attr.aria-controls]="panelId" [attr.aria-label]="text()" [title]="text()" (click)="toggle()">
      <app-icon name="more" [size]="15" />
    </button>
    <div #panel class="column-chooser-panel" role="dialog" [attr.popover]="popover" [id]="panelId" [attr.aria-label]="text()" [hidden]="!open()">
      <p class="cc-title">{{ i18n.t('shared.table.columns') }}</p>
      @for (choice of choices(); track choice.key) {
        <label class="cc-item">
          <input type="checkbox" [checked]="choice.visible" (change)="toggled.emit(choice.key)" />
          <span>{{ choice.label }}</span>
        </label>
      }
      <button type="button" class="cc-reset" [disabled]="!changed()" (click)="resetAll.emit()">{{ i18n.t('shared.table.resetColumns') }}</button>
    </div>
  `,
  styles: `
    app-column-chooser {
      display: inline-flex;
      vertical-align: middle;
    }

    .column-chooser-panel {
      position: fixed;
      inset: auto;
      margin: 0;
      z-index: 60;
      min-width: 200px;
      max-height: min(420px, 70vh);
      overflow: auto;
      padding: 6px;
      display: flex;
      flex-direction: column;
      gap: 2px;
      background: var(--surface);
      color: var(--ink);
      border: 1px solid var(--line);
      border-radius: var(--r);
      box-shadow: var(--sh);
      text-align: start;
      text-transform: none;
      letter-spacing: normal;
      font-weight: 400;
      font-size: 13px;
      white-space: nowrap;
    }

    .column-chooser-panel[hidden] {
      display: none;
    }

    .column-chooser-panel .cc-title {
      margin: 2px 8px 4px;
      color: var(--muted);
      font-size: var(--fs-xs);
      font-weight: 700;
      text-transform: uppercase;
    }

    .column-chooser-panel .cc-item {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 6px 8px;
      border-radius: 6px;
      cursor: pointer;
    }

    .column-chooser-panel .cc-item:hover {
      background: var(--row-hover);
    }

    .column-chooser-panel .cc-reset {
      margin-top: 4px;
      padding: 6px 8px;
      border: 0;
      border-top: 1px solid var(--line-soft);
      background: none;
      color: var(--blue);
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .column-chooser-panel .cc-reset:disabled {
      color: var(--muted);
      cursor: default;
    }
  `,
  // The panel lives in the top layer and is styled by hand-scoped rules, as in `app-row-menu`.
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(keydown)': 'onKeydown($event)',
    '(focusout)': 'onFocusOut($event)',
    '(document:click)': 'onDocumentClick($event)',
    '(window:resize)': 'close(false)',
  },
})
export class ColumnChooserComponent {
  protected readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly panel = viewChild.required<ElementRef<HTMLElement>>('panel');

  readonly choices = input.required<readonly ColumnChoice[]>();

  /** A column was checked or unchecked (its key). */
  readonly toggled = output<string>();
  /** *Reset*: every optional column shows again. */
  readonly resetAll = output();

  protected readonly popover = typeof HTMLElement !== 'undefined' && typeof HTMLElement.prototype.showPopover === 'function' ? 'manual' : null;
  protected readonly panelId = `app-column-chooser-${++nextChooserId}`;
  protected readonly text = computed(() => this.i18n.t('shared.table.chooseColumns'));
  protected readonly changed = computed(() => this.choices().some((choice) => !choice.visible));

  readonly open = signal(false);

  private readonly onScroll = (event: Event): void => {
    if (!(event.target instanceof Node && this.panel().nativeElement.contains(event.target))) {
      this.close(false);
    }
  };

  constructor() {
    inject(DestroyRef).onDestroy(() => document.removeEventListener('scroll', this.onScroll, true));
  }

  toggle(): void {
    if (this.open()) {
      this.close(true);
    } else {
      this.show('first');
    }
  }

  close(refocus: boolean): void {
    if (!this.open()) {
      return;
    }

    this.open.set(false);
    document.removeEventListener('scroll', this.onScroll, true);
    const panel = this.panel().nativeElement;
    if (this.popover !== null && panel.matches(':popover-open')) {
      panel.hidePopover();
    }

    if (refocus) {
      this.trigger().nativeElement.focus();
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    const fromTrigger = event.target === this.trigger().nativeElement;
    if (fromTrigger && !this.open()) {
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        this.show(event.key === 'ArrowDown' ? 'first' : 'last');
      }

      return;
    }

    if (!this.open()) {
      return;
    }

    const items = this.items();
    const index = items.indexOf(document.activeElement as HTMLElement);
    switch (event.key) {
      case 'Escape':
        event.preventDefault();
        event.stopPropagation();
        this.close(true);
        break;
      case 'ArrowDown':
        event.preventDefault();
        items[(index + 1) % items.length]?.focus();
        break;
      case 'ArrowUp':
        event.preventDefault();
        items[index <= 0 ? items.length - 1 : index - 1]?.focus();
        break;
      case 'Home':
        event.preventDefault();
        items[0]?.focus();
        break;
      case 'End':
        event.preventDefault();
        items[items.length - 1]?.focus();
        break;
      default:
        break;
    }
  }

  /** The focus left the trigger and the panel (Tab past the last item): close without taking the focus back. */
  protected onFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget;
    if (this.open() && next instanceof Node && !this.host.contains(next)) {
      this.close(false);
    }
  }

  protected onDocumentClick(event: MouseEvent): void {
    if (this.open() && !(event.target instanceof Node && this.host.contains(event.target))) {
      this.close(false);
    }
  }

  private show(focus: 'first' | 'last'): void {
    this.open.set(true);
    document.addEventListener('scroll', this.onScroll, true);
    afterNextRender(
      () => {
        if (!this.open()) {
          return;
        }

        const panel = this.panel().nativeElement;
        if (this.popover !== null && !panel.matches(':popover-open')) {
          panel.showPopover();
        }

        this.place(panel);
        const items = this.items();
        (focus === 'first' ? items[0] : items[items.length - 1])?.focus();
      },
      { injector: this.injector },
    );
  }

  /** Under the trigger, aligned to its inline end; above it when the viewport has no room below; never off screen. */
  private place(panel: HTMLElement): void {
    const anchor = this.trigger().nativeElement.getBoundingClientRect();
    const width = panel.offsetWidth;
    const height = panel.offsetHeight;
    const rtl = getComputedStyle(this.host).direction === 'rtl';
    const margin = 8;
    const left = Math.max(margin, Math.min(rtl ? anchor.left : anchor.right - width, window.innerWidth - width - margin));
    const below = anchor.bottom + 4;
    const top = below + height > window.innerHeight - margin && anchor.top - height - 4 >= margin ? anchor.top - height - 4 : below;
    panel.style.left = `${left}px`;
    panel.style.top = `${top}px`;
  }

  private items(): HTMLElement[] {
    return [...this.panel().nativeElement.querySelectorAll<HTMLElement>(FOCUSABLE)];
  }
}
