import { ChangeDetectionStrategy, Component, DestroyRef, afterNextRender, inject, input, model, output, signal, viewChild, type ElementRef } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../icon/icon.component';
import { SearchFocusService } from './search-focus.service';

let nextSearchId = 0;

/**
 * `<app-search-box [(value)]="…" (searchChange)="facade.setSearch($event)" [placeholder]="…" />`: a search input
 * with a clear button. `value` follows every keystroke; `searchChange` emits after `debounce` ms of silence (and immediately
 * on Enter or clear). Ctrl+K focuses the first search box of the page (see `SearchFocusService`); the shell's
 * global box registers with `fallback`.
 */
@Component({
  selector: 'app-search-box',
  imports: [IconComponent],
  template: `
    <label class="search-wrap" [class.dark]="dark()">
      <span class="visually-hidden">{{ label() ?? i18n.t('shared.search.label') }}</span>
      <app-icon name="search" [size]="16" />
      <input
        #field
        class="inp"
        type="search"
        [id]="id"
        [value]="value()"
        [placeholder]="placeholder() ?? i18n.t('shared.search.placeholder')"
        [attr.aria-keyshortcuts]="'Control+K'"
        autocomplete="off"
        spellcheck="false"
        (input)="onInput($event)"
        (keydown.enter)="flush()"
        (keydown.escape)="clear()"
      />
      @if (value().length > 0) {
        <button type="button" class="clear" (click)="clear()" [attr.aria-label]="i18n.t('shared.search.clear')"><app-icon name="x" [size]="14" /></button>
      }
    </label>
  `,
  styleUrl: './search-box.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SearchBoxComponent {
  protected readonly i18n = inject(I18nService);
  private readonly focusRegistry = inject(SearchFocusService);
  private readonly field = viewChild.required<ElementRef<HTMLInputElement>>('field');
  protected readonly id = `app-search-${++nextSearchId}`;

  readonly value = model('');
  readonly placeholder = input<string | null>(null);
  readonly label = input<string | null>(null);
  readonly debounce = input(300);
  /** The shell's global box: Ctrl+K reaches it only when the page has no search box of its own. */
  readonly fallback = input(false);
  /** Dark variant for the sidebar/topbar. */
  readonly dark = input(false);
  readonly searchChange = output<string>();

  private readonly timer = signal<ReturnType<typeof setTimeout> | null>(null);
  private lastEmitted = '';

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const unregister = this.focusRegistry.register(this.field().nativeElement, this.fallback());
      destroyRef.onDestroy(unregister);
    });
    destroyRef.onDestroy(() => this.cancelTimer());
  }

  focus(): void {
    this.field().nativeElement.focus();
  }

  protected onInput(event: Event): void {
    this.value.set((event.target as HTMLInputElement).value);
    this.cancelTimer();
    this.timer.set(setTimeout(() => this.flush(), this.debounce()));
  }

  protected flush(): void {
    this.cancelTimer();
    const text = this.value().trim();
    if (text === this.lastEmitted) {
      return;
    }

    this.lastEmitted = text;
    this.searchChange.emit(text);
  }

  protected clear(): void {
    this.value.set('');
    this.flush();
    this.field().nativeElement.focus();
  }

  private cancelTimer(): void {
    const timer = this.timer();
    if (timer !== null) {
      clearTimeout(timer);
      this.timer.set(null);
    }
  }
}
