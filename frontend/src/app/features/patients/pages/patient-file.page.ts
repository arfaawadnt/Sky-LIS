import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';

import {
  PATIENT_DOCUMENT_RULES,
  PATIENTS_PERMISSIONS,
  type MergePatientsRequest,
  type PatientDocumentItem,
  type PatientVisitItem,
  type RegisterPatientRequest,
} from '../../../core/api/patients.contracts';
import { ProblemError, type ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { type Breadcrumb, PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { type TabItem, TabsComponent } from '../../../shared/ui/tabs/tabs.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { openFile, saveFile } from '../data/save-file';
import { PatientFileFacade } from '../state/patient-file.facade';
import { MergeDialogComponent } from '../ui/merge-dialog.component';
import { ageParts, fileSize, formatDateTime } from '../ui/patient-demographics';
import { PatientFormComponent } from '../ui/patient-form.component';

/**
 * One patient file (SRS CP-11): the demographics, the documents (attach, open, download, deactivate with a reason —
 * never delete, BR-15; documents of files merged into this one are listed with their original number), the visit
 * history (Registration module) and the merge of a duplicate into this file (patients.manage). A Merged file is shown
 * read-only with a link to its survivor.
 */
@Component({
  selector: 'app-patient-file-page',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    TabsComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    BadgeComponent,
    DrawerComponent,
    EmptyStateComponent,
    FormFieldComponent,
    IconComponent,
    PatientFormComponent,
    MergeDialogComponent,
  ],
  templateUrl: './patient-file.page.html',
  styleUrl: './patient-file.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PatientFilePage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(PatientFileFacade);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly form = viewChild(PatientFormComponent);
  private readonly fileInput = viewChild<ElementRef<HTMLInputElement>>('fileInput');

  /** Route parameter `:id` (component input binding). */
  readonly id = input.required<string>();

  protected readonly permissions = PATIENTS_PERMISSIONS;
  protected readonly documentRules = PATIENT_DOCUMENT_RULES;
  protected readonly activeTab = signal<string | null>('file');
  protected readonly editOpen = signal(false);
  protected readonly mergeOpen = signal(false);
  protected readonly chosenFile = signal<File | null>(null);
  protected readonly fileProblem = signal<string | null>(null);
  protected readonly note = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(500)] });

  protected readonly patient = computed(() => (this.facade.detail.hasValue() ? this.facade.detail.value() : null));
  protected readonly isActive = computed(() => this.patient()?.status === 'Active');
  protected readonly documents = computed<readonly PatientDocumentItem[]>(() => (this.facade.documents.hasValue() ? this.facade.documents.value() : []));
  protected readonly visits = computed<readonly PatientVisitItem[]>(() => (this.facade.visits.hasValue() ? this.facade.visits.value() : []));

  protected readonly breadcrumb = computed<readonly Breadcrumb[]>(() => [
    { label: this.i18n.t('patients.title'), path: '/patients' },
    { label: this.patient()?.number ?? '…', path: null },
  ]);

  protected readonly tabs = computed<readonly TabItem[]>(() => [
    { id: 'file', label: this.i18n.t('patients.tab.file') },
    { id: 'documents', label: this.i18n.t('patients.tab.documents'), badge: this.facade.documents.hasValue() ? this.documents().filter((document) => document.isActive).length : null },
    { id: 'visits', label: this.i18n.t('patients.tab.visits') },
  ]);

  protected readonly documentColumns: readonly TableColumn<PatientDocumentItem>[] = [
    { key: 'fileName', header: 'patients.document.fileName' },
    { key: 'type', header: 'patients.document.type', width: '90px', value: (row) => this.typeLabel(row.contentType) },
    { key: 'size', header: 'patients.document.size', width: '90px', align: 'end', value: (row) => fileSize(row.sizeBytes) },
    { key: 'note', header: 'patients.document.note', secondary: true },
    { key: 'uploaded', header: 'patients.document.uploaded', secondary: true },
    { key: 'patientNumber', header: 'patients.document.file', mono: true, secondary: true },
    { key: 'isActive', header: 'patients.document.status', width: '110px' },
  ];

  protected readonly visitColumns: readonly TableColumn<PatientVisitItem>[] = [
    { key: 'accession', header: 'patients.visit.accession', mono: true, width: '160px' },
    { key: 'registeredAtUtc', header: 'patients.visit.date', value: (row) => (row.registeredAtUtc ? formatDateTime(row.registeredAtUtc, this.i18n.lang()) : null) },
    { key: 'branchName', header: 'patients.visit.branch', secondary: true },
    { key: 'servicesCount', header: 'patients.visit.services', align: 'end', width: '100px' },
    { key: 'status', header: 'patients.visit.status', width: '120px' },
  ];

  constructor() {
    void this.i18n.load('patients');

    effect(() => {
      const id = this.id();
      untracked(() => this.facade.load(id));
    });

    effect(() => {
      if (this.activeTab() === 'visits') {
        untracked(() => this.facade.visitsRequested.set(true));
      }
    });
  }

  protected openEdit(): void {
    this.facade.update.reset();
    this.editOpen.set(true);
  }

  protected submitEdit(): void {
    this.form()?.submit();
  }

  protected async onSave(request: RegisterPatientRequest): Promise<void> {
    const patient = this.patient();
    if (patient === null) {
      return;
    }

    const saved = await this.facade.update.execute({ ...request, rowVersion: patient.rowVersion });
    if (saved !== null) {
      this.editOpen.set(false);
      this.toast.success(this.i18n.tf('patients.saved', { number: patient.number }));
    }
  }

  protected openMerge(): void {
    this.facade.merge.reset();
    this.facade.mergeQuery.set('');
    this.mergeOpen.set(true);
  }

  protected async onMerge(request: MergePatientsRequest): Promise<void> {
    const patient = this.patient();
    const duplicate = this.facade.mergeCandidates().find((candidate) => candidate.id === request.mergedPatientId);
    if (patient === null || duplicate === undefined) {
      return;
    }

    const confirmed = await this.confirm.confirm({
      title: this.i18n.t('patients.merge.confirmTitle'),
      message: this.i18n.tf('patients.merge.confirmMessage', { duplicate: duplicate.number, survivor: patient.number }),
      danger: true,
      confirmLabel: this.i18n.t('patients.merge.action'),
    });
    if (!confirmed) {
      return;
    }

    if ((await this.facade.merge.execute(request)) !== null) {
      this.mergeOpen.set(false);
      this.toast.success(this.i18n.tf('patients.merge.done', { duplicate: duplicate.number, survivor: patient.number }));
    }
  }

  protected onFileChosen(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.item(0) ?? null;
    this.fileProblem.set(null);
    this.chosenFile.set(file);
    if (file === null) {
      return;
    }

    const extension = file.name.includes('.') ? (file.name.split('.').pop() ?? '').toLowerCase() : '';
    if (!(this.documentRules.extensions as readonly string[]).includes(extension)) {
      this.fileProblem.set(this.i18n.t('patients.error.PAT-042'));
    } else if (file.size === 0) {
      this.fileProblem.set(this.i18n.t('patients.error.PAT-040'));
    } else if (file.size > this.documentRules.maxBytes) {
      this.fileProblem.set(this.i18n.t('patients.error.PAT-041'));
    }
  }

  protected async upload(): Promise<void> {
    const file = this.chosenFile();
    if (file === null || this.fileProblem() !== null || this.note.invalid || this.facade.upload.pending()) {
      return;
    }

    const note = this.note.value.trim();
    if ((await this.facade.upload.execute({ file, note: note === '' ? null : note })) !== null) {
      this.toast.success(this.i18n.tf('patients.document.uploadDone', { name: file.name }));
      this.chosenFile.set(null);
      this.note.reset();
      const element = this.fileInput()?.nativeElement;
      if (element) {
        element.value = '';
      }
    }
  }

  protected async view(document: PatientDocumentItem): Promise<void> {
    const file = await this.facade.download.execute(document);
    if (file !== null) {
      openFile(file);
    } else {
      this.toast.error(this.downloadProblem());
    }
  }

  protected async save(document: PatientDocumentItem): Promise<void> {
    const file = await this.facade.download.execute(document);
    if (file !== null) {
      saveFile(file);
    } else {
      this.toast.error(this.downloadProblem());
    }
  }

  protected async deactivate(document: PatientDocumentItem): Promise<void> {
    const reason = await this.confirm.promptReason({
      title: this.i18n.t('patients.document.deactivate'),
      message: this.i18n.tf('patients.document.deactivatePrompt', { name: document.fileName }),
      danger: true,
      maxLength: 1000,
    });
    if (reason === null) {
      return;
    }

    if ((await this.facade.deactivate.execute({ document, reason })) !== null) {
      this.toast.success(this.i18n.tf('patients.document.deactivated', { name: document.fileName }));
    } else {
      this.toast.error(problemMessage(this.i18n, 'patients', this.facade.deactivate.problem()));
    }
  }

  protected onIncludeInactive(event: Event): void {
    this.facade.includeInactive.set((event.target as HTMLInputElement).checked);
  }

  protected ageText(age: { readonly years: number; readonly months: number; readonly days: number }): string {
    return ageParts(age)
      .map((part) => this.i18n.tf(`patients.age.${part.unit}`, { value: part.value }))
      .join(' ');
  }

  protected optionName(option: { readonly nameEn: string; readonly nameAr: string } | null | undefined): string | null {
    return option ? (this.i18n.lang() === 'ar' ? option.nameAr : option.nameEn) : null;
  }

  protected countryName(code: string | null): string | null {
    if (code === null) {
      return null;
    }

    return this.optionName((this.facade.countries.value() ?? []).find((country) => country.code === code)) ?? code;
  }

  protected governorateName(id: string | null): string | null {
    return id === null ? null : this.optionName((this.facade.governorates.value() ?? []).find((governorate) => governorate.id === id));
  }

  protected typeLabel(contentType: string): string {
    return contentType === 'application/pdf' ? 'PDF' : contentType === 'image/png' ? 'PNG' : contentType === 'image/jpeg' ? 'JPEG' : contentType;
  }

  protected loadProblem(): string {
    const error = this.facade.detail.error();
    return error instanceof ProblemError && error.code === 'PAT-404' ? this.i18n.t('patients.error.PAT-404') : this.i18n.t('patients.loadError');
  }

  protected readonly inactiveRow = (row: PatientDocumentItem): string | null => (row.isActive ? null : 'inactive');

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'patients', problem);
  }

  protected when(iso: string | null): string {
    return iso === null ? '—' : formatDateTime(iso, this.i18n.lang());
  }

  private downloadProblem(): string {
    const problem = this.facade.download.problem();
    return problem?.status === 403 ? this.i18n.t('patients.error.PAT-049') : problem?.status === 404 ? this.i18n.t('patients.error.PAT-048') : problemMessage(this.i18n, 'patients', problem);
  }
}
