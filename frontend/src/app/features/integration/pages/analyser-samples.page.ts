import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import { INTEGRATION_PERMISSIONS, type SampleCodeItem } from '../../../core/api/integration.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { AnalyserTestsFacade } from '../state/analyser-tests.facade';
import { integrationTone } from '../ui/integration-format';

/**
 * Analyser samples (SRS CP-60): per analyser, the instrument's code for each catalogue sample type (some instruments
 * refuse an order without it, LDM manual §13.6). Add with the line form (Enter adds), edit a code in place, (de)activate.
 */
@Component({
  selector: 'app-analyser-samples-page',
  imports: [
    ReactiveFormsModule,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    BadgeComponent,
    CanDirective,
    IconComponent,
    EmptyStateComponent,
  ],
  template: `
    <app-page-header [title]="i18n.t('integration.samples.title')" [subtitle]="i18n.t('integration.samples.subtitle')" />

    <app-section-card [padded]="false">
      <app-toolbar>
        <label toolbarFilters class="picker">
          <span>{{ i18n.t('integration.field.analyser') }}</span>
          <select class="inp" (change)="onAnalyser($event)" data-testid="analyser-picker">
            <option value="">{{ i18n.t('integration.chooseAnalyser') }}</option>
            @for (analyser of analysers(); track analyser.id) {
              <option [value]="analyser.id" [selected]="analyser.id === facade.analyserId()">{{ analyser.code }} · {{ name(analyser) }}</option>
            }
          </select>
        </label>
      </app-toolbar>

      @if (!facade.analyserId()) {
        <app-empty-state [title]="i18n.t('integration.chooseAnalyser')" [message]="i18n.t('integration.samples.chooseHint')" icon="barcode" />
      } @else {
        <form *appCan="permissions.edit" class="add-line" [formGroup]="form" (ngSubmit)="add()" novalidate>
          <select class="inp" formControlName="sampleTypeId" [attr.aria-label]="i18n.t('integration.samples.sampleType')" data-testid="sample-type">
            <option value="">{{ i18n.t('integration.samples.chooseType') }}</option>
            @for (type of sampleTypes(); track type.id) {
              <option [value]="type.id">{{ type.code }} · {{ name(type) }}</option>
            }
          </select>
          <input class="inp mono" formControlName="code" maxlength="16" [placeholder]="i18n.t('integration.samples.code')" [attr.aria-label]="i18n.t('integration.samples.code')" (keydown.enter)="add(); $event.preventDefault()" data-testid="sample-code" />
          <button type="submit" class="btn primary" [disabled]="facade.addSampleCode.pending()"><app-icon name="plus" [size]="15" />{{ i18n.t('integration.samples.add') }}</button>
        </form>
        @for (problem of [facade.addSampleCode.problem(), facade.changeSampleCode.problem(), facade.setSampleCodeActive.problem()]; track $index) {
          @if (problem) {
            <p class="problem list-problem" role="alert">{{ problemText(problem.code) }}</p>
          }
        }

        <app-data-table [columns]="columns" [rows]="rows()" [loading]="facade.sampleCodes.isLoading()" [interactive]="false" [emptyTitle]="i18n.t('integration.samples.empty')" [caption]="i18n.t('integration.samples.title')">
          <ng-template appCell="code" [appCellOf]="rows()" let-row>
            @if (editingId() === row.id) {
              <input class="inp mono" [formControl]="editCode" maxlength="16" (keydown.enter)="saveEdit(row)" (keydown.escape)="editingId.set(null)" [attr.aria-label]="i18n.t('integration.samples.code')" />
            } @else {
              <span class="mono">{{ row.code }}</span>
            }
          </ng-template>
          <ng-template appCell="status" [appCellOf]="rows()" let-row>
            <app-badge [tone]="tone(row.isActive ? 'Active' : 'Inactive')">{{ i18n.t(row.isActive ? 'integration.active' : 'integration.inactive') }}</app-badge>
          </ng-template>
          <ng-template appRowActions [appRowActionsOf]="rows()" let-row>
            @if (editingId() === row.id) {
              <button type="button" class="btn sm primary" (click)="saveEdit(row)">{{ i18n.t('integration.save') }}</button>
              <button type="button" class="btn sm" (click)="editingId.set(null)">{{ i18n.t('integration.cancel') }}</button>
            } @else {
              <button type="button" class="icon-btn" *appCan="permissions.edit" (click)="startEdit(row)" [attr.aria-label]="i18n.t('integration.edit')"><app-icon name="edit" [size]="15" /></button>
              <button type="button" class="icon-btn" *appCan="permissions.manage" (click)="toggleActive(row)" [attr.aria-label]="i18n.t(row.isActive ? 'integration.samples.deactivate' : 'integration.samples.activate')"><app-icon name="power" [size]="15" /></button>
            }
          </ng-template>
        </app-data-table>
      }
    </app-section-card>
  `,
  styles: `
    .picker { display: inline-flex; align-items: center; gap: var(--sp-2); font-size: var(--fs-sm); color: var(--ink-2); }
    .picker select { width: auto; min-width: 240px; }
    .add-line { display: flex; flex-wrap: wrap; gap: var(--sp-2); padding: var(--sp-3) var(--sp-4); align-items: center; }
    .add-line select { width: auto; min-width: 220px; }
    .add-line input { width: 160px; }
    .list-problem { margin: var(--sp-2) var(--sp-4) 0; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AnalyserSamplesPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(AnalyserTestsFacade);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);

  protected readonly permissions = INTEGRATION_PERMISSIONS;
  protected readonly tone = integrationTone;
  private readonly queryParams = toSignal(this.route.queryParamMap);

  protected readonly columns: readonly TableColumn<SampleCodeItem>[] = [
    { key: 'sampleType', header: 'integration.samples.sampleType', value: (row) => `${row.sampleTypeCode ?? ''} ${localizedPick({ en: row.sampleTypeNameEn ?? '', ar: row.sampleTypeNameAr ?? '' }, this.i18n.lang())}`.trim() },
    { key: 'code', header: 'integration.samples.code', width: '200px' },
    { key: 'status', header: 'integration.field.status', width: '120px' },
  ];

  protected readonly analysers = computed(() => (this.facade.analysers.hasValue() ? this.facade.analysers.value().items : []));
  protected readonly rows = computed<readonly SampleCodeItem[]>(() => (this.facade.sampleCodes.hasValue() ? this.facade.sampleCodes.value() : []));
  protected readonly sampleTypes = computed(() => (this.facade.sampleTypes.hasValue() ? this.facade.sampleTypes.value() : []));

  protected readonly form = new FormGroup({
    sampleTypeId: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    code: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(16)] }),
  });

  protected readonly editingId = signal<string | null>(null);
  protected readonly editCode = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(16)] });

  constructor() {
    void this.i18n.load('integration');
    effect(() => {
      const id = this.queryParams()?.get('analyserId') ?? null;
      if (id !== this.facade.analyserId()) {
        this.facade.selectAnalyser(id);
        this.editingId.set(null);
      }
    });
  }

  protected name(option: { nameEn: string; nameAr: string }): string {
    return localizedPick({ en: option.nameEn, ar: option.nameAr }, this.i18n.lang());
  }

  protected onAnalyser(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    void this.router.navigate([], { relativeTo: this.route, queryParams: { analyserId: value === '' ? null : value }, queryParamsHandling: 'merge' });
  }

  protected async add(): Promise<void> {
    if (!markAllTouched(this.form) || this.facade.addSampleCode.pending()) {
      return;
    }

    const { sampleTypeId, code } = this.form.getRawValue();
    if ((await this.facade.addSampleCode.execute({ sampleTypeId, code: code.trim() })) !== null) {
      this.form.reset();
      this.toast.success(this.i18n.t('integration.samples.added'));
    }
  }

  protected startEdit(row: SampleCodeItem): void {
    this.editCode.setValue(row.code);
    this.editingId.set(row.id);
  }

  protected async saveEdit(row: SampleCodeItem): Promise<void> {
    if (this.editCode.invalid) {
      this.editCode.markAsTouched();
      return;
    }

    if ((await this.facade.changeSampleCode.execute({ sampleCodeId: row.id, request: { sampleTypeId: row.sampleTypeId, code: this.editCode.value.trim() } })) !== null) {
      this.editingId.set(null);
      this.toast.success(this.i18n.t('integration.samples.saved'));
    }
  }

  protected async toggleActive(row: SampleCodeItem): Promise<void> {
    const key = row.isActive ? 'deactivate' : 'activate';
    const confirmed = await this.confirm.confirm({ title: this.i18n.t(`integration.samples.${key}`), message: this.i18n.tf(`integration.samples.confirm.${key}`, { code: row.code }), danger: row.isActive });
    if (confirmed && (await this.facade.setSampleCodeActive.execute({ sampleCodeId: row.id, active: !row.isActive })) !== null) {
      this.toast.success(this.i18n.t(row.isActive ? 'integration.deactivated' : 'integration.activated'));
    }
  }

  protected problemText(code: string): string {
    return problemMessage(this.i18n, 'integration', { code });
  }
}
