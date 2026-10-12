import { ChangeDetectionStrategy, Component, computed, inject, resource, signal, viewChild } from '@angular/core';

import type { PageRequest } from '../../../core/api/paging';
import {
  CODE_IMPORT_MAX_BYTES,
  CODE_SYSTEM_NAMES,
  type CodeSystemImportResult,
  type CodeSystemItemView,
  type CodeSystemListItem,
  type CodeSystemName,
  type CodeSystemRequest,
  PLATFORM_API,
  PLATFORM_PERMISSIONS,
} from '../../../core/api/platform.contracts';
import { ProblemError } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { MasterDataPageComponent } from '../../../shared/ui/master-data-page/master-data-page.component';
import type { FormValue, MasterDataConfig, MasterDataFilter } from '../../../shared/ui/master-data-page/master-data.types';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { CsvExportService } from '../data/csv-export';
import { PlatformApi } from '../data/platform.api';
import { fileStamp, formatDateTime, formatNumber } from '../ui/format';
import { sortParams } from '../../../shared/forms/paging';

const NAME_LABELS: Readonly<Record<CodeSystemName, string>> = { Loinc: 'LOINC', Cpt: 'CPT', Icd10: 'ICD-10', Snomed: 'SNOMED CT' };

/**
 * SA-17 Code systems: LOINC, CPT, ICD-10 and SNOMED versions with their use. "Import codes" uploads a CSV (header
 * code,display; at most 20 MB; upsert by code); "View codes" searches the imported items.
 */
@Component({
  selector: 'app-platform-code-systems-page',
  imports: [MasterDataPageComponent, DialogComponent, DataTableComponent, PaginatorComponent, SearchBoxComponent, IconComponent],
  template: `
    <app-master-data-page [config]="config">
      <button pageActions type="button" class="btn ghost" (click)="exportCsv()"><app-icon name="printer" [size]="15" />{{ i18n.t('platform.exportCsv') }}</button>
    </app-master-data-page>

    <app-dialog [(open)]="importOpen" [title]="i18n.t('platform.codeSystems.import')" size="md">
      @if (importTarget(); as target) {
        <div class="stack">
          <p class="small">{{ i18n.tf('platform.codeSystems.importIntro', { name: label(target.name), version: target.version }) }}</p>
          <p class="info-strip small">{{ i18n.t('platform.codeSystems.importFormat') }}</p>
          <label class="field">
            <span>{{ i18n.t('platform.codeSystems.file') }}</span>
            <input type="file" accept=".csv,text/csv,text/plain" (change)="onFile($event)" />
          </label>
          @if (importError(); as message) {
            <p class="problem" role="alert">{{ message }}</p>
          }
          @if (importResult(); as result) {
            <p class="info-strip small" role="status">{{ i18n.tf('platform.codeSystems.importDone', { inserted: result.inserted, updated: result.updated, unchanged: result.unchanged, total: result.total }) }}</p>
          }
        </div>
      }
      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="importOpen.set(false)">{{ i18n.t('platform.close') }}</button>
        <button type="button" class="btn primary" [disabled]="file() === null || importing()" (click)="runImport()">
          {{ importing() ? i18n.t('platform.codeSystems.importing') : i18n.t('platform.codeSystems.import') }}
        </button>
      </ng-container>
    </app-dialog>

    <app-dialog [(open)]="itemsOpen" [title]="itemsTitle()" size="lg">
      <div class="stack">
        <app-search-box [placeholder]="i18n.t('platform.codeSystems.searchCodes')" (searchChange)="onItemsSearch($event)" />
        <app-data-table [columns]="itemColumns" [rows]="itemRows()" [rowId]="itemId" [loading]="items.isLoading()" [interactive]="false" [compact]="true"
                        [emptyTitle]="i18n.t('platform.codeSystems.noCodes')" [caption]="itemsTitle()" />
        <app-paginator [page]="itemsPage().page" [pageSize]="itemsPage().pageSize" [totalCount]="items.value()?.totalCount ?? 0" (pageChange)="itemsPage.set($event)" />
      </div>
      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="itemsOpen.set(false)">{{ i18n.t('platform.close') }}</button>
      </ng-container>
    </app-dialog>
  `,
  styleUrl: './platform-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CodeSystemsPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PlatformApi);
  private readonly toast = inject(ToastService);
  private readonly csv = inject(CsvExportService);

  /** The filter of the list on screen: the export writes exactly these rows (UAT R2-02, as C-04 elsewhere). */
  private filter: MasterDataFilter = { search: '', active: true };

  private readonly page = viewChild.required(MasterDataPageComponent);

  protected readonly config: MasterDataConfig<CodeSystemListItem, CodeSystemRequest> = {
    i18nPrefix: 'platform.codeSystems',
    // B-03: the server sorts the whole library before paging (PlatformAdminSortKeys.CodeSystems; the name by its stored code)
    sortMode: 'server',
    columns: [
      { key: 'name', header: 'platform.codeSystems.column.name', sortable: true, value: (row) => this.label(row.name) },
      { key: 'version', header: 'platform.codeSystems.column.version', mono: true, sortable: true },
      { key: 'itemCount', header: 'platform.codeSystems.column.items', align: 'end', value: (row) => formatNumber(row.itemCount, this.i18n.lang()) },
      { key: 'lastImportedAtUtc', header: 'platform.codeSystems.column.lastImport', value: (row) => formatDateTime(row.lastImportedAtUtc, this.i18n.lang()), secondary: true },
      { key: 'use', header: 'platform.codeSystems.column.use', secondary: true },
    ],
    fields: [
      { name: 'name', kind: 'select', required: true, immutable: true, defaultValue: 'Loinc', options: CODE_SYSTEM_NAMES.map((value) => ({ value, label: NAME_LABELS[value] })) },
      { name: 'version', kind: 'text', required: true, maxLength: 40, hint: 'platform.codeSystems.hint.version' },
      { name: 'use', kind: 'textarea', maxLength: 300, hint: 'platform.codeSystems.hint.use' },
    ],
    adapter: {
      list: (filter, page, signal) => {
        this.filter = filter;
        return this.api.listCodeSystems({ ...sortParams(filter.sort), search: filter.search || null, isActive: filter.active, page: page.page, pageSize: page.pageSize }, signal);
      },
      create: (dto) => this.api.saveCodeSystem(null, dto),
      update: (id, dto) => this.api.saveCodeSystem(id, dto),
      setActive: (id, active) => this.api.setLibraryActive(PLATFORM_API.codeSystems, id, active),
    },
    permissions: { create: PLATFORM_PERMISSIONS.integrationManage, update: PLATFORM_PERMISSIONS.integrationManage, activate: PLATFORM_PERMISSIONS.integrationManage },
    toDto: (value: FormValue) => ({
      name: (value['name'] ?? 'Loinc') as CodeSystemName,
      version: String(value['version'] ?? ''),
      use: String(value['use'] ?? '').trim() || null,
    }),
    rowActions: [
      { id: 'import', label: 'platform.codeSystems.import', icon: 'database', permission: PLATFORM_PERMISSIONS.integrationManage, run: (row) => this.openImport(row) },
      { id: 'codes', label: 'platform.codeSystems.viewCodes', icon: 'eye', run: (row) => this.openItems(row) },
    ],
  };

  // import dialog
  protected readonly importOpen = signal(false);
  protected readonly importTarget = signal<CodeSystemListItem | null>(null);
  protected readonly file = signal<File | null>(null);
  protected readonly importing = signal(false);
  protected readonly importError = signal<string | null>(null);
  protected readonly importResult = signal<CodeSystemImportResult | null>(null);

  // items dialog
  protected readonly itemsOpen = signal(false);
  protected readonly itemsTarget = signal<CodeSystemListItem | null>(null);
  protected readonly itemsSearch = signal<string | null>(null);
  protected readonly itemsPage = signal<PageRequest>({ page: 1, pageSize: 50 });
  protected readonly itemId = (item: CodeSystemItemView): string => item.code;
  protected readonly itemColumns: readonly TableColumn<CodeSystemItemView>[] = [
    { key: 'code', header: 'platform.codeSystems.column.code', mono: true, width: '160px' },
    { key: 'display', header: 'platform.codeSystems.column.display' },
  ];

  protected readonly items = resource({
    params: () => ({ target: this.itemsTarget(), search: this.itemsSearch(), page: this.itemsPage(), open: this.itemsOpen() }),
    loader: ({ params, abortSignal }) =>
      params.target === null || !params.open ? Promise.resolve(null) : this.api.listCodeSystemItems(params.target.id, params.search, params.page, abortSignal),
  });

  protected readonly itemRows = computed<readonly CodeSystemItemView[]>(() => this.items.value()?.items ?? []);

  protected readonly itemsTitle = computed(() => {
    const target = this.itemsTarget();
    return target === null ? this.i18n.t('platform.codeSystems.viewCodes') : `${this.label(target.name)} ${target.version}`;
  });

  constructor() {
    void this.i18n.load('platform');
  }

  protected label(name: CodeSystemName): string {
    return NAME_LABELS[name] ?? name;
  }

  protected onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const chosen = input.files?.item(0) ?? null;
    this.importResult.set(null);
    if (chosen !== null && chosen.size > CODE_IMPORT_MAX_BYTES) {
      this.importError.set(this.i18n.t('platform.codeSystems.tooLarge'));
      this.file.set(null);
      return;
    }

    this.importError.set(null);
    this.file.set(chosen);
  }

  protected async runImport(): Promise<void> {
    const target = this.importTarget();
    const chosen = this.file();
    if (target === null || chosen === null || this.importing()) {
      return;
    }

    this.importing.set(true);
    this.importError.set(null);
    try {
      const result = await this.api.importCodeSystemItems(target.id, chosen);
      this.importResult.set(result);
      this.toast.success(this.i18n.t('platform.codeSystems.imported'));
      this.page().reload();
    } catch (error: unknown) {
      this.importError.set(error instanceof ProblemError ? problemMessage(this.i18n, 'platform.codeSystems', error.problem) : this.i18n.t('shared.error.generic'));
    } finally {
      this.importing.set(false);
    }
  }

  protected onItemsSearch(search: string): void {
    this.itemsSearch.set(search === '' ? null : search);
    this.itemsPage.update((current) => ({ ...current, page: 1 }));
  }

  protected async exportCsv(): Promise<void> {
    const rows = await this.api.all((page) => this.api.listCodeSystems({ search: this.filter.search || null, isActive: this.filter.active, ...page }));
    this.csv.download(`code-systems-${fileStamp()}`, [
      { header: this.i18n.t('platform.codeSystems.column.name'), value: (row: CodeSystemListItem) => this.label(row.name) },
      { header: this.i18n.t('platform.codeSystems.column.version'), value: (row) => row.version },
      { header: this.i18n.t('platform.codeSystems.column.items'), value: (row) => row.itemCount },
      { header: this.i18n.t('platform.codeSystems.column.lastImport'), value: (row) => row.lastImportedAtUtc },
      { header: this.i18n.t('platform.codeSystems.column.use'), value: (row) => row.use },
      { header: this.i18n.t('platform.active'), value: (row) => (row.isActive ? this.i18n.t('platform.yes') : this.i18n.t('platform.no')) },
    ], rows);
  }

  private openImport(row: CodeSystemListItem): void {
    this.importTarget.set(row);
    this.file.set(null);
    this.importError.set(null);
    this.importResult.set(null);
    this.importOpen.set(true);
  }

  private openItems(row: CodeSystemListItem): void {
    this.itemsTarget.set(row);
    this.itemsSearch.set(null);
    this.itemsPage.set({ page: 1, pageSize: 50 });
    this.itemsOpen.set(true);
  }
}
