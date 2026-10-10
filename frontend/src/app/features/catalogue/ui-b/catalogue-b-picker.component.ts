import { ChangeDetectionStrategy, Component, DestroyRef, computed, forwardRef, inject, input, signal } from '@angular/core';
import { type ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import type { CatalogueBOption } from '../data-b/catalogue-b-lookups';

let nextPickerId = 0;

/**
 * `<app-catalogue-b-picker formControlName="serviceIds" [search]="searchServices" [known]="labels" />`: a multi-select
 * of ids (services, branches) with chips and a type-ahead list. The form value is the ordered id array. Keyboard:
 * ↑/↓ move in the list, Enter adds the highlighted (or first) match, Backspace on an empty box removes the last chip,
 * Esc closes the list without closing the drawer. `known` gives the labels of ids selected before the page opened.
 */
@Component({
  selector: 'app-catalogue-b-picker',
  imports: [IconComponent],
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => CatalogueBPickerComponent), multi: true }],
  template: `
    <div class="picker" [class.disabled]="disabled()">
      @if (selected().length > 0) {
        <ul class="chips" [attr.aria-label]="label()">
          @for (id of selected(); track id; let index = $index) {
            <li class="chip" [class.inactive]="optionOf(id).inactive === true">
              @if (ordered()) {
                <span class="order">{{ index + 1 }}</span>
              }
              @if (optionOf(id).code) {
                <span class="mono code">{{ optionOf(id).code }}</span>
              }
              <span class="name">{{ nameOf(optionOf(id)) }}</span>
              <button type="button" class="chip-x" [disabled]="disabled()" (click)="remove(id)" [attr.aria-label]="i18n.t('catalogue-b.picker.remove') + ' ' + nameOf(optionOf(id))">
                <app-icon name="x" [size]="12" />
              </button>
            </li>
          }
        </ul>
      } @else if (emptyText()) {
        <p class="empty muted small">{{ emptyText() }}</p>
      }
      <div class="box">
        <input
          type="text"
          class="inp"
          role="combobox"
          aria-autocomplete="list"
          autocomplete="off"
          spellcheck="false"
          [attr.aria-label]="label()"
          [attr.aria-expanded]="open()"
          [attr.aria-controls]="listId"
          [attr.aria-activedescendant]="activeDescendant()"
          [placeholder]="placeholder() || i18n.t('catalogue-b.picker.placeholder')"
          [value]="term()"
          [disabled]="disabled()"
          (input)="onInput($event)"
          (keydown)="onKeydown($event)"
          (focus)="onFocus()"
          (blur)="onBlur()"
        />
        @if (open()) {
          <ul class="results" role="listbox" [id]="listId" [attr.aria-label]="label()">
            @for (option of results(); track option.id; let index = $index) {
              <li
                role="option"
                [id]="listId + '-' + index"
                [class.active]="index === activeIndex()"
                [class.picked]="isSelected(option.id)"
                [attr.aria-selected]="isSelected(option.id)"
                (mousedown)="$event.preventDefault()"
                tabindex="-1"
                (click)="toggle(option)"
                (keydown.enter)="toggle(option)"
              >
                @if (option.code) {
                  <span class="mono code">{{ option.code }}</span>
                }
                <span class="name">{{ nameOf(option) }}</span>
                @if (isSelected(option.id)) {
                  <app-icon name="check" [size]="14" />
                }
              </li>
            } @empty {
              <li class="muted small none" role="presentation">{{ searching() ? i18n.t('catalogue-b.picker.searching') : i18n.t('catalogue-b.picker.noMatch') }}</li>
            }
          </ul>
        }
      </div>
    </div>
  `,
  styles: `
    :host { display: block; }
    .picker { display: grid; gap: var(--sp-2); }
    .picker.disabled { opacity: 0.7; }
    .chips { display: flex; flex-wrap: wrap; gap: var(--sp-1); margin: 0; padding: 0; list-style: none; }
    .chip { display: inline-flex; align-items: center; gap: var(--sp-1); padding-block: 2px; padding-inline: var(--sp-2) 2px; border: 1px solid var(--line); border-radius: var(--r-pill); background: var(--blue-soft); color: var(--ink); font-size: var(--fs-sm); max-inline-size: 100%; }
    .chip.inactive { background: var(--surface-2); color: var(--muted); text-decoration: line-through; }
    .chip .order { font-weight: 600; color: var(--blue); }
    .chip .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .chip-x { display: inline-grid; place-items: center; inline-size: 20px; block-size: 20px; border: 0; border-radius: 50%; background: transparent; color: inherit; cursor: pointer; }
    .chip-x:hover, .chip-x:focus-visible { background: var(--line-soft); }
    .code { color: var(--ink-2); }
    .empty { margin: 0; }
    .box { position: relative; }
    .box .inp { inline-size: 100%; }
    .results { position: absolute; inset-inline: 0; inset-block-start: calc(100% + 2px); z-index: 5; max-block-size: 240px; overflow-y: auto; margin: 0; padding: var(--sp-1) 0; list-style: none; background: var(--paper); border: 1px solid var(--line); border-radius: var(--r); box-shadow: var(--sh-lg); }
    .results li { display: flex; align-items: center; gap: var(--sp-2); padding: var(--sp-1) var(--sp-3); cursor: pointer; font-size: var(--fs-sm); }
    .results li.active, .results li:hover { background: var(--row-hover); }
    .results li.picked .name { font-weight: 600; }
    .results li.none { cursor: default; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CatalogueBPickerComponent implements ControlValueAccessor {
  protected readonly i18n = inject(I18nService);

  /** Returns the matches of a text (debounced 250 ms; an empty text lists the first matches). */
  readonly search = input.required<(term: string) => Promise<readonly CatalogueBOption[]>>();
  /** Labels of ids that were selected before (the record's linked services / branches). */
  readonly known = input<readonly CatalogueBOption[]>([]);
  /** Accessible name of the box and the list (already translated). */
  readonly label = input('');
  readonly placeholder = input('');
  /** Shown when nothing is selected (for example "All services"). */
  readonly emptyText = input('');
  /** Numbers the chips (the order services are added in). */
  readonly ordered = input(false);

  protected readonly listId = `catalogue-b-picker-${++nextPickerId}`;
  protected readonly selected = signal<readonly string[]>([]);
  protected readonly term = signal('');
  protected readonly results = signal<readonly CatalogueBOption[]>([]);
  protected readonly open = signal(false);
  protected readonly searching = signal(false);
  protected readonly activeIndex = signal(-1);
  protected readonly disabled = signal(false);

  private readonly seen = signal<ReadonlyMap<string, CatalogueBOption>>(new Map());
  private readonly labels = computed(() => {
    const map = new Map<string, CatalogueBOption>(this.known().map((option) => [option.id, option]));
    for (const [id, option] of this.seen()) {
      map.set(id, option);
    }

    return map;
  });

  protected readonly activeDescendant = computed(() => (this.open() && this.activeIndex() >= 0 ? `${this.listId}-${this.activeIndex()}` : null));

  private onChange: (value: readonly string[]) => void = () => undefined;
  private onTouched: () => void = () => undefined;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private sequence = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.clearTimer());
  }

  writeValue(value: unknown): void {
    this.selected.set(Array.isArray(value) ? value.filter((id): id is string => typeof id === 'string') : []);
  }

  registerOnChange(fn: (value: readonly string[]) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  protected optionOf(id: string): CatalogueBOption {
    return this.labels().get(id) ?? { id, code: id.slice(0, 8).toUpperCase(), nameEn: '', nameAr: '' };
  }

  protected nameOf(option: CatalogueBOption): string {
    return (this.i18n.lang() === 'ar' ? option.nameAr || option.nameEn : option.nameEn || option.nameAr) || '';
  }

  protected isSelected(id: string): boolean {
    return this.selected().includes(id);
  }

  protected onInput(event: Event): void {
    this.term.set((event.target as HTMLInputElement).value);
    this.open.set(true);
    this.scheduleSearch();
  }

  protected onFocus(): void {
    this.open.set(true);
    if (this.results().length === 0) {
      this.scheduleSearch(0);
    }
  }

  protected onBlur(): void {
    this.open.set(false);
    this.activeIndex.set(-1);
    this.onTouched();
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.results().length;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.open.set(true);
        this.activeIndex.set(count === 0 ? -1 : (this.activeIndex() + 1) % count);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.activeIndex.set(count === 0 ? -1 : (this.activeIndex() - 1 + count) % count);
        break;
      case 'Enter': {
        if (!this.open()) {
          return;
        }

        event.preventDefault();
        const option = this.results()[Math.max(this.activeIndex(), 0)];
        if (option) {
          this.toggle(option);
        }

        break;
      }
      case 'Escape':
        if (this.open()) {
          event.preventDefault();
          event.stopPropagation();
          this.open.set(false);
        }

        break;
      case 'Backspace': {
        const last = this.selected().at(-1);
        if (this.term() === '' && last !== undefined) {
          this.remove(last);
        }

        break;
      }
      default:
        break;
    }
  }

  protected toggle(option: CatalogueBOption): void {
    if (this.disabled()) {
      return;
    }

    if (this.isSelected(option.id)) {
      this.remove(option.id);
      return;
    }

    this.seen.update((current) => new Map(current).set(option.id, option));
    this.commit([...this.selected(), option.id]);
    this.term.set('');
    this.scheduleSearch(0);
  }

  protected remove(id: string): void {
    this.commit(this.selected().filter((selectedId) => selectedId !== id));
  }

  private commit(ids: readonly string[]): void {
    this.selected.set(ids);
    this.onChange(ids);
    this.onTouched();
  }

  private scheduleSearch(delay = 250): void {
    this.clearTimer();
    this.timer = setTimeout(() => void this.runSearch(), delay);
  }

  private async runSearch(): Promise<void> {
    const sequence = ++this.sequence;
    this.searching.set(true);
    try {
      const options = await this.search()(this.term().trim());
      if (sequence === this.sequence) {
        this.results.set(options);
        this.activeIndex.set(options.length > 0 ? 0 : -1);
      }
    } catch {
      if (sequence === this.sequence) {
        this.results.set([]);
      }
    } finally {
      if (sequence === this.sequence) {
        this.searching.set(false);
      }
    }
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}
