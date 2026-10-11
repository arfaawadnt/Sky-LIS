import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';

import { CATALOGUE_A_PERMISSIONS, SERVICE_TYPES, type ServiceSequenceChange, type ServiceSequenceRow, type ServiceType } from '../../../core/api/catalogue-a.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { csvFileName, downloadCsv, toCsv, yesNo } from '../data-a/csv-export';
import { ServiceSequenceFacade } from '../state-a/service-sequence.facade';
import { CatalogueExportButtonComponent } from '../ui-a/export-button.component';

/**
 * Service sequence (SRS CP-42): the global display and report order of services, edited inline and saved in one
 * request (Ctrl+S). Values are whole numbers above zero and unique across the laboratory (INV-CAT-004); duplicates
 * among the listed rows are flagged before saving and the server checks the whole catalogue (CAT-105).
 */
@Component({
  selector: 'app-service-sequence-page',
  imports: [
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    BadgeComponent,
    IconComponent,
    CanDirective,
    SaveShortcutDirective,
    CatalogueExportButtonComponent,
  ],
  templateUrl: './service-sequence.page.html',
  styleUrls: ['./catalogue-list.css', './service-sequence.page.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceSequencePage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(ServiceSequenceFacade);
  private readonly toast = inject(ToastService);
  private readonly access = inject(PermissionsService);

  protected readonly permissions = CATALOGUE_A_PERMISSIONS;

  /** Viewers (no edit right) see the sequence read-only (UAT B-12); the server decides regardless. */
  protected readonly canEdit = computed(() => this.access.can(CATALOGUE_A_PERMISSIONS.edit));
  protected readonly types = SERVICE_TYPES;

  /** Edited values by service id (raw input text, so an empty or invalid entry stays visible). */
  protected readonly edits = signal<Readonly<Record<string, string>>>({});

  protected readonly columns: readonly TableColumn<ServiceSequenceRow>[] = [
    { key: 'sequence', header: 'catalogue-a.sequence.column.sequence', width: '140px' },
    { key: 'code', header: 'catalogue-a.sequence.column.code', mono: true, width: '130px' },
    { key: 'name', header: 'catalogue-a.sequence.column.name', value: (row) => localizedPick({ en: row.nameEn, ar: row.nameAr }, this.i18n.lang()) },
    { key: 'type', header: 'catalogue-a.sequence.column.type', width: '130px' },
    { key: 'group', header: 'catalogue-a.sequence.column.group', secondary: true, value: (row) => localizedPick({ en: row.clinicalGroupNameEn ?? '', ar: row.clinicalGroupNameAr ?? '' }, this.i18n.lang()) },
    { key: 'status', header: 'catalogue-a.sequence.column.status', width: '110px' },
  ];

  protected readonly rows = computed<readonly ServiceSequenceRow[]>(() => (this.facade.rows.hasValue() ? this.facade.rows.value() : []));

  /** The rows whose edited value differs from the stored one. */
  protected readonly changes = computed<readonly ServiceSequenceChange[]>(() => {
    const edits = this.edits();
    return this.rows()
      .filter((row) => edits[row.id] !== undefined && edits[row.id] !== String(row.sequence))
      .map((row) => ({ serviceId: row.id, sequence: Number(edits[row.id]) }));
  });

  protected readonly invalidIds = computed(() => new Set(this.changes().filter((change) => !Number.isInteger(change.sequence) || change.sequence <= 0).map((change) => change.serviceId)));

  /** Sequences used twice among the listed rows after the edits. */
  protected readonly duplicateIds = computed(() => {
    const byValue = new Map<number, string[]>();
    for (const row of this.rows()) {
      const value = this.effective(row);
      byValue.set(value, [...(byValue.get(value) ?? []), row.id]);
    }

    return new Set([...byValue.values()].filter((ids) => ids.length > 1).flat());
  });

  protected readonly canSave = computed(() => this.changes().length > 0 && this.invalidIds().size === 0 && this.duplicateIds().size === 0 && !this.facade.save.pending());

  protected readonly exportCsv = (): Promise<void> => {
    const t = (key: string): string => this.i18n.t(`catalogue-a.sequence.column.${key}`);
    downloadCsv(
      csvFileName('service-sequence'),
      toCsv(this.rows(), [
        { header: t('sequence'), value: (row) => row.sequence },
        { header: t('code'), value: (row) => row.code },
        { header: this.i18n.t('catalogue-a.csv.nameEn'), value: (row) => row.nameEn },
        { header: this.i18n.t('catalogue-a.csv.nameAr'), value: (row) => row.nameAr },
        { header: t('type'), value: (row) => this.typeLabel(row.type) },
        { header: this.i18n.t('catalogue-a.csv.active'), value: (row) => yesNo((key) => this.i18n.t(key), row.isActive) },
      ]),
    );
    return Promise.resolve();
  };

  constructor() {
    void this.i18n.load('catalogue-a');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
  }

  protected typeLabel(type: ServiceType): string {
    return this.i18n.t(`catalogue-a.serviceType.${type}`);
  }

  protected value(row: ServiceSequenceRow): string {
    return this.edits()[row.id] ?? String(row.sequence);
  }

  protected changed(row: ServiceSequenceRow): boolean {
    const edited = this.edits()[row.id];
    return edited !== undefined && edited !== String(row.sequence);
  }

  protected onInput(row: ServiceSequenceRow, event: Event): void {
    if (!this.canEdit()) {
      return;
    }

    const text = (event.target as HTMLInputElement).value.trim();
    this.edits.update((current) => ({ ...current, [row.id]: text }));
  }

  protected onType(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setType(this.types.find((type) => type === value) ?? null);
  }

  protected discard(): void {
    this.edits.set({});
    this.facade.save.reset();
  }

  async save(): Promise<void> {
    if (!this.canSave()) {
      return;
    }

    const changes = this.changes();
    if ((await this.facade.save.execute(changes)) !== null) {
      this.edits.set({});
      this.toast.success(this.i18n.tf('catalogue-a.sequence.saved', { count: changes.length }));
    }
  }

  protected problemText(): string {
    return problemMessage(this.i18n, 'catalogue-a', this.facade.save.problem());
  }

  private effective(row: ServiceSequenceRow): number {
    const edited = this.edits()[row.id];
    return edited === undefined ? row.sequence : Number(edited);
  }
}
