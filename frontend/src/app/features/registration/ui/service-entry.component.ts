import { ChangeDetectionStrategy, Component, type ElementRef, computed, inject, input, output, resource, signal, viewChild } from '@angular/core';

import type { ServiceLookupItem } from '../../../core/api/catalogue-a.contracts';
import type { ServiceEntryItem } from '../../../core/api/registration.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { RegistrationApi } from '../data/registration.api';
import { pick } from '../data/registration-format';

/** A service chosen at the desk. */
export interface PickedService {
  readonly serviceId: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
}

/**
 * The keyboard-first service entry of CP-06 / CP-12 (LDM §4.4 "shortcut number + Enter"): Enter resolves the typed text
 * on the server (shortcut number, code, short name, alias, then a shortcut set); an ambiguous or unknown text opens the
 * picker with the candidates; F2 opens the picker (Catalogue A's lookup) at any time. Emits the chosen services.
 */
@Component({
  selector: 'app-reg-service-entry',
  imports: [DialogComponent, SearchBoxComponent, IconComponent],
  template: `
    <div class="entry">
      <label class="visually-hidden" for="reg-service-entry">{{ i18n.t('registration.services.entryLabel') }}</label>
      <input
        #entry
        id="reg-service-entry"
        class="inp"
        type="text"
        autocomplete="off"
        [placeholder]="i18n.t('registration.services.entryPlaceholder')"
        [disabled]="disabled()"
        [value]="text()"
        (input)="onInput($event)"
        (keydown)="onKeydown($event)"
      />
      <button type="button" class="btn" [disabled]="disabled()" (click)="openPicker(text())"><app-icon name="search" [size]="15" />{{ i18n.t('registration.services.browse') }} (F2)</button>
      @if (resolving()) {
        <span class="small muted" role="status">{{ i18n.t('registration.services.resolving') }}</span>
      }
    </div>

    <app-dialog [(open)]="pickerOpen" [title]="i18n.t('registration.services.pickerTitle')" size="lg">
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

    .entry .inp {
      flex: 1 1 260px;
      max-width: 460px;
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

  readonly disabled = input(false);
  readonly added = output<readonly PickedService[]>();

  protected readonly text = signal('');
  protected readonly resolving = signal(false);
  protected readonly pickerOpen = signal(false);
  protected readonly pickerQuery = signal('');
  protected readonly initialQuery = signal('');
  protected readonly candidates = signal<readonly ServiceEntryItem[]>([]);

  protected readonly lookup = resource({
    params: () => (this.pickerOpen() && this.pickerQuery().trim() !== '' && this.pickerQuery() !== this.initialQuery() ? this.pickerQuery().trim() : undefined),
    loader: ({ params, abortSignal }) => this.api.lookupServices(params, abortSignal),
  });

  protected readonly rows = computed<readonly (ServiceEntryItem | ServiceLookupItem)[]>(() => {
    if (this.pickerQuery() === this.initialQuery() && this.candidates().length > 0) {
      return this.candidates();
    }

    return this.lookup.hasValue() ? this.lookup.value().items : [];
  });

  focus(): void {
    this.entry()?.nativeElement.focus();
  }

  protected onInput(event: Event): void {
    this.text.set((event.target as HTMLInputElement).value);
  }

  protected async onKeydown(event: KeyboardEvent): Promise<void> {
    if (event.key === 'Enter') {
      event.preventDefault();
      await this.resolve(this.text());
    }
  }

  protected onF2(event: Event): void {
    if (this.disabled() || this.pickerOpen()) {
      return;
    }

    event.preventDefault();
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
        this.toast.success(this.i18n.tf('registration.services.setAdded', { code: result.setCode ?? text, count: result.services.length }));
      }

      this.text.set('');
    } catch {
      this.openPicker(text);
    } finally {
      this.resolving.set(false);
    }
  }
}
