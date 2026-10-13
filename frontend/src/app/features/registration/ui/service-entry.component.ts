import { ChangeDetectionStrategy, Component, DestroyRef, type ElementRef, computed, inject, input, output, resource, signal, viewChild } from '@angular/core';

import type { ServiceLookupItem } from '../../../core/api/catalogue-a.contracts';
import type { ServiceEntryItem } from '../../../core/api/registration.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { RegistrationApi } from '../data/registration.api';
import { count, pick } from '../data/registration-format';

const SUGGEST_MIN_LENGTH = 2;
const SUGGEST_LIMIT = 8;
const SUGGEST_DELAY_MS = 250;

/** A service chosen at the desk. */
export interface PickedService {
  readonly serviceId: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

/**
 * The keyboard-first service entry of CP-06 / CP-12 (LDM §4.4 "shortcut number + Enter"): while the clerk types, the
 * matching catalogue services are suggested (↑↓ chooses, Enter adds the chosen one, Esc closes the list); Enter without a
 * chosen suggestion resolves the typed text on the server (shortcut number, code, short name, alias, then a shortcut set);
 * an ambiguous or unknown text opens the picker with the candidates; F2 opens the picker (Catalogue A's lookup) at any
 * time, and ↑↓ + Enter work in its list too. Emits the chosen services.
 */
@Component({
  selector: 'app-reg-service-entry',
  imports: [DialogComponent, SearchBoxComponent, IconComponent],
  template: `
    <div class="entry">
      <label class="visually-hidden" for="reg-service-entry">{{ i18n.t('registration.services.entryLabel') }}</label>
      <div class="combo">
        <div class="command" [class.disabled]="disabled()" [class.invalid]="invalid()">
          <span class="prompt" aria-hidden="true">›</span>
          <input
            #entry
            id="reg-service-entry"
            type="text"
            role="combobox"
            autocomplete="off"
            aria-autocomplete="list"
            aria-controls="reg-service-suggestions"
            [attr.aria-expanded]="suggestionsOpen()"
            [attr.aria-activedescendant]="activeId()"
            [attr.aria-invalid]="invalid() || null"
            [placeholder]="i18n.t('registration.services.entryPlaceholder')"
            [disabled]="disabled()"
            [value]="text()"
            (input)="onInput($event)"
            (keydown)="onKeydown($event)"
            (blur)="closeSuggestions()"
          />
          @if (resolving()) {
            <span class="small muted" role="status">{{ i18n.t('registration.services.resolving') }}</span>
          } @else {
            <span class="keys small" aria-hidden="true">{{ i18n.t('registration.services.keys') }}</span>
          }
        </div>
        @if (suggestionsOpen()) {
          <ul id="reg-service-suggestions" class="suggestions" role="listbox" [attr.aria-label]="i18n.t('registration.services.suggestions')">
            @for (item of suggestions(); track item.id; let index = $index) {
              <li [id]="'reg-service-option-' + index" role="option" [attr.aria-selected]="index === active()">
                <!-- not in the tab order: the input keeps the focus and ↑↓ move the active option (aria-activedescendant) -->
                <button type="button" tabindex="-1" class="option" [class.active]="index === active()" (mousedown)="$event.preventDefault()" (click)="pick(item)">
                  <span class="mono muted">{{ item.shortcutNo ?? '' }}</span>
                  <b class="mono">{{ item.code }}</b>
                  <span class="option-name">{{ name(item) }}</span>
                  <span class="small muted">{{ item.type }}</span>
                </button>
              </li>
            }
          </ul>
        }
      </div>
      <button type="button" class="btn" [disabled]="disabled()" (click)="openPicker(text())"><app-icon name="search" [size]="15" />{{ i18n.t('registration.services.browse') }} <kbd>F2</kbd></button>
    </div>

    <app-dialog [(open)]="pickerOpen" [title]="i18n.t('registration.services.pickerTitle')" size="lg">
      <div tabindex="-1" (keydown)="onPickerKeydown($event)">
        <app-search-box [value]="pickerQuery()" [placeholder]="i18n.t('registration.services.pickerPlaceholder')" [label]="i18n.t('registration.services.pickerPlaceholder')" (searchChange)="pickerQuery.set($event)" />
        @if (candidates().length > 0 && pickerQuery() === initialQuery()) {
          <p class="small muted">{{ i18n.t('registration.services.candidatesHint') }}</p>
        }
        <ul class="picker-list" [attr.aria-label]="i18n.t('registration.services.pickerTitle')">
          @for (item of rows(); track item.id) {
            <li>
              <button type="button" class="pick" (click)="pick(item)">
                <span class="mono">{{ item.shortcutNo ?? '' }}</span>
                <b class="mono">{{ item.code }}</b>
                <span>{{ name(item) }}</span>
                <span class="small muted">{{ item.type }}</span>
              </button>
            </li>
          } @empty {
            <li class="muted small">{{ lookup.isLoading() ? i18n.t('registration.loading') : i18n.t('registration.services.noMatch') }}</li>
          }
        </ul>
      </div>
      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="pickerOpen.set(false)">{{ i18n.t('registration.close') }}</button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    .entry {
      display: flex;
      gap: var(--sp-2);
      align-items: center;
      flex-wrap: wrap;
    }

    .entry > .btn {
      min-height: 46px;
    }

    .combo {
      position: relative;
      flex: 1 1 320px;
      min-width: 0;
    }

    .command {
      display: flex;
      align-items: center;
      gap: var(--sp-2);
      min-height: 46px;
      padding: 0 var(--sp-3);
      border: 2px solid var(--blue);
      border-radius: var(--r);
      background: var(--surface);
      box-sizing: border-box;
    }

    .command:focus-within {
      box-shadow: var(--focus-ring);
    }

    .command.invalid {
      border-color: var(--bad);
    }

    .command.disabled {
      border-color: var(--line);
      background: var(--surface-2);
    }

    .command input {
      flex: 1;
      min-width: 0;
      border: 0;
      outline: none;
      background: transparent;
      color: var(--ink);
      font: inherit;
      font-family: var(--mono);
      font-size: var(--fs-lg);
    }

    .prompt {
      color: var(--blue-2);
      font-family: var(--mono);
      font-weight: 700;
    }

    .keys {
      color: var(--ink-2);
      white-space: nowrap;
    }

    .suggestions {
      position: absolute;
      inset-inline: 0;
      inset-block-start: calc(100% + 4px);
      z-index: var(--z-sticky);
      list-style: none;
      margin: 0;
      padding: var(--sp-1);
      border: 1px solid var(--line);
      border-radius: var(--r);
      background: var(--surface);
      box-shadow: var(--sh-lg);
      max-height: 320px;
      overflow: auto;
    }

    .option {
      display: grid;
      grid-template-columns: 3.5em 7em minmax(0, 1fr) 6em;
      gap: var(--sp-2);
      align-items: center;
      width: 100%;
      min-height: 38px;
      padding: 0 var(--sp-2);
      border: 0;
      border-radius: var(--r-sm);
      background: transparent;
      color: inherit;
      font: inherit;
      text-align: start;
      cursor: pointer;
    }

    .option.active,
    .option:hover {
      background: var(--blue-soft);
    }

    .option-name {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    kbd {
      margin-inline-start: var(--sp-1);
      padding: 0 var(--sp-1);
      border: 1px solid var(--line);
      border-radius: 4px;
      font-family: var(--mono);
      font-size: var(--fs-xs);
    }

    .picker-list {
      list-style: none;
      margin: var(--sp-3) 0 0;
      padding: 0;
      max-height: 50vh;
      overflow: auto;
    }

    .pick {
      display: grid;
      grid-template-columns: 4em 7em minmax(0, 1fr) 7em;
      gap: var(--sp-2);
      align-items: baseline;
      width: 100%;
      padding: var(--sp-2);
      border: 0;
      border-bottom: 1px solid var(--line);
      background: transparent;
      text-align: start;
      cursor: pointer;
      color: inherit;
      font: inherit;
    }

    .pick:hover,
    .pick:focus-visible {
      background: var(--brand-soft, rgba(0, 0, 0, 0.04));
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.f2)': 'onF2($event)' },
})
export class ServiceEntryComponent {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(RegistrationApi);
  private readonly toast = inject(ToastService);
  private readonly entry = viewChild<ElementRef<HTMLInputElement>>('entry');
  private suggestTimer: ReturnType<typeof setTimeout> | undefined;

  readonly disabled = input(false);
  /** Marked by the host after a refused save (no service added, or one refused). */
  readonly invalid = input(false);
  readonly added = output<readonly PickedService[]>();

  protected readonly text = signal('');
  protected readonly resolving = signal(false);
  protected readonly pickerOpen = signal(false);
  protected readonly pickerQuery = signal('');
  protected readonly initialQuery = signal('');
  protected readonly candidates = signal<readonly ServiceEntryItem[]>([]);
  /** The text the suggestion list was asked for (debounced, two characters at least); empty while the list is closed. */
  protected readonly suggestQuery = signal('');
  protected readonly active = signal(-1);

  /**
   * The catalogue page the picker lists: the typed term, or with an empty box the first page of active services, so F2
   * browses the catalogue at once (UAT A-13: nothing was listed until a term was typed). The candidates of an unknown
   * entry are shown instead while the box still holds the entry's text.
   */
  protected readonly lookup = resource({
    params: () => (this.pickerOpen() && !this.showsCandidates() ? this.pickerQuery().trim() : undefined),
    loader: ({ params, abortSignal }) => this.api.lookupServices(params, abortSignal),
  });

  /** The services suggested under the entry while the clerk types (same catalogue lookup as the picker). */
  protected readonly suggestLookup = resource({
    params: () => (this.suggestQuery().length >= SUGGEST_MIN_LENGTH && !this.pickerOpen() ? this.suggestQuery() : undefined),
    loader: ({ params, abortSignal }) => this.api.lookupServices(params, abortSignal),
  });

  private readonly showsCandidates = computed(() => this.pickerQuery() === this.initialQuery() && this.candidates().length > 0);

  protected readonly rows = computed<readonly (ServiceEntryItem | ServiceLookupItem)[]>(() => {
    if (this.showsCandidates()) {
      return this.candidates();
    }

    return this.lookup.hasValue() ? this.lookup.value().items : [];
  });

  protected readonly suggestions = computed<readonly ServiceLookupItem[]>(() =>
    this.suggestQuery() !== '' && this.suggestLookup.hasValue() ? this.suggestLookup.value().items.slice(0, SUGGEST_LIMIT) : []);

  protected readonly suggestionsOpen = computed(() => this.suggestions().length > 0 && !this.resolving() && !this.pickerOpen());

  protected readonly activeId = computed(() => (this.suggestionsOpen() && this.active() >= 0 ? `reg-service-option-${this.active()}` : null));

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.suggestTimer));
  }

  focus(): void {
    this.entry()?.nativeElement.focus();
  }

  protected onInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.text.set(value);
    this.active.set(-1);
    clearTimeout(this.suggestTimer);
    const query = value.trim();
    if (query.length < SUGGEST_MIN_LENGTH) {
      this.suggestQuery.set('');
      return;
    }

    this.suggestTimer = setTimeout(() => this.suggestQuery.set(query), SUGGEST_DELAY_MS);
  }

  protected async onKeydown(event: KeyboardEvent): Promise<void> {
    if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && this.suggestionsOpen()) {
      event.preventDefault();
      this.active.set(step(this.active(), this.suggestions().length, event.key === 'ArrowDown' ? 1 : -1));
      return;
    }

    if (event.key === 'Escape' && this.suggestionsOpen()) {
      event.preventDefault();
      this.closeSuggestions();
      return;
    }

    if (event.key === 'Enter') {
      event.preventDefault();
      const chosen = this.suggestionsOpen() && this.active() >= 0 ? this.suggestions()[this.active()] : undefined;
      if (chosen) {
        this.pick(chosen);
        return;
      }

      this.closeSuggestions();
      await this.resolve(this.text());
    }
  }

  /** ↑↓ move between the search box and the picker's rows (UAT: the list was mouse- and Tab-only). */
  protected onPickerKeydown(event: KeyboardEvent): void {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') {
      return;
    }

    const container = event.currentTarget as HTMLElement;
    const buttons = Array.from(container.querySelectorAll<HTMLButtonElement>('.picker-list .pick'));
    if (buttons.length === 0) {
      return;
    }

    event.preventDefault();
    const current = buttons.indexOf(document.activeElement as HTMLButtonElement);
    const next = step(current, buttons.length, event.key === 'ArrowDown' ? 1 : -1);
    if (next < 0) {
      container.querySelector<HTMLInputElement>('app-search-box input')?.focus();
    } else {
      buttons[next]?.focus();
    }
  }

  protected closeSuggestions(): void {
    clearTimeout(this.suggestTimer);
    this.suggestQuery.set('');
    this.active.set(-1);
  }

  protected onF2(event: Event): void {
    if (this.disabled() || this.pickerOpen()) {
      return;
    }

    event.preventDefault();
    this.closeSuggestions();
    this.openPicker(this.text());
  }

  protected openPicker(query: string, candidates: readonly ServiceEntryItem[] = []): void {
    this.candidates.set(candidates);
    this.initialQuery.set(candidates.length > 0 ? query.trim() : '');
    this.pickerQuery.set(query.trim());
    this.pickerOpen.set(true);
  }

  protected pick(item: ServiceEntryItem | ServiceLookupItem): void {
    this.added.emit([{ serviceId: item.id, code: item.code, nameEn: item.nameEn, nameAr: item.nameAr }]);
    this.pickerOpen.set(false);
    this.closeSuggestions();
    this.text.set('');
    this.focus();
  }

  protected name(item: ServiceEntryItem | ServiceLookupItem): string {
    return pick(item.nameEn, item.nameAr, this.i18n.lang());
  }

  private async resolve(raw: string): Promise<void> {
    const text = raw.trim();
    if (text === '' || this.resolving()) {
      return;
    }

    this.resolving.set(true);
    try {
      const result = await this.api.serviceEntry(text);
      if (result.kind === 'None') {
        this.openPicker(text, result.candidates);
        return;
      }

      this.added.emit(result.services.map((item) => ({ serviceId: item.id, code: item.code, nameEn: item.nameEn, nameAr: item.nameAr })));
      if (result.kind === 'ShortcutSet') {
        this.toast.success(this.i18n.tc('registration.services.setAdded', result.services.length, { code: result.setCode ?? text, count: count(result.services.length, this.i18n.lang()) }));
      }

      this.text.set('');
    } catch {
      this.openPicker(text);
    } finally {
      this.resolving.set(false);
    }
  }
}

/** The next index of a list walked with ↑↓, where −1 is "nothing chosen" (the input itself). */
function step(current: number, length: number, delta: 1 | -1): number {
  const next = current + delta;
  if (next >= length) {
    return -1;
  }

  return next < -1 ? length - 1 : next;
}
