import { ChangeDetectionStrategy, Component, computed, effect, inject, input, model, output, signal, untracked } from '@angular/core';

import type { ProblemDetails } from '../../../core/api/problem-details';
import type { ImportPriceLinesResult, ImportPriceRowRequest } from '../../../core/api/pricing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { type ParsedPriceSheet, PriceSheetError, type PriceSheetFault, parsePriceWorkbook } from '../data/excel';

/**
 * "Upload Excel" of the price list editor (CR 2026-10-19 C21): the chosen `.xlsx` is parsed in the browser (first sheet,
 * header row by name), the rows are sent as a dry run (`check`) and the server's row errors are listed; without errors the
 * user confirms *Apply* (`apply`). Dumb: the page runs the mutation and passes `pending` / `result` / `problem`.
 */
@Component({
  selector: 'app-import-prices-dialog',
  imports: [DialogComponent, IconComponent],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.t('pricing.import.title')" size="lg">
      <p class="muted small">{{ i18n.tf('pricing.import.help', { code: listCode() }) }}</p>

      <label class="file">
        <span class="btn">
          <app-icon name="arrow-up" [size]="15" />{{ i18n.t('pricing.import.chooseFile') }}
        </span>
        <input type="file" accept=".xlsx" class="visually-hidden" [disabled]="pending() || parsing()" (change)="onFile($event)" />
        <span class="file-name">{{ fileName() ?? i18n.t('pricing.import.noFile') }}</span>
      </label>

      @if (parsing()) {
        <p class="muted small" role="status">{{ i18n.t('pricing.import.parsing') }}</p>
      }
      @if (parseFault(); as fault) {
        <p class="problem" role="alert">{{ i18n.t('pricing.import.fault.' + fault) }}</p>
      }
      @if (problem(); as current) {
        <p class="problem" role="alert">{{ problemText(current) }}</p>
      }

      @if (parsed(); as sheet) {
        <p class="small summary" role="status">
          {{ i18n.tf('pricing.import.parsed', { sheet: sheet.sheetName, rows: sheet.rows.length, header: sheet.headerRow }) }}
          @if (sheet.skipped > 0) {
            · {{ i18n.tc('pricing.import.skipped', sheet.skipped) }}
          }
        </p>
      }

      @if (result(); as outcome) {
        @if (outcome.errors.length > 0) {
          <p class="problem" role="alert">{{ i18n.tc('pricing.import.refused', outcome.errors.length) }}</p>
          <div class="errors">
            <table>
              <caption class="visually-hidden">{{ i18n.t('pricing.import.errorsCaption') }}</caption>
              <thead>
                <tr>
                  <th scope="col">{{ i18n.t('pricing.import.column.row') }}</th>
                  <th scope="col">{{ i18n.t('pricing.import.column.code') }}</th>
                  <th scope="col">{{ i18n.t('pricing.import.column.problem') }}</th>
                </tr>
              </thead>
              <tbody>
                @for (error of outcome.errors; track error.row) {
                  <tr>
                    <td class="mono">{{ error.row }}</td>
                    <td class="mono">{{ error.serviceCode ?? '—' }}</td>
                    <td>{{ rowErrorText(error.code) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else {
          <p class="ok small" role="status">{{ i18n.tf('pricing.import.ready', { accepted: outcome.accepted, added: outcome.added, changed: outcome.changed }) }}</p>
        }
      }

      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="open.set(false)">{{ i18n.t('pricing.cancel') }}</button>
        <button type="button" class="btn primary" [disabled]="!canApply()" (click)="submit()">
          <app-icon name="check" [size]="15" />{{ i18n.t(pending() ? 'pricing.import.applying' : 'pricing.import.apply') }}
        </button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    .file {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--sp-2);
      margin: var(--sp-3) 0;
      cursor: pointer;
    }
    .file-name {
      color: var(--ink-2);
      font-size: var(--fs-sm);
    }
    .summary,
    .ok {
      margin: var(--sp-2) 0;
    }
    .ok {
      color: var(--ok);
      font-weight: 600;
    }
    .errors {
      max-height: 40vh;
      overflow: auto;
      margin-top: var(--sp-2);
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--fs-sm);
    }
    th,
    td {
      padding: 4px var(--sp-2);
      border-bottom: 1px solid var(--line);
      text-align: start;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ImportPricesDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly listCode = input('');
  readonly pending = input(false);
  /** The dry run's or the import's answer; the page clears it when the dialog opens. */
  readonly result = input<ImportPriceLinesResult | null>(null);
  readonly problem = input<ProblemDetails | null>(null);
  /** The parsed rows, to be checked (dry run). */
  readonly check = output<readonly ImportPriceRowRequest[]>();
  /** The same rows, to be applied. */
  readonly apply = output<readonly ImportPriceRowRequest[]>();

  protected readonly fileName = signal<string | null>(null);
  protected readonly parsing = signal(false);
  protected readonly parsed = signal<ParsedPriceSheet | null>(null);
  protected readonly parseFault = signal<PriceSheetFault | null>(null);

  protected readonly canApply = computed(() => {
    const outcome = this.result();
    return this.parsed() !== null && outcome !== null && outcome.errors.length === 0 && outcome.accepted > 0 && !outcome.applied && !this.pending();
  });

  constructor() {
    effect(() => {
      if (!this.open()) {
        untracked(() => this.reset());
      }
    });
  }

  protected async onFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }

    await this.read(file);
  }

  /** Parses the workbook and asks the page for a dry run; exposed for the spec. */
  async read(file: File): Promise<void> {
    this.fileName.set(file.name);
    this.parsed.set(null);
    this.parseFault.set(null);
    this.parsing.set(true);
    try {
      const sheet = await parsePriceWorkbook(await file.arrayBuffer());
      this.parsed.set(sheet);
      if (sheet.rows.length === 0) {
        this.parseFault.set('noRows');
        return;
      }

      this.check.emit(sheet.rows);
    } catch (error: unknown) {
      this.parseFault.set(error instanceof PriceSheetError ? error.fault : 'unreadable');
    } finally {
      this.parsing.set(false);
    }
  }

  protected submit(): void {
    const sheet = this.parsed();
    if (sheet !== null && this.canApply()) {
      this.apply.emit(sheet.rows);
    }
  }

  protected rowErrorText(code: string): string {
    return problemMessage(this.i18n, 'pricing', { code });
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'pricing', problem);
  }

  private reset(): void {
    this.fileName.set(null);
    this.parsed.set(null);
    this.parseFault.set(null);
    this.parsing.set(false);
  }
}
