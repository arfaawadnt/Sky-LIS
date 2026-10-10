import { ChangeDetectionStrategy, Component, type ElementRef, afterNextRender, effect, inject, input, output, viewChild } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';

import type { ScanMode } from '../../../core/api/samples.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';

let nextScanBoxId = 0;

/**
 * The barcode box of the sample pages (CP-14, CP-16, CP-17): a scanner types the number and sends Enter, so Enter
 * submits; the field takes the focus when the page opens and again after every scan; Esc clears it. With a `mode`
 * the Collect / Receive toggle is shown (CP-14 "scan in collect or receive mode").
 */
@Component({
  selector: 'app-scan-box',
  imports: [ReactiveFormsModule, IconComponent],
  template: `
    <form class="scan" (submit)="onSubmit($event)" novalidate>
      @if (mode(); as current) {
        <div class="modes" role="radiogroup" [attr.aria-label]="i18n.t('samples.scan.mode')">
          @for (option of modes; track option) {
            <button
              type="button"
              class="btn mode"
              role="radio"
              [class.primary]="current === option"
              [attr.aria-checked]="current === option"
              (click)="selectMode(option)"
            >
              {{ i18n.t('samples.scan.mode.' + option) }}
            </button>
          }
        </div>
      }
      <label class="visually-hidden" [for]="inputId">{{ label() }}</label>
      <div class="field-row">
        <app-icon name="barcode" [size]="22" />
        <input
          #field
          class="inp scan-input mono"
          type="text"
          [id]="inputId"
          [formControl]="value"
          [placeholder]="placeholder()"
          autocomplete="off"
          autocapitalize="characters"
          spellcheck="false"
          maxlength="32"
          (keydown.escape)="clear($event)"
        />
        <button type="submit" class="btn primary" [disabled]="busy()">{{ submitLabel() }}</button>
      </div>
    </form>
  `,
  styles: `
    .scan {
      display: grid;
      gap: var(--sp-3);
    }

    .modes {
      display: inline-flex;
      gap: var(--sp-2);
    }

    .mode {
      min-width: 120px;
    }

    .field-row {
      display: flex;
      align-items: center;
      gap: var(--sp-2);
      color: var(--ink-2);
    }

    .scan-input {
      flex: 1;
      min-width: 0;
      font-size: var(--fs-lg);
      padding-block: var(--sp-3);
      letter-spacing: 0.04em;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ScanBoxComponent {
  protected readonly i18n = inject(I18nService);
  private readonly field = viewChild<ElementRef<HTMLInputElement>>('field');

  /** Visually hidden label of the field (already translated). */
  readonly label = input.required<string>();
  readonly placeholder = input('');
  readonly submitLabel = input.required<string>();
  readonly busy = input(false);
  /** The current mode, or null to hide the toggle. */
  readonly mode = input<ScanMode | null>(null);
  /** Takes the focus when the page opens. */
  readonly autofocus = input(true);

  readonly scanned = output<string>();
  readonly modeChange = output<ScanMode>();

  protected readonly modes: readonly ScanMode[] = ['Collect', 'Receive'];
  protected readonly inputId = `scan-${++nextScanBoxId}`;
  protected readonly value = new FormControl('', { nonNullable: true });

  constructor() {
    afterNextRender(() => {
      if (this.autofocus()) {
        this.focus();
      }
    });

    // keep the field usable while a scan is in flight, and give the focus back when it finishes
    effect(() => {
      if (!this.busy()) {
        queueMicrotask(() => this.autofocus() && this.focus());
      }
    });
  }

  focus(): void {
    this.field()?.nativeElement.focus();
  }

  protected onSubmit(event: Event): void {
    event.preventDefault();
    this.submit();
  }

  protected submit(): void {
    const text = this.value.value.trim();
    if (text.length === 0 || this.busy()) {
      return;
    }

    this.scanned.emit(text);
    this.value.setValue('');
    this.focus();
  }

  protected selectMode(mode: ScanMode): void {
    this.modeChange.emit(mode);
    this.focus();
  }

  protected clear(event: Event): void {
    if (this.value.value.length > 0) {
      event.stopPropagation();
      this.value.setValue('');
    }
  }
}
