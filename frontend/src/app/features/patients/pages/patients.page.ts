import { ChangeDetectionStrategy, Component, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

import type { PageRequest } from '../../../core/api/paging';
import {
  PATIENT_GENDERS,
  PATIENT_STATUSES,
  PATIENTS_PERMISSIONS,
  type PatientListItem,
  type RegisterPatientRequest,
  type SimilarPatient,
} from '../../../core/api/patients.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { formatDateTime } from '../../../shared/patient/patient-demographics';
import { saveFile } from '../data/save-file';
import { PatientsFacade } from '../state/patients.facade';
import { ageText, localDigits } from '../../../shared/patient/patient-demographics';
import { PatientFormComponent, type TypedIdentity } from '../../../shared/patient/patient-form.component';

/**
 * Patients register (SRS CP-11): search by number, name, mobile or national id; filters gender, status and (with the
 * confidential right) VIP; CSV export; the create/edit drawer with the similar-patient warning (BR-01, REG-002). A row
 * opens the patient file page.
 */
@Component({
  selector: 'app-patients-page',
  imports: [
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    DrawerComponent,
    IconComponent,
    PatientFormComponent,
  ],
  templateUrl: './patients.page.html',
  styleUrl: './patients.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PatientsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(PatientsFacade);
  private readonly permissionsService = inject(PermissionsService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly form = viewChild(PatientFormComponent);

  protected readonly permissions = PATIENTS_PERMISSIONS;
  protected readonly genders = PATIENT_GENDERS;
  protected readonly statuses = PATIENT_STATUSES;
  protected readonly drawerOpen = signal(false);

  protected readonly rows = computed<readonly PatientListItem[]>(() => (this.facade.list.hasValue() ? this.facade.list.value().items : []));
  protected readonly totalCount = computed(() => (this.facade.list.hasValue() ? this.facade.list.value().totalCount : 0));
  protected readonly editing = computed(() => (this.facade.editingId() !== null && this.facade.editing.hasValue() ? this.facade.editing.value() : null));
  protected readonly saving = computed(() => this.facade.register.pending() || this.facade.update.pending());
  protected readonly saveProblem = computed(() => (this.facade.editingId() === null ? this.facade.register.problem() : this.facade.update.problem()));

  protected readonly columns = computed<readonly TableColumn<PatientListItem>[]>(() => {
    const columns: TableColumn<PatientListItem>[] = [
      { key: 'number', header: 'patients.column.number', mono: true, width: '150px', sortable: true },
      { key: 'name', header: 'patients.column.name', sortable: true, value: (row) => (row.identityMasked === true ? this.i18n.t('patients.vipMasked') : row.fullName) },
      { key: 'gender', header: 'patients.column.gender', width: '90px', value: (row) => this.i18n.t(`patients.gender.${row.gender}`) },
      { key: 'age', header: 'patients.column.age', width: '110px', value: (row) => this.ageText(row) },
      { key: 'mobile', header: 'patients.column.mobile', mono: true, width: '140px' },
      { key: 'nationalId', header: 'patients.column.nationalId', mono: true, secondary: true },
      { key: 'condition', header: 'patients.column.condition', secondary: true, value: (row) => this.conditionName(row.patientConditionId) },
      // CP-11 "visit count, last visit" (UAT A-09), counted by Registration
      // in the language's digits (round 2 R2-02); the column sorts by the number
      { key: 'visitCount', header: 'patients.column.visits', width: '80px', align: 'end', value: (row) => localDigits(row.visitCount ?? 0, this.i18n.lang()), sortValue: (row) => row.visitCount ?? 0 },
      { key: 'lastVisitAtUtc', header: 'patients.column.lastVisit', width: '150px', secondary: true, value: (row) => (row.lastVisitAtUtc ? formatDateTime(row.lastVisitAtUtc, this.i18n.lang()) : '—') },
    ];
    if (this.permissionsService.can(PATIENTS_PERMISSIONS.viewConfidential)) {
      columns.push({ key: 'isVip', header: 'patients.column.vip', width: '70px', align: 'center' });
    }

    columns.push({ key: 'status', header: 'patients.column.status', width: '110px' });
    return columns;
  });

  constructor() {
    void this.i18n.load('patients');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14, A-01)
  }

  protected open(row: PatientListItem): void {
    void this.router.navigate([row.id], { relativeTo: this.route });
  }

  protected add(): void {
    this.facade.startEditing(null);
    this.drawerOpen.set(true);
  }

  protected edit(row: PatientListItem): void {
    this.facade.startEditing(row.id);
    this.drawerOpen.set(true);
  }

  protected closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  protected submit(): void {
    this.form()?.submit();
  }

  protected async onSave(request: RegisterPatientRequest): Promise<void> {
    const editing = this.editing();
    if (editing === null) {
      const created = await this.facade.register.execute(request);
      if (created !== null) {
        this.drawerOpen.set(false);
        this.toast.success(this.i18n.tf('patients.created', { number: created.number }));
        void this.router.navigate([created.id], { relativeTo: this.route });
      }

      return;
    }

    const updated = await this.facade.update.execute({ id: editing.id, request: { ...request, rowVersion: editing.rowVersion } });
    if (updated !== null) {
      this.drawerOpen.set(false);
      this.toast.success(this.i18n.tf('patients.saved', { number: editing.number }));
    }
  }

  protected onTyped(identity: TypedIdentity): void {
    this.facade.onTyped(identity);
  }

  /** "Use this file": the clerk goes to the existing file instead of creating a duplicate. */
  protected useExisting(candidate: SimilarPatient): void {
    this.drawerOpen.set(false);
    void this.router.navigate([candidate.id], { relativeTo: this.route });
  }

  protected async exportCsv(): Promise<void> {
    const file = await this.facade.exportCsv.execute(undefined);
    if (file !== null) {
      saveFile(file);
      this.toast.success(this.i18n.t('patients.exported'));
    } else {
      this.toast.error(problemMessage(this.i18n, 'patients', this.facade.exportCsv.problem()));
    }
  }

  protected onGender(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setGender(this.genders.find((gender) => gender === value) ?? null);
  }

  protected onStatus(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setStatus(this.statuses.find((status) => status === value) ?? null);
  }

  protected onVip(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setVip(value === 'yes' ? true : value === 'no' ? false : null);
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected ageText(row: PatientListItem): string {
    const text = ageText(row.age, this.i18n.lang(), (key, params) => this.i18n.tf(key, params));
    return row.dateOfBirthIsEstimated ? `≈ ${text}` : text;
  }

  protected conditionName(id: string | null): string | null {
    if (id === null) {
      return null;
    }

    const condition = (this.facade.conditions.value() ?? []).find((item) => item.id === id);
    return condition ? (this.i18n.lang() === 'ar' ? condition.nameAr : condition.nameEn) : null;
  }

  protected statusLabel(status: string): string {
    return this.i18n.t(`patients.status.${status}`);
  }
}
