import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import type { PageRequest } from '../../../core/api/paging';
import { type CopyBasePriceListRequest, PRICING_PERMISSIONS, type ServiceLabel } from '../../../core/api/pricing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { inReleaseScope } from '../../../core/release';
import { localizedPick } from '../../../shared/forms/localized-text';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { type Breadcrumb, PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { csvFileName, downloadCsv, toCsv } from '../data/csv';
import { formatAmount } from '../data/money';
import { ServiceCatalogApi } from '../data/service-catalog.api';
import { type EditorRow, PriceListEditorFacade } from '../state/price-list-editor.facade';
import { CopyListDialogComponent } from '../ui/copy-list-dialog.component';
import { ServicePickerComponent } from '../ui/service-picker.component';

const PAGE_SIZE = 50;

/**
 * The price grid of one base list (SRS CP-33): inline price and active flag per service, new services through the
 * catalogue typeahead (Enter adds), Ctrl+S saves every changed line in one call, copy into next year's list, Apply to
 * the derived contracts (BR-07, confirmed), activation, CSV export. Leaving with unsaved changes asks first.
 */
@Component({
  selector: 'app-price-list-editor-page',
  imports: [
    RouterLink,
    SaveShortcutDirective,
    CanDirective,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    IconComponent,
    ServicePickerComponent,
    CopyListDialogComponent,
  ],
  templateUrl: './price-list-editor.page.html',
  styleUrl: './price-list-editor.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PriceListEditorPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(PriceListEditorFacade);
  private readonly catalog = inject(ServiceCatalogApi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly permissionsService = inject(PermissionsService);

  /** Route parameter (`withComponentInputBinding`). */
  readonly listId = input.required<string>();

  protected readonly permissions = PRICING_PERMISSIONS;

  /** Derived contracts link to contract pricing (CP-34) only when that Release-2 page is in scope (UAT R1-03). */
  protected readonly contractPricingOffered = inReleaseScope('R2');
  protected readonly page = signal<PageRequest>({ page: 1, pageSize: PAGE_SIZE });
  protected readonly copyOpen = signal(false);

  protected readonly detail = computed(() => (this.facade.detail.hasValue() ? this.facade.detail.value() : null));

  protected readonly title = computed(() => {
    const detail = this.detail();
    return detail ? `${detail.code} · ${localizedPick({ en: detail.nameEn, ar: detail.nameAr }, this.i18n.lang())}` : this.i18n.t('pricing.lists.title');
  });

  protected readonly crumbs = computed<readonly Breadcrumb[]>(() => [
    { label: this.i18n.t('pricing.lists.title'), path: '/pricing/base-price-lists' },
    { label: this.detail()?.code ?? '…', path: null },
  ]);

  protected readonly columns: readonly TableColumn<EditorRow>[] = [
    { key: 'code', header: 'pricing.grid.code', mono: true, width: '120px', sortable: true, value: (row) => this.facade.names.code(row.serviceId) },
    { key: 'service', header: 'pricing.grid.service', sortable: true, value: (row) => this.facade.names.name(row.serviceId, this.i18n.lang()) },
    { key: 'price', header: 'pricing.grid.price', align: 'end', width: '160px', sortable: true, value: (row) => row.price },
    { key: 'active', header: 'pricing.grid.activeAtOrdering', align: 'center', width: '130px' },
    { key: 'state', header: 'pricing.grid.state', width: '110px', secondary: true },
  ];

  protected readonly pagedRows = computed(() => {
    const { page, pageSize } = this.page();
    return this.facade.visibleRows().slice((page - 1) * pageSize, page * pageSize);
  });

  /** Inline inputs are read-only without pricing.edit (affordance; the server decides). */
  protected readonly canEdit = computed(() => this.permissionsService.can(PRICING_PERMISSIONS.edit));

  protected readonly rowClass = (row: EditorRow): string | null => (row.invalid && row.dirty ? 'invalid-row' : row.isActive ? null : 'inactive');

  protected readonly searchService = (term: string): Promise<readonly ServiceLabel[]> => this.catalog.search(term);

  constructor() {
    void this.i18n.load('pricing');
    effect(() => {
      const id = this.listId();
      untracked(() => this.facade.load(id));
    });
  }

  protected onSearch(term: string): void {
    this.facade.search.set(term);
    this.page.update((current) => ({ ...current, page: 1 }));
  }

  protected onPrice(row: EditorRow, event: Event): void {
    const value = (event.target as HTMLInputElement).valueAsNumber;
    this.facade.setPrice(row.serviceId, Number.isNaN(value) ? -1 : value);
  }

  protected onActive(row: EditorRow, event: Event): void {
    this.facade.setLineActive(row.serviceId, (event.target as HTMLInputElement).checked);
  }

  protected onPicked(service: ServiceLabel): void {
    if (this.facade.addService(service)) {
      this.facade.search.set('');
      this.page.update((current) => ({ ...current, page: 1 }));
      this.toast.info(this.i18n.tf('pricing.editor.added', { code: service.code || service.nameEn }));
    } else {
      this.facade.search.set(service.code || service.nameEn);
      this.toast.info(this.i18n.t('pricing.editor.alreadyOnList'));
    }
  }

  protected async save(): Promise<void> {
    const listId = this.facade.listId();
    const lines = this.facade.pendingLines();
    if (listId === null || lines.length === 0 || this.facade.save.pending()) {
      return;
    }

    if (this.facade.invalidCount() > 0) {
      this.toast.error(this.i18n.t('pricing.editor.fixInvalid'));
      return;
    }

    const result = await this.facade.save.execute({ listId, lines });
    if (result !== null) {
      this.toast.success(this.i18n.tf('pricing.editor.saved', { added: result.added, changed: result.changed }), { title: this.i18n.t('pricing.editor.applyReminder') });
    }
  }

  protected async discard(): Promise<void> {
    if (this.facade.dirtyRows().length === 0) {
      return;
    }

    if (await this.confirm.confirm({ title: this.i18n.t('pricing.editor.discard'), message: this.i18n.t('pricing.editor.confirmDiscard'), danger: true })) {
      this.facade.discard();
    }
  }

  protected async toggleActive(): Promise<void> {
    const detail = this.detail();
    if (!detail) {
      return;
    }

    if (detail.isActive) {
      const confirmed = await this.confirm.confirm({
        title: this.i18n.t('pricing.lists.deactivate'),
        message: this.i18n.tf('pricing.editor.confirmDeactivate', { code: detail.code }),
        danger: true,
      });
      if (!confirmed) {
        return;
      }
    }

    if ((await this.facade.setActive.execute({ listId: detail.id, active: !detail.isActive })) !== null) {
      this.toast.success(this.i18n.t(detail.isActive ? 'pricing.lists.deactivated' : 'pricing.lists.activated'));
    }
  }

  protected async apply(): Promise<void> {
    const detail = this.detail();
    if (!detail) {
      return;
    }

    if (this.facade.dirtyRows().length > 0) {
      this.toast.warn(this.i18n.t('pricing.editor.saveBeforeApply'));
      return;
    }

    const confirmed = await this.confirm.confirm({
      title: this.i18n.t('pricing.editor.apply'),
      message: this.i18n.tf('pricing.editor.confirmApply', { code: detail.code, count: detail.derivedContracts.length }),
      confirmLabel: this.i18n.t('pricing.editor.apply'),
    });
    if (!confirmed) {
      return;
    }

    const result = await this.facade.apply.execute(detail.id);
    if (result !== null) {
      this.toast.success(this.i18n.tf('pricing.editor.applied', { contracts: result.contractsApplied, prices: result.pricesChanged }));
    }
  }

  protected openCopy(): void {
    this.facade.copy.reset();
    this.copyOpen.set(true);
  }

  protected async runCopy(request: CopyBasePriceListRequest): Promise<void> {
    const listId = this.facade.listId();
    if (listId === null) {
      return;
    }

    const created = await this.facade.copy.execute({ listId, request });
    if (created !== null) {
      this.copyOpen.set(false);
      this.toast.success(this.i18n.tf('pricing.copy.done', { code: request.code.toUpperCase() }));
      void this.router.navigate(['/pricing/base-price-lists', created.id]);
    }
  }

  protected exportCsv(): void {
    const detail = this.detail();
    const lines = this.facade.lines.hasValue() ? this.facade.lines.value() : [];
    if (!detail) {
      return;
    }

    const lang = this.i18n.lang();
    const header = [this.i18n.t('pricing.grid.code'), this.i18n.t('pricing.grid.service'), this.i18n.t('pricing.grid.price'), this.i18n.t('pricing.grid.activeAtOrdering')];
    const rows = lines.map((line) => [this.facade.names.code(line.serviceId), this.facade.names.name(line.serviceId, lang), line.price, this.i18n.t(line.isActive ? 'shared.yes' : 'shared.no')]);
    downloadCsv(csvFileName(`${detail.code}-prices`, new Date()), toCsv(header, rows));
    this.toast.success(this.i18n.t('pricing.exported'));
  }

  protected tierLabel(nameEn: string, nameAr: string): string {
    return localizedPick({ en: nameEn, ar: nameAr }, this.i18n.lang());
  }

  protected amount(value: number | null): string {
    return value === null ? '—' : formatAmount(value, this.i18n.lang());
  }

  protected problemText(code: string): string {
    return problemMessage(this.i18n, 'pricing', { code });
  }

  /** CanDeactivate: unsaved prices ask before leaving. */
  async canLeave(): Promise<boolean> {
    if (this.facade.dirtyRows().length === 0) {
      return true;
    }

    return this.confirm.confirm({ title: this.i18n.t('pricing.editor.leaveTitle'), message: this.i18n.t('pricing.editor.leaveMessage'), danger: true });
  }
}
