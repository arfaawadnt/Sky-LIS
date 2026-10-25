import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';

import { LOCALITY_IMPORT_MAX_BYTES, type LocalityImportResult, PLATFORM_PERMISSIONS } from '../../../core/api/platform.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { CanDirective } from '../../../shared/ui/can.directive';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { LocationsFacade } from '../state/locations.facade';

/** At most this many refused lines are listed; the summary counts them all. */
const SHOWN_ERRORS = 20;

/**
 * SA-11 "Import CSV" (LIS-CR-008 C-04, decision D-2) inside the Locations drawer, after the price-lines import of LIS-CR-003
 * C21: choosing a file runs a dry run and shows what would be created, what exists already and the refused lines; *Import*
 * then sends the same file for real (enabled only when nothing is refused: the server is all-or-nothing). The expected
 * header is `city_en,city_ar,district_en,district_ar,area_en,area_ar` (district and area optional). `tenancy.manage`.
 */
@Component({
  selector: 'app-platform-locality-import',
  imports: [CanDirective, IconComponent],
  template: `
    <div class="import-bar" *appCan="permissions.tenancyManage">
      <label class="btn sm" [class.disabled]="facade.importFile.pending()">
        <app-icon name="layers" [size]="14" />{{ i18n.t('platform.locations.import.action') }}
        <input class="visually-hidden" type="file" accept=".csv,text/csv" [disabled]="facade.importFile.pending()" (change)="onFile($event)" />
      </label>
      <span class="muted small">{{ i18n.t('platform.locations.import.hint') }}</span>
    </div>

    @if (tooLarge()) {
      <p class="problem" role="alert">{{ i18n.t('platform.locations.import.tooLarge') }}</p>
    }
    @if (facade.importFile.problem(); as problem) {
      <p class="problem" role="alert">{{ problemText(problem) }}</p>
    }

    @if (preview(); as result) {
      <section class="summary" role="status" [attr.aria-label]="i18n.t('platform.locations.import.summary')">
        <p class="strong">{{ i18n.tf('platform.locations.import.read', { file: fileName(), rows: result.rows }) }}</p>
        <table class="counts">
          <thead>
            <tr>
              <th scope="col"></th>
              <th scope="col">{{ i18n.t('platform.locations.import.created') }}</th>
              <th scope="col">{{ i18n.t('platform.locations.import.existing') }}</th>
              <th scope="col">{{ i18n.t('platform.locations.import.refused') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (level of levels(result); track level.key) {
              <tr>
                <th scope="row">{{ i18n.t('platform.locations.' + level.key) }}</th>
                <td>{{ level.counts.created }}</td>
                <td>{{ level.counts.existing }}</td>
                <td [class.bad]="level.counts.refused > 0">{{ level.counts.refused }}</td>
              </tr>
            }
          </tbody>
        </table>
        @if (result.errors.length > 0) {
          <p class="problem">{{ i18n.tf('platform.locations.import.blocked', { count: result.errors.length }) }}</p>
          <ul class="errors">
            @for (error of shownErrors(); track error.line + error.level) {
              <li>{{ i18n.tf('platform.locations.import.errorLine', { line: error.line, level: i18n.t('platform.locations.import.level.' + error.level) }) }} {{ errorText(error.code, error.message) }}</li>
            }
          </ul>
        }
        <div class="actions">
          <button type="button" class="btn ghost sm" (click)="cancel()">{{ i18n.t('platform.locations.cancel') }}</button>
          <button type="button" class="btn primary sm" [disabled]="result.errors.length > 0 || facade.importFile.pending()" (click)="apply()">
            {{ i18n.t('platform.locations.import.apply') }}
          </button>
        </div>
      </section>
    }
  `,
  styles: `
    .import-bar { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-block-end: 12px; }
    .import-bar label { cursor: pointer; }
    .import-bar label.disabled { opacity: 0.6; pointer-events: none; }
    .summary { border: 1px solid var(--line); border-radius: var(--r); padding: 12px; margin-block-end: 16px; display: grid; gap: 8px; }
    .strong { font-weight: 600; margin: 0; }
    .counts { border-collapse: collapse; width: 100%; max-width: 420px; }
    .counts th, .counts td { padding: 4px 8px; text-align: end; }
    .counts th[scope='row'] { text-align: start; }
    .counts .bad { color: var(--bad); font-weight: 600; }
    .errors { margin: 0; padding-inline-start: 18px; max-height: 160px; overflow: auto; font-size: 0.875rem; }
    .actions { display: flex; justify-content: flex-end; gap: 8px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocalityImportComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(LocationsFacade);
  private readonly toast = inject(ToastService);

  protected readonly permissions = PLATFORM_PERMISSIONS;
  private readonly file = signal<File | null>(null);
  protected readonly preview = signal<LocalityImportResult | null>(null);
  protected readonly tooLarge = signal(false);
  protected readonly fileName = computed(() => this.file()?.name ?? '');
  protected readonly shownErrors = computed(() => this.preview()?.errors.slice(0, SHOWN_ERRORS) ?? []);

  protected levels(result: LocalityImportResult): readonly { key: 'cities' | 'districts' | 'areas'; counts: LocalityImportResult['cities'] }[] {
    return [
      { key: 'cities', counts: result.cities },
      { key: 'districts', counts: result.districts },
      { key: 'areas', counts: result.areas },
    ];
  }

  /** A file chosen: checked at once (dry run); the input is cleared so the same file can be chosen again. */
  protected async onFile(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.item(0) ?? null;
    input.value = '';
    this.preview.set(null);
    this.facade.importFile.reset();
    if (file === null) {
      return;
    }

    this.tooLarge.set(file.size > LOCALITY_IMPORT_MAX_BYTES);
    if (this.tooLarge()) {
      this.file.set(null);
      return;
    }

    this.file.set(file);
    this.preview.set(await this.facade.importFile.execute({ file, dryRun: true }));
  }

  protected async apply(): Promise<void> {
    const file = this.file();
    if (file === null) {
      return;
    }

    const result = await this.facade.importFile.execute({ file, dryRun: false });
    if (result === null) {
      return;
    }

    if (result.applied) {
      this.toast.success(this.i18n.tf('platform.locations.import.done', { cities: result.cities.created, districts: result.districts.created, areas: result.areas.created }));
      this.cancel();
    } else {
      this.preview.set(result);   // the file changed under us: show what the server refused
    }
  }

  protected cancel(): void {
    this.file.set(null);
    this.preview.set(null);
    this.tooLarge.set(false);
    this.facade.importFile.reset();
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'platform.locations', problem);
  }

  /** The translated reason of a refused line, else the server's own text. */
  protected errorText(code: string, message: string): string {
    const key = `platform.locations.error.${code}`;
    return this.i18n.has(key) ? this.i18n.t(key) : message;
  }
}
