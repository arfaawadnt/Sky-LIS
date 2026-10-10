import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, input, output, signal, viewChild } from '@angular/core';

import type { ServiceLabel } from '../../../core/api/pricing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';

let nextPickerId = 0;

/**
 * Typeahead over the catalogue's services (combobox pattern): type a code or a name, ↑/↓ moves through the matches,
 * Enter adds the highlighted (or the first) match — the SRS rule "Enter adds a typed line" —, Esc clears. The search
 * itself is the page's (`search` input), so the component stays free of HTTP.
 */
@Component({
  selector: 'app-service-picker',
  imports: [IconComponent],
  template: `
    <div class="picker">
      <app-icon name="search" [size]="15" />
      <input
        #box
        type="text"
        class="inp"
        role="combobox"
        autocomplete="off"
        spellcheck="false"
        [attr.aria-label]="label()"
        [attr.aria-expanded]="open()"
        [attr.aria-controls]="listId"
        [attr.aria-activedescendant]="open() && active() >= 0 ? listId + '-' + active() : null"
        [placeholder]="placeholder()"
        [value]="term()"
        [disabled]="disabled()"
        (input)="onInput($event)"
        (keydown)="onKeydown($event)"
        (blur)="onBlur()"
      />
      @if (open()) {
        <ul class="options" role="listbox" [id]="listId">
          @if (searching() && results().length === 0) {
            <li class="hint" role="presentation">{{ i18n.t('pricing.picker.searching') }}</li>
          } @else if (results().length === 0) {
            <li class="hint" role="presentation">{{ i18n.t('pricing.picker.none') }}</li>
          }
          @for (service of results(); track service.id; let index = $index) {
            <li
              role="option"
              [id]="listId + '-' + index"
              [class.active]="index === active()"
              [attr.aria-selected]="index === active()"
              [class.taken]="isTaken(service)"
              (mousedown)="$event.preventDefault()"
              (click)="choose(service)"
              (keydown.enter)="choose(service)"
              tabindex="-1"
            >
              <span class="mono code">{{ service.code }}</span>
              <span class="name">{{ i18n.lang() === 'ar' ? service.nameAr : service.nameEn }}</span>
              @if (isTaken(service)) {
                <span class="muted small">{{ i18n.t('pricing.picker.taken') }}</span>
              }
            </li>
          }
        </ul>
      }
    </div>
  `,
  styles: `
    .picker {
      position: relative;
      display: inline-flex;
      align-items: center;
      gap: var(--sp-2);
      min-width: min(360px, 100%);
      color: var(--muted);
    }
    .picker input {
      flex: 1;
    }
    .options {
      position: absolute;
      inset-block-start: calc(100% + 4px);
      inset-inline: 0;
      z-index: var(--z-sticky);
      max-height: 280px;
      margin: 0;
      padding: var(--sp-1) 0;
      overflow-y: auto;
      list-style: none;
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: var(--r);
      box-shadow: var(--sh);
    }
    .options li {
      display: flex;
      gap: var(--sp-2);
      align-items: baseline;
      padding: var(--sp-2) var(--sp-3);
      color: var(--ink);
      cursor: pointer;
    }
    .options li.active {
      background: var(--blue-soft);
    }
    .options li.taken .name {
      color: var(--muted);
    }
    .options li.hint {
      color: var(--muted);
      cursor: default;
    }
    .code {
      min-width: 70px;
      color: var(--ink-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServicePickerComponent {
  protected readonly i18n = inject(I18nService);
  private readonly box = viewChild.required<ElementRef<HTMLInputElement>>('box');

  /** Runs the catalogue search for a term. */
  readonly search = input.required<(term: string) => Promise<readonly ServiceLabel[]>>();
  /** Ids already on the grid (shown, but adding them only focuses the existing row). */
  readonly taken = input<ReadonlySet<string>>(new Set());
  readonly placeholder = input('');
  readonly label = input('');
  readonly disabled = input(false);
  readonly debounce = input(250);
  readonly picked = output<ServiceLabel>();

  protected readonly listId = `app-service-picker-${++nextPickerId}`;
  protected readonly term = signal('');
  protected readonly results = signal<readonly ServiceLabel[]>([]);
  protected readonly searching = signal(false);
  protected readonly active = signal(-1);
  protected readonly open = computed(() => this.term().trim().length > 0);

  private timer: ReturnType<typeof setTimeout> | null = null;
  private sequence = 0;

  protected onInput(event: Event): void {
    this.term.set((event.target as HTMLInputElement).value);
    this.schedule();
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.results().length;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.active.set(count === 0 ? -1 : (this.active() + 1) % count);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.active.set(count === 0 ? -1 : (this.active() - 1 + count) % count);
        break;
      case 'Enter': {
        event.preventDefault();
        const chosen = this.results()[this.active() >= 0 ? this.active() : 0];
        if (chosen) {
          this.choose(chosen);
        }

        break;
      }
      case 'Escape':
        if (this.term() !== '') {
          event.preventDefault();
          event.stopPropagation();
          this.clear();
        }

        break;
    }
  }

  protected onBlur(): void {
    // keep the typed text, close the list once focus has really left
    setTimeout(() => {
      if (this.box().nativeElement !== this.box().nativeElement.ownerDocument.activeElement) {
        this.active.set(-1);
      }
    }, 150);
  }

  protected isTaken(service: ServiceLabel): boolean {
    return this.taken().has(service.id);
  }

  protected choose(service: ServiceLabel): void {
    this.picked.emit(service);
    this.clear();
    this.box().nativeElement.focus();
  }

  private clear(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }

    this.sequence++;
    this.term.set('');
    this.results.set([]);
    this.active.set(-1);
    this.searching.set(false);
    this.box().nativeElement.value = '';
  }

  private schedule(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
    }

    const term = this.term().trim();
    if (term === '') {
      this.results.set([]);
      this.active.set(-1);
      return;
    }

    this.searching.set(true);
    this.timer = setTimeout(() => void this.run(term), this.debounce());
  }

  private async run(term: string): Promise<void> {
    const sequence = ++this.sequence;
    try {
      const found = await this.search()(term);
      if (sequence === this.sequence) {
        this.results.set(found);
        this.active.set(found.length > 0 ? 0 : -1);
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
}
