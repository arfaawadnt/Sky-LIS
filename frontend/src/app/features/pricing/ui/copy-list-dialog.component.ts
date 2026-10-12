import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import { ProblemCodes, type ProblemDetails } from '../../../core/api/problem-details';
import { type CopyBasePriceListRequest, type CopyOperation, PRICE_LIMITS, ROUNDING_STEPS } from '../../../core/api/pricing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedTextGroup } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { UppercaseDirective } from '../../../shared/forms/uppercase.directive';
import { codeValidator, requiredText } from '../../../shared/forms/validators';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { LocalizedTextInputComponent } from '../../../shared/ui/localized-text-input/localized-text-input.component';
import { roundToStep } from '../data/money';

/** The list being copied, as the dialog needs it. */
export interface CopySource {
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly year: number;
}

/** PRC-101: a base price list with this code already exists. */
const LIST_CODE_TAKEN = 'PRC-101';

const MAX_CODE_LENGTH = 16;

/**
 * "Copy list" of CP-33 (LDM "Copy price list"): a new list from the source with × factor or + amount and a rounding
 * step; the new list is created inactive. Dumb: emits the request; the page runs the mutation and passes `pending`/`problem`.
 */
@Component({
  selector: 'app-copy-list-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, LocalizedTextInputComponent, SaveShortcutDirective, UppercaseDirective],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.t('pricing.copy.title')" size="md">
      <form [formGroup]="form" (ngSubmit)="submit()" appSaveShortcut (appSaveShortcut)="submit()" novalidate>
        <p class="muted small">{{ i18n.tf('pricing.copy.help', { code: source()?.code ?? '' }) }}</p>
        @if (bannerProblem(); as current) {
          <p class="problem" role="alert">{{ problemText(current) }}</p>
        }
        <div class="form-grid">
          <app-form-field [label]="i18n.t('pricing.lists.field.code')" [hint]="i18n.t('pricing.lists.hint.code')" [problem]="codeProblem()">
            <input type="text" formControlName="code" appUppercase maxlength="16" autocomplete="off" spellcheck="false" />
          </app-form-field>
          <app-form-field [label]="i18n.t('pricing.lists.field.year')">
            <input type="number" formControlName="year" min="2000" max="2100" step="1" />
          </app-form-field>
          <app-localized-text-input class="full" [group]="form.controls.name" [label]="i18n.t('pricing.lists.field.name')" [maxLength]="200" [problem]="problem()" />
          <app-form-field [label]="i18n.t('pricing.copy.operation')">
            <select formControlName="operation">
              <option value="Multiply">{{ i18n.t('pricing.copy.multiply') }}</option>
              <option value="Add">{{ i18n.t('pricing.copy.add') }}</option>
            </select>
          </app-form-field>
          <app-form-field [label]="i18n.t(form.controls.operation.value === 'Multiply' ? 'pricing.copy.factor' : 'pricing.copy.amount')">
            <input type="number" formControlName="value" step="0.01" />
          </app-form-field>
          <app-form-field [label]="i18n.t('pricing.copy.rounding')">
            <select formControlName="roundingStep">
              @for (step of steps; track step) {
                <option [ngValue]="step">{{ step }}</option>
              }
            </select>
          </app-form-field>
          <p class="example small" aria-live="polite">{{ example() }}</p>
        </div>
      </form>
      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="open.set(false)">{{ i18n.t('pricing.cancel') }}</button>
        <button type="button" class="btn primary" [disabled]="pending()" (click)="submit()">{{ i18n.t(pending() ? 'pricing.copy.copying' : 'pricing.copy.submit') }}</button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    form {
      display: grid;
      gap: var(--sp-3);
    }
    .example {
      align-self: end;
      margin: 0;
      color: var(--ink-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CopyListDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly source = input<CopySource | null>(null);
  readonly pending = input(false);
  readonly problem = input<ProblemDetails | null>(null);
  /** Codes of existing lists (any status) the page read before opening: the proposal skips them (UAT B-06). */
  readonly takenCodes = input<ReadonlySet<string>>(new Set<string>());
  readonly confirmCopy = output<CopyBasePriceListRequest>();

  protected readonly steps = ROUNDING_STEPS;

  protected readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [requiredText, codeValidator, Validators.maxLength(16)] }),
    name: localizedTextGroup({}, { required: true, maxLength: 200 }),
    year: new FormControl(2027, { nonNullable: true, validators: [Validators.required, Validators.min(2000), Validators.max(2100)] }),
    operation: new FormControl<CopyOperation>('Multiply', { nonNullable: true }),
    value: new FormControl(1.1, { nonNullable: true, validators: [Validators.required, Validators.min(-PRICE_LIMITS.maxAmount), Validators.max(PRICE_LIMITS.maxAmount)] }),
    roundingStep: new FormControl(1, { nonNullable: true }),
  });

  private readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });

  /** The code of the last submit: a duplicate-code refusal stays under the field until the code is changed. */
  private readonly submittedCode = signal<string | null>(null);

  /** PRC-101 (code taken) is a field error under the code, not a banner (UAT B-06). */
  protected readonly codeProblem = computed<ProblemDetails | null>(() => {
    const problem = this.problem();
    if (problem?.code !== LIST_CODE_TAKEN || cleanCode(this.value().code ?? '') !== this.submittedCode()) {
      return null;
    }

    return { ...problem, code: ProblemCodes.validationFailed, errors: { code: [problemMessage(this.i18n, 'pricing', problem)] }, rules: null };
  });

  protected readonly bannerProblem = computed(() => {
    const problem = this.problem();
    return problem?.code === LIST_CODE_TAKEN ? null : problem;
  });

  /** "100 → 110" with the current operation, value and step. */
  protected readonly example = computed(() => {
    const value = this.value();
    const amount = Number(value.value ?? 0);
    const step = Number(value.roundingStep ?? 1);
    const raw = value.operation === 'Add' ? 100 + amount : 100 * amount;
    return this.i18n.tf('pricing.copy.example', { result: roundToStep(raw, step).toFixed(2) });
  });

  constructor() {
    // every opening starts from the source: next year's code, name and year
    effect(() => {
      const source = this.source();
      if (this.open() && source) {
        untracked(() => this.reset(source));
      }
    });
  }

  protected submit(): void {
    if (this.pending() || !markAllTouched(this.form)) {
      return;
    }

    const value = this.form.getRawValue();
    this.submittedCode.set(cleanCode(value.code));
    this.confirmCopy.emit({
      code: value.code.trim(),
      nameEn: value.name.en.trim(),
      nameAr: value.name.ar.trim(),
      year: value.year,
      operation: value.operation,
      value: value.value,
      roundingStep: value.roundingStep,
    });
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'pricing', problem);
  }

  private reset(source: CopySource): void {
    const year = Math.min(source.year + 1, 2100);
    this.submittedCode.set(null);
    this.form.reset({
      code: freeCode(nextCode(source.code, source.year, year), this.takenCodes()),
      name: { en: source.nameEn.replace(String(source.year), String(year)), ar: source.nameAr.replace(String(source.year), String(year)) },
      year,
      operation: 'Multiply',
      value: 1.1,
      roundingStep: 1,
    });
  }
}

function cleanCode(code: string): string {
  return code.trim().toUpperCase();
}

/**
 * What the page searches the existing lists for before opening the dialog: the start of next year's code, short enough
 * that every `-n` variant `freeCode` may propose contains it (UAT B-06).
 */
export function copyCodeStem(source: CopySource): string {
  return nextCode(source.code, source.year, Math.min(source.year + 1, 2100)).slice(0, MAX_CODE_LENGTH - 4);
}

/**
 * The proposal when it is free, otherwise the first free `<code>-2`, `<code>-3`, … (cut to 16 characters), so the dialog
 * never proposes an existing list's code, active or not (UAT B-06). The server still refuses a taken code (PRC-101).
 */
export function freeCode(candidate: string, taken: ReadonlySet<string>): string {
  const code = cleanCode(candidate);
  const used = new Set([...taken].map(cleanCode));
  if (!used.has(code)) {
    return code;
  }

  for (let suffix = 2; suffix < 1000; suffix++) {
    const tail = `-${suffix}`;
    const next = `${code.slice(0, MAX_CODE_LENGTH - tail.length)}${tail}`;
    if (!used.has(next)) {
      return next;
    }
  }

  return code;
}

/** `PL26` of 2026 → `PL27`; `MAIN-2026` → `MAIN-2027`; otherwise `<code>-COPY`. */
export function nextCode(code: string, fromYear: number, toYear: number): string {
  const full = String(fromYear);
  const short = full.slice(2);
  if (code.includes(full)) {
    return code.replace(full, String(toYear)).slice(0, 16);
  }

  if (code.endsWith(short)) {
    return `${code.slice(0, -short.length)}${String(toYear).slice(2)}`.slice(0, 16);
  }

  return `${code.slice(0, 11)}-COPY`;
}
