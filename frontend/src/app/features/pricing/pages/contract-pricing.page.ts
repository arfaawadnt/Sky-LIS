import { ChangeDetectionStrategy, Component, computed, effect, inject, input, linkedSignal, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { type AbstractControl, FormArray, FormControl, FormGroup, ReactiveFormsModule, type ValidationErrors, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import type { PageRequest } from '../../../core/api/paging';
import {
  type BasePriceListItem,
  type ContractPriceItem,
  type ContractPricingDetail,
  PRICE_LIMITS,
  PRICING_PERMISSIONS,
  type ServiceLabel,
  type TierOption,
} from '../../../core/api/pricing.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { PrintService } from '../../../shared/print/print.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import { type TableColumn, type TableSort, sortRows } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { csvFileName, downloadCsv, toCsv } from '../data/csv';
import { formatAmount, formatFactor, hasScale, round2 } from '../data/money';
import { ServiceCatalogApi } from '../data/service-catalog.api';
import { ContractPricingFacade } from '../state/contract-pricing.facade';
import { ContractReviewDocumentComponent, type ReviewLabels, type ReviewLine } from '../ui/contract-review.document';
import { CopyTierDialogComponent, type CopyTierRequest } from '../ui/copy-tier-dialog.component';
import { NegotiatePriceDialogComponent, type NegotiationTarget } from '../ui/negotiate-price-dialog.component';
import { PriceAlertDialogComponent } from '../ui/price-alert-dialog.component';
import { ServicePickerComponent } from '../ui/service-picker.component';

const PAGE_SIZE = 50;
const EXAMPLE_PRICE = 100;

type TermGroup = FormGroup<{ baseListId: FormControl<string>; patientFactor: FormControl<number>; payerFactor: FormControl<number> }>;

function termGroup(): TermGroup {
  const factor = [Validators.required, Validators.min(0), Validators.max(PRICE_LIMITS.maxFactor)];
  return new FormGroup({
    baseListId: new FormControl('', { nonNullable: true }),
    patientFactor: new FormControl(1, { nonNullable: true, validators: factor }),
    payerFactor: new FormControl(0, { nonNullable: true, validators: factor }),
  });
}

/** Formula rules the server enforces too (PRC-007…010): a list at least, no list twice, four decimals, end ≥ start. */
export function formulaValidator(control: AbstractControl): ValidationErrors | null {
  const value = (control as FormGroup).getRawValue() as { effectiveFrom: string; effectiveTo: string; terms: { baseListId: string; patientFactor: number; payerFactor: number }[] };
  const chosen = value.terms.filter((term) => term.baseListId !== '');
  const errors: Record<string, true> = {};
  if (chosen.length === 0) {
    errors['formulaEmpty'] = true;
  }

  if (new Set(chosen.map((term) => term.baseListId)).size !== chosen.length) {
    errors['formulaDuplicate'] = true;
  }

  if (chosen.some((term) => !hasScale(Number(term.patientFactor), 4) || !hasScale(Number(term.payerFactor), 4))) {
    errors['factorScale'] = true;
  }

  if (value.effectiveTo !== '' && value.effectiveFrom !== '' && value.effectiveTo < value.effectiveFrom) {
    errors['datesInverted'] = true;
  }

  return Object.keys(errors).length > 0 ? errors : null;
}

/**
 * Contract pricing of one tier (SRS CP-34, BR-06/BR-07, WF-10 step 2): pick payer and tier; edit the formula (up to three
 * base lists with patient and payer factors, effective dates, Ctrl+S); Apply (confirmed; excluded prices are kept);
 * the derived price grid with search, negotiate / restore / alert per service; copy from another tier; the printable
 * review report; CSV export.
 */
@Component({
  selector: 'app-contract-pricing-page',
  imports: [
    ReactiveFormsModule,
    SaveShortcutDirective,
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
    IconComponent,
    EmptyStateComponent,
    FormFieldComponent,
    ServicePickerComponent,
    NegotiatePriceDialogComponent,
    PriceAlertDialogComponent,
    CopyTierDialogComponent,
  ],
  templateUrl: './contract-pricing.page.html',
  styleUrl: './contract-pricing.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContractPricingPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(ContractPricingFacade);
  private readonly catalog = inject(ServiceCatalogApi);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly printer = inject(PrintService);

  /** Route parameter (`withComponentInputBinding`); absent on `/pricing/contract-pricing`. */
  readonly tierId = input<string | undefined>(undefined);

  protected readonly permissions = PRICING_PERMISSIONS;
  protected readonly priorities = [0, 1, 2];
  protected readonly page = signal<PageRequest>({ page: 1, pageSize: PAGE_SIZE });

  protected readonly detail = computed<ContractPricingDetail | null>(() => (this.facade.detail.hasValue() ? this.facade.detail.value() : null));

  protected readonly options = computed<readonly TierOption[]>(() => (this.facade.tierOptions.hasValue() ? this.facade.tierOptions.value() : []));

  protected readonly payers = computed(() => {
    const seen = new Map<string, TierOption>();
    for (const option of this.options()) {
      if (!seen.has(option.payerId)) {
        seen.set(option.payerId, option);
      }
    }

    return [...seen.values()];
  });

  /** The payer of the picker: the loaded tier's, or the one the user just chose. */
  protected readonly payerId = linkedSignal(() => this.detail()?.payerId ?? this.options().find((option) => option.id === this.tierId())?.payerId ?? '');

  protected readonly payerTiers = computed(() => this.options().filter((option) => option.payerId === this.payerId()));

  protected readonly lists = computed<readonly BasePriceListItem[]>(() => (this.facade.baseLists.hasValue() ? this.facade.baseLists.value().items : []));

  protected readonly formulaForm = new FormGroup(
    {
      effectiveFrom: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
      effectiveTo: new FormControl('', { nonNullable: true }),
      terms: new FormArray<TermGroup>([termGroup(), termGroup(), termGroup()]),
    },
    { validators: [formulaValidator] },
  );

  private readonly formulaValue = toSignal(this.formulaForm.valueChanges, { initialValue: this.formulaForm.getRawValue() });

  protected readonly formulaErrors = computed(() => {
    this.formulaValue();
    return Object.keys(this.formulaForm.errors ?? {});
  });

  protected readonly columns: readonly TableColumn<ContractPriceItem>[] = [
    { key: 'code', header: 'pricing.grid.code', mono: true, width: '110px', sortable: true, value: (row) => this.facade.names.code(row.serviceId) },
    { key: 'service', header: 'pricing.grid.service', sortable: true, value: (row) => this.facade.names.name(row.serviceId, this.i18n.lang()) },
    { key: 'source', header: 'pricing.grid.source', mono: true, width: '100px', secondary: true, sortable: true, value: (row) => row.sourceListCode ?? '' },
    { key: 'listPrice', header: 'pricing.grid.listPrice', align: 'end', width: '110px', sortable: true, value: (row) => row.listPrice },
    { key: 'patientShare', header: 'pricing.grid.patientShare', align: 'end', width: '110px', sortable: true, value: (row) => row.patientShare },
    { key: 'payerShare', header: 'pricing.grid.payerShare', align: 'end', width: '110px', sortable: true, value: (row) => row.payerShare },
    { key: 'total', header: 'pricing.grid.total', align: 'end', width: '110px', secondary: true, value: (row) => round2(row.patientShare + row.payerShare) },
    { key: 'flags', header: 'pricing.grid.flags', width: '150px' },
  ];

  /** The column order chosen in the header: the grid holds every price, so the whole list is sorted before the page is cut (B-03). */
  protected readonly sort = signal<TableSort | null>(null);

  protected readonly pagedPrices = computed(() => {
    const { page, pageSize } = this.page();
    return sortRows(this.facade.visiblePrices(), this.columns, this.sort(), this.i18n.lang()).slice((page - 1) * pageSize, page * pageSize);
  });

  /** A header click sorts every price (the table shows the slice as given) and goes back to page 1. */
  protected onSort(sort: TableSort | null): void {
    this.sort.set(sort);
    this.page.update((current) => ({ ...current, page: 1 }));
  }

  protected readonly searchService = (term: string): Promise<readonly ServiceLabel[]> => this.catalog.search(term);

  // dialogs
  protected readonly negotiateOpen = signal(false);
  protected readonly negotiation = signal<NegotiationTarget | null>(null);
  protected readonly alertOpen = signal(false);
  protected readonly alertTarget = signal<ContractPriceItem | null>(null);
  protected readonly copyOpen = signal(false);
  protected readonly printing = signal(false);

  protected readonly alertLabel = computed(() => {
    const target = this.alertTarget();
    return target ? this.serviceLabel(target.serviceId) : '';
  });

  protected readonly priceRowId = (row: ContractPriceItem): string => row.serviceId;

  constructor() {
    void this.i18n.load('pricing');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)

    effect(() => {
      const tierId = this.tierId() ?? null;
      untracked(() => {
        this.facade.select(tierId);
        this.page.set({ page: 1, pageSize: PAGE_SIZE });
      });
    });

    // the formula form follows the loaded tier, unless the user is editing it
    let patchedTier: string | null = null;
    effect(() => {
      const detail = this.detail();
      const lists = this.lists();
      untracked(() => {
        if (detail && (patchedTier !== detail.contractTierId || this.formulaForm.pristine)) {
          patchedTier = detail.contractTierId;
          this.patchFormula(detail, lists);
        }
      });
    });
  }

  // ---- picker ----

  protected payerLabel(option: TierOption): string {
    return localizedPick({ en: option.payerNameEn, ar: option.payerNameAr }, this.i18n.lang()) + (option.payerIsActive ? '' : ` (${this.i18n.t('pricing.inactive')})`);
  }

  protected tierLabel(option: TierOption): string {
    return localizedPick({ en: option.nameEn, ar: option.nameAr }, this.i18n.lang()) + (option.isActive ? '' : ` (${this.i18n.t('pricing.inactive')})`) + (option.hasPricing ? '' : ` — ${this.i18n.t('pricing.contract.noPricingShort')}`);
  }

  protected onPayerChange(event: Event): void {
    const payerId = (event.target as HTMLSelectElement).value;
    this.payerId.set(payerId);
    const tiers = this.options().filter((option) => option.payerId === payerId);
    const first = tiers.find((option) => option.isActive) ?? tiers[0];
    if (first) {
      void this.router.navigate(['/pricing/contract-pricing', first.id]);
    }
  }

  protected onTierChange(event: Event): void {
    const tierId = (event.target as HTMLSelectElement).value;
    if (tierId !== '') {
      void this.router.navigate(['/pricing/contract-pricing', tierId]);
    }
  }

  // ---- formula ----

  protected get terms(): FormArray<TermGroup> {
    return this.formulaForm.controls.terms;
  }

  protected listLabel(list: BasePriceListItem): string {
    return `${list.code} · ${localizedPick({ en: list.nameEn, ar: list.nameAr }, this.i18n.lang())} (${list.year})${list.isActive ? '' : ` — ${this.i18n.t('pricing.inactive')}`}`;
  }

  protected example(index: number): string {
    this.formulaValue();
    const term = this.terms.at(index).getRawValue();
    if (term.baseListId === '') {
      return '—';
    }

    const lang = this.i18n.lang();
    const patient = round2(EXAMPLE_PRICE * Number(term.patientFactor));
    const payer = round2(EXAMPLE_PRICE * Number(term.payerFactor));
    return this.i18n.tf('pricing.contract.example', {
      price: formatAmount(EXAMPLE_PRICE, lang),
      patientFactor: formatFactor(Number(term.patientFactor), lang),
      patient: formatAmount(patient, lang),
      payerFactor: formatFactor(Number(term.payerFactor), lang),
      payer: formatAmount(payer, lang),
    });
  }

  protected async saveFormula(): Promise<void> {
    const tierId = this.facade.tierId();
    if (tierId === null || this.facade.saveFormula.pending() || !markAllTouched(this.formulaForm) || this.formulaForm.invalid) {
      return;
    }

    const value = this.formulaForm.getRawValue();
    const request = {
      effectiveFrom: value.effectiveFrom,
      effectiveTo: value.effectiveTo === '' ? null : value.effectiveTo,
      terms: value.terms.filter((term) => term.baseListId !== '').map((term) => ({ baseListId: term.baseListId, patientFactor: Number(term.patientFactor), payerFactor: Number(term.payerFactor) })),
    };
    if ((await this.facade.saveFormula.execute({ tierId, request })) !== null) {
      this.formulaForm.markAsPristine();
      this.toast.success(this.i18n.t('pricing.contract.formulaSaved'), { title: this.i18n.t('pricing.contract.applyReminder') });
    }
  }

  protected async apply(): Promise<void> {
    const detail = this.detail();
    if (!detail?.exists || this.facade.apply.pending()) {
      return;
    }

    if (this.formulaForm.dirty) {
      this.toast.warn(this.i18n.t('pricing.contract.saveBeforeApply'));
      return;
    }

    const confirmed = await this.confirm.confirm({
      title: this.i18n.t('pricing.contract.apply'),
      message: this.i18n.tc('pricing.contract.confirmApply', this.facade.excludedCount(), { tier: this.tierTitle(detail) }),
      confirmLabel: this.i18n.t('pricing.contract.apply'),
    });
    if (!confirmed) {
      return;
    }

    const result = await this.facade.apply.execute(detail.contractTierId);
    if (result !== null) {
      this.toast.success(this.i18n.tf('pricing.contract.applied', { priced: result.priced, added: result.added, updated: result.updated, removed: result.removed, kept: result.excludedKept }));
    }
  }

  // ---- prices ----

  protected onSearch(term: string): void {
    this.facade.search.set(term);
    this.page.update((current) => ({ ...current, page: 1 }));
  }

  protected serviceLabel(serviceId: string): string {
    const code = this.facade.names.code(serviceId);
    const name = this.facade.names.name(serviceId, this.i18n.lang());
    return [code, name].filter((part) => part !== '').join(' · ') || serviceId;
  }

  protected amount(value: number): string {
    return formatAmount(value, this.i18n.lang());
  }

  protected openNegotiate(row: ContractPriceItem): void {
    this.facade.negotiate.reset();
    this.negotiation.set({ serviceId: row.serviceId, label: this.serviceLabel(row.serviceId), listPrice: row.listPrice, patientShare: row.patientShare, payerShare: row.payerShare, isExcluded: row.isExcluded });
    this.negotiateOpen.set(true);
  }

  /** "Negotiate a service" picker: a service the tier prices opens its row; another one starts from zero. */
  protected onNegotiateService(service: ServiceLabel): void {
    this.facade.names.remember([service]);
    const existing = this.facade.prices().find((price) => price.serviceId === service.id);
    if (existing) {
      this.openNegotiate(existing);
      return;
    }

    this.facade.negotiate.reset();
    this.negotiation.set({ serviceId: service.id, label: this.serviceLabel(service.id), listPrice: 0, patientShare: 0, payerShare: 0, isExcluded: false });
    this.negotiateOpen.set(true);
  }

  protected async runNegotiate(request: { patientShare: number; payerShare: number }): Promise<void> {
    const tierId = this.facade.tierId();
    const target = this.negotiation();
    if (tierId === null || target === null) {
      return;
    }

    if ((await this.facade.negotiate.execute({ tierId, serviceId: target.serviceId, request })) !== null) {
      this.negotiateOpen.set(false);
      this.toast.success(this.i18n.t('pricing.negotiate.done'));
    }
  }

  protected async restore(row: ContractPriceItem): Promise<void> {
    const tierId = this.facade.tierId();
    if (tierId === null) {
      return;
    }

    const confirmed = await this.confirm.confirm({
      title: this.i18n.t('pricing.contract.restore'),
      message: this.i18n.tf('pricing.contract.confirmRestore', { service: this.serviceLabel(row.serviceId) }),
    });
    if (confirmed && (await this.facade.clearExclusion.execute({ tierId, serviceId: row.serviceId })) !== null) {
      this.toast.success(this.i18n.t('pricing.contract.restored'));
    }
  }

  protected openAlert(row: ContractPriceItem): void {
    this.facade.setAlert.reset();
    this.alertTarget.set(row);
    this.alertOpen.set(true);
  }

  protected async runAlert(alertCondition: string | null): Promise<void> {
    const tierId = this.facade.tierId();
    const target = this.alertTarget();
    if (tierId === null || target === null) {
      return;
    }

    if ((await this.facade.setAlert.execute({ tierId, serviceId: target.serviceId, alertCondition })) !== null) {
      this.alertOpen.set(false);
      this.toast.success(this.i18n.t(alertCondition === null ? 'pricing.alert.cleared' : 'pricing.alert.saved'));
    }
  }

  protected openCopy(): void {
    this.facade.copyFrom.reset();
    this.copyOpen.set(true);
  }

  protected async runCopy(request: CopyTierRequest): Promise<void> {
    const tierId = this.facade.tierId();
    if (tierId === null) {
      return;
    }

    if (this.detail()?.exists) {
      const confirmed = await this.confirm.confirm({ title: this.i18n.t('pricing.copyTier.title'), message: this.i18n.t('pricing.copyTier.confirmReplace'), danger: true });
      if (!confirmed) {
        return;
      }
    }

    if ((await this.facade.copyFrom.execute({ tierId, sourceTierId: request.sourceTierId, factor: request.factor })) !== null) {
      this.copyOpen.set(false);
      this.formulaForm.markAsPristine();
      this.toast.success(this.i18n.t('pricing.copyTier.done'));
    }
  }

  protected exportCsv(): void {
    const detail = this.detail();
    if (!detail) {
      return;
    }

    const lang = this.i18n.lang();
    const t = (key: string): string => this.i18n.t(key);
    const header = [t('pricing.grid.code'), t('pricing.grid.service'), t('pricing.grid.source'), t('pricing.grid.listPrice'), t('pricing.grid.patientShare'), t('pricing.grid.payerShare'), t('pricing.grid.excluded'), t('pricing.grid.alert')];
    const rows = detail.prices.map((price) => [
      this.facade.names.code(price.serviceId),
      this.facade.names.name(price.serviceId, lang),
      price.sourceListCode,
      price.listPrice,
      price.patientShare,
      price.payerShare,
      t(price.isExcluded ? 'shared.yes' : 'shared.no'),
      price.alertCondition,
    ]);
    downloadCsv(csvFileName(`${detail.payerNameEn}-${detail.tierNameEn}-prices`, new Date()), toCsv(header, rows));
  }

  protected async printReview(): Promise<void> {
    const detail = this.detail();
    if (!detail?.exists || this.printing()) {
      return;
    }

    this.printing.set(true);
    try {
      const doc = await this.facade.review(detail.contractTierId);
      this.facade.names.rememberRows(doc.prices);
      const lang = this.i18n.lang();
      const lines: ReviewLine[] = doc.prices.map((price) => ({
        code: this.facade.names.code(price.serviceId),
        name: this.facade.names.name(price.serviceId, lang),
        source: price.sourceListCode ?? '',
        listPrice: price.listPrice,
        patientShare: price.patientShare,
        payerShare: price.payerShare,
        excluded: price.isExcluded,
        alert: price.alertCondition,
      }));
      await this.printer.print({
        title: `${this.i18n.t('pricing.review.title')} — ${this.tierTitle(detail)}`,
        size: 'A4',
        landscape: true,
        component: { type: ContractReviewDocumentComponent, inputs: { doc, lines, labels: this.reviewLabels(), lang } },
      });
    } catch {
      this.toast.error(this.i18n.t('pricing.review.failed'));
    } finally {
      this.printing.set(false);
    }
  }

  protected tierTitle(detail: ContractPricingDetail): string {
    const lang = this.i18n.lang();
    return `${localizedPick({ en: detail.payerNameEn, ar: detail.payerNameAr }, lang)} / ${localizedPick({ en: detail.tierNameEn, ar: detail.tierNameAr }, lang)}`;
  }

  protected appliedText(detail: ContractPricingDetail): string {
    if (!detail.appliedAtUtc) {
      return this.i18n.t('pricing.contract.neverApplied');
    }

    const date = new Date(detail.appliedAtUtc);
    return Number.isNaN(date.getTime()) ? detail.appliedAtUtc : new Intl.DateTimeFormat(this.i18n.lang() === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'medium', timeStyle: 'short' }).format(date);
  }

  protected problemText(code: string): string {
    return problemMessage(this.i18n, 'pricing', { code });
  }

  private patchFormula(detail: ContractPricingDetail, lists: readonly BasePriceListItem[]): void {
    const today = new Date().toISOString().slice(0, 10);
    const fallbackList = [...lists].filter((list) => list.isActive).sort((a, b) => b.year - a.year)[0]?.id ?? '';
    const terms = this.priorities.map((index) => {
      const term = detail.terms[index];
      if (term) {
        return { baseListId: term.baseListId, patientFactor: term.patientFactor, payerFactor: term.payerFactor };
      }

      // a tier without a formula starts from the most recent active list at 100 % / 0 %
      return index === 0 && !detail.exists ? { baseListId: fallbackList, patientFactor: 1, payerFactor: 0 } : { baseListId: '', patientFactor: 1, payerFactor: 0 };
    });
    this.formulaForm.reset({ effectiveFrom: detail.effectiveFrom ?? today, effectiveTo: detail.effectiveTo ?? '', terms });
  }

  private reviewLabels(): ReviewLabels {
    const t = (key: string): string => this.i18n.t(`pricing.review.${key}`);
    return {
      title: t('title'),
      payer: t('payer'),
      tier: t('tier'),
      effective: t('effective'),
      openEnded: t('openEnded'),
      applied: t('applied'),
      never: t('never'),
      currency: t('currency'),
      formula: t('formula'),
      priority: t('priority'),
      baseList: t('baseList'),
      patientFactor: t('patientFactor'),
      payerFactor: t('payerFactor'),
      inactive: t('inactive'),
      prices: t('prices'),
      code: t('code'),
      service: t('service'),
      source: t('source'),
      listPrice: t('listPrice'),
      patientShare: t('patientShare'),
      payerShare: t('payerShare'),
      total: t('total'),
      excluded: t('excluded'),
      alert: t('alert'),
      totals: t('totals'),
      preparedBy: t('preparedBy'),
      reviewedBy: t('reviewedBy'),
      approvedBy: t('approvedBy'),
      labNameEn: t('labNameEn'),
      labNameAr: t('labNameAr'),
    };
  }
}
