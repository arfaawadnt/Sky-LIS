import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, ViewEncapsulation, afterEveryRender, afterNextRender, computed, inject, input, signal, viewChild } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../icon/icon.component';

let nextRowMenuId = 0;

const ITEMS = 'button:not([disabled]), a[href]';

/**
 * `<app-row-menu>…buttons or links…</app-row-menu>` inside an `appRowActions` template: the *More* menu of a row whose
 * actions do not fit in one line (LIS-CR-007 C-06 / C-10: at most four actions inline, the rest here). The trigger is a
 * row icon button (`shared.table.moreActions`, or `label`); the projected items show in a small popover placed in the
 * top layer (so the table's scroll box never clips it) under the trigger, aligned to its inline end.
 *
 * Keyboard (WAI-ARIA menu button): Enter / Space / Arrow Down open on the first item, Arrow Up on the last; arrows move,
 * Home / End jump, Escape closes back to the trigger (and never reaches a surrounding drawer), Tab closes. Running an
 * item closes the menu; so do a click outside, a scroll or a resize. Items are the page's own buttons with their own
 * `(click)`, `*appCan` and `[disabled]`; give each an icon and a text. A menu left without items hides its trigger.
 */
@Component({
  selector: 'app-row-menu',
  imports: [IconComponent],
  template: `
    <button #trigger type="button" class="row-btn" aria-haspopup="menu" [attr.aria-expanded]="open()" [attr.aria-controls]="menuId" [attr.aria-label]="text()" [title]="text()" (click)="toggle()">
      <app-icon name="more" [size]="15" />
    </button>
    <div #menu class="row-menu-panel" role="menu" [attr.popover]="popover" [id]="menuId" [attr.aria-label]="text()" [hidden]="!open()">
      <ng-content />
    </div>
  `,
  styles: `
    app-row-menu {
      display: inline-flex;
    }

    app-row-menu[hidden] {
      display: none;
    }

    .row-menu-panel {
      position: fixed;
      inset: auto;
      margin: 0;
      z-index: 60;
      min-width: 180px;
      padding: 4px;
      display: flex;
      flex-direction: column;
      gap: 2px;
      background: var(--surface);
      color: var(--ink);
      border: 1px solid var(--line);
      border-radius: var(--r);
      box-shadow: var(--sh);
      white-space: nowrap;
      text-align: start;
    }

    .row-menu-panel[hidden] {
      display: none;
    }

    .row-menu-panel :is(button, a) {
      display: flex;
      align-items: center;
      gap: 8px;
      width: 100%;
      padding: 7px 10px;
      border: 0;
      border-radius: 6px;
      background: none;
      color: inherit;
      font: inherit;
      font-size: 13px;
      text-align: start;
      text-decoration: none;
      cursor: pointer;
    }

    .row-menu-panel :is(button, a):is(:hover, :focus-visible) {
      background: var(--row-hover);
      outline: none;
    }

    .row-menu-panel :is(button, a).danger {
      color: var(--bad);
    }

    .row-menu-panel button:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }
  `,
  // The items are the page's projected buttons: emulated styles could not reach them, so the rules are scoped by hand.
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(keydown)': 'onKeydown($event)',
    '(click)': 'onClick($event)',
    '(document:click)': 'onDocumentClick($event)',
    '(window:resize)': 'close(false)',
    '[hidden]': 'empty()',
  },
})
export class RowMenuComponent {
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly trigger = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly menu = viewChild.required<ElementRef<HTMLElement>>('menu');

  /** The trigger's accessible name and tooltip (translated); `shared.table.moreActions` by default. */
  readonly label = input<string | null>(null);

  /** The top layer where the browser has it (never clipped by a scroll box or a transformed drawer); fixed position elsewhere. */
  protected readonly popover = typeof HTMLElement !== 'undefined' && typeof HTMLElement.prototype.showPopover === 'function' ? 'manual' : null;

  protected readonly menuId = `app-row-menu-${++nextRowMenuId}`;
  protected readonly text = computed(() => this.label() ?? this.i18n.t('shared.table.moreActions'));

  readonly open = signal(false);

  /** No item survived the page's `*appCan` / `@if`: the trigger is hidden rather than opening an empty menu. */
  protected readonly empty = signal(true);

  private readonly onScroll = (event: Event): void => {
    if (!(event.target instanceof Node && this.menu().nativeElement.contains(event.target))) {
      this.close(false);
    }
  };

  constructor() {
    afterEveryRender({
      read: () => {
        const empty = this.menu().nativeElement.querySelector('button, a[href]') === null;
        if (empty !== this.empty()) {
          this.empty.set(empty);
        }
      },
    });
    inject(DestroyRef).onDestroy(() => document.removeEventListener('scroll', this.onScroll, true));
  }

  toggle(focus: 'first' | 'last' = 'first'): void {
    if (this.open()) {
      this.close(true);
    } else {
      this.show(focus);
    }
  }

  /** Closes the menu; `refocus` returns the focus to the trigger (keyboard close, item run). */
  close(refocus: boolean): void {
    if (!this.open()) {
      return;
    }

    this.open.set(false);
    document.removeEventListener('scroll', this.onScroll, true);
    const panel = this.menu().nativeElement;
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
      case 'Tab':
        this.close(false);
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

  /** An item ran (its own click handler has already fired): close and give the focus back to the trigger. */
  protected onClick(event: MouseEvent): void {
    const target = event.target as HTMLElement | null;
    const item = target?.closest<HTMLElement>(ITEMS);
    if (item && this.menu().nativeElement.contains(item)) {
      this.close(true);
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

        const panel = this.menu().nativeElement;
        if (this.popover !== null && !panel.matches(':popover-open')) {
          panel.showPopover();
        }

        this.place(panel);
        const items = this.items();
        for (const item of items) {
          item.setAttribute('role', 'menuitem');
          item.tabIndex = -1;
        }

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
    return [...this.menu().nativeElement.querySelectorAll<HTMLElement>(ITEMS)];
  }
}
