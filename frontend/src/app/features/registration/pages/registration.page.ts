import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { PAYMENT_METHODS, type PaymentCreated, type PaymentMethod } from '../../../core/api/billing.contracts';
import type { PatientSummary } from '../../../core/api/patients.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import {
  type DiscountKind,
  type OrderIssue,
  type OrderLinePreview,
  type OrderWarning,
  type PreparationQuestionItem,
  REGISTRATION_PERMISSIONS,
  type TubePreview,
  type VisitLineItem,
  type VisitRegisteredResponse,
} from '../../../core/api/registration.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { dateTime, money, pick } from '../data/registration-format';
import { PatientLookupApi } from '../../../shared/patient/patient-lookup.api';
import { toSummary } from '../../../shared/patient/patient-demographics';
import { PatientPickerComponent } from '../../../shared/patient/patient-picker.component';
import { RegistrationFacade, type SaveBlocker } from '../state/registration.facade';
import { RegistrationPrinter } from '../state/registration-printer';
import { type PickedService, ServiceEntryComponent } from '../ui/service-entry.component';

interface SuccessState {
  readonly visit: VisitRegisteredResponse;
  readonly patient: PatientSummary | null;
  readonly payment: PaymentCreated | null;
  readonly paymentProblem: ProblemDetails | null;
  readonly amount: number;
}

/**
 * Registration (SRS CP-06, WF-02; Release 1: no location/room, payment captured on the page, BR-26). Keyboard-first:
 * the patient picker (search, similar-name warning, new file), the visit data, the service entry (Enter resolves a
 * code, shortcut number, alias or set; F2 opens the picker), the lines priced and planned by the server dry run
 * (tubes BR-02, expected dates BR-13, frequency BR-12, contract alerts, rules, preparations), the discount within the
 * user's cap, the payment panel and Ctrl+S. After saving, a dialog shows the accession, the payment is posted to Billing
 * and labels, job order and receipt print per the parameters. `/visits/:visitId` opens a saved visit to add or release
 * services and change its details or discount (no payment panel there).
 */
@Component({
  selector: 'app-registration-page',
  imports: [
    RouterLink,
    SaveShortcutDirective,
    PageHeaderComponent,
    SectionCardComponent,
    BadgeComponent,
    CanDirective,
    DialogComponent,
    IconComponent,
    PatientPickerComponent,
    ServiceEntryComponent,
  ],
  templateUrl: './registration.page.html',
  styleUrl: './registration.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegistrationPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(RegistrationFacade);
  private readonly auth = inject(AuthStore);
  private readonly patients = inject(PatientLookupApi);
  private readonly printer = inject(RegistrationPrinter);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly entry = viewChild(ServiceEntryComponent);

  /** Route parameter of the edit page. */
  readonly visitId = input<string>();
  /** `?patientId=` (patient file "new visit"), `?services=a,b&tierId=` (price query "continue to registration"). */
  readonly patientId = input<string>();
  readonly services = input<string>();
  readonly tierId = input<string>();

  protected readonly permissions = REGISTRATION_PERMISSIONS;

  /** Read-only without the right the save needs (UAT R3-02); the server refuses anyway, the page no longer pretends. */
  protected readonly readOnly = computed(() =>
    !this.auth.hasPermission(this.facade.isEdit() ? REGISTRATION_PERMISSIONS.edit : REGISTRATION_PERMISSIONS.create));
  protected readonly methods = PAYMENT_METHODS;
  protected readonly discountKinds: readonly DiscountKind[] = ['Percent', 'Amount'];
  protected readonly saving = signal(false);
  protected readonly successOpen = signal(false);
  protected readonly success = signal<SuccessState | null>(null);
  private adoptedVisit: string | null = null;

  protected readonly title = computed(() => {
    const saved = this.facade.saved();
    return this.facade.isEdit() ? this.i18n.tf('registration.editTitle', { accession: saved?.accession ?? '…' }) : this.i18n.t('registration.title');
  });

  protected readonly branchName = computed(() => {
    const saved = this.facade.saved();
    return saved ? pick(saved.branchNameEn, saved.branchNameAr, this.i18n.lang()) : this.i18n.t('registration.sessionBranch');
  });

  protected readonly tubesByKey = computed(() => new Map<string, TubePreview>((this.facade.result()?.tubes ?? []).map((tube) => [tube.key, tube])));

  protected readonly manualIds = computed(() => new Set(this.facade.lines().map((line) => line.serviceId)));

  /** Draft lines not yet planned by the server (while the dry run runs or when it failed). */
  protected readonly pendingLines = computed(() => {
    const planned = new Set(this.facade.plannedLines().map((line) => line.serviceId));
    return this.facade.lines().filter((line) => !planned.has(line.serviceId));
  });

  protected readonly savedLines = computed<readonly VisitLineItem[]>(() => this.facade.saved()?.lines ?? []);

  protected readonly issuesByService = computed(() => {
    const map = new Map<string, OrderIssue[]>();
    for (const issue of this.facade.result()?.issues ?? []) {
      const key = issue.serviceId ?? '';
      map.set(key, [...(map.get(key) ?? []), issue]);
    }

    return map;
  });

  protected readonly preparations = computed<readonly PreparationQuestionItem[]>(() => this.facade.result()?.preparations ?? []);

  protected readonly currency = computed(() => this.facade.result()?.currency ?? this.facade.settings.value()?.currency ?? '');

  protected readonly treasuryList = computed(() => this.facade.treasuries.value() ?? []);

  constructor() {
    void this.i18n.load('registration');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14, A-01)

    effect(() => {
      const id = this.visitId() ?? null;
      untracked(() => {
        if (id !== this.facade.visitId()) {
          this.facade.open(id);
          this.adoptedVisit = null;
        }
      });
    });

    effect(() => {
      const saved = this.facade.saved();
      untracked(() => {
        if (saved !== null && this.adoptedVisit !== saved.id) {
          this.adoptedVisit = saved.id;
          this.facade.adopt(saved);
        }
      });
    });

    effect(() => {
      const options = this.facade.tierOptions();
      const preset = this.tierId();
      untracked(() => {
        if (options.length === 0 || this.facade.isEdit()) {
          return;
        }

        if (preset && options.some((tier) => tier.id === preset) && this.facade.header().tierId === '') {
          this.facade.patchHeader({ tierId: preset });
        }

        this.facade.defaultTier();
      });
    });

    effect(() => {
      const list = this.treasuryList();
      untracked(() => (list.length > 0 ? this.facade.defaultTreasury() : undefined));
    });

    effect(() => {
      const patientId = this.patientId();
      untracked(() => {
        if (patientId && !this.facade.isEdit() && this.facade.patient()?.id !== patientId) {
          void this.patients
            .get(patientId)
            .then((detail) => this.facade.setPatient(toSummary(detail)))
            .catch(() => this.toast.error(this.i18n.t('registration.patientLoadError')));
        }
      });
    });

    effect(() => {
      const ids = (this.services() ?? '').split(',').map((id) => id.trim()).filter((id) => id !== '');
      untracked(() => {
        if (ids.length > 0 && !this.facade.isEdit() && this.facade.lines().length === 0) {
          this.facade.addServices(ids.map((serviceId) => ({ serviceId, code: '…', nameEn: '', nameAr: '' })));
        }
      });
    });
  }

  // ---- view helpers --------------------------------------------------------------------------------------------------

  protected name(en: string | null | undefined, ar: string | null | undefined): string {
    return pick(en, ar, this.i18n.lang());
  }

  protected amount(value: number | null | undefined): string {
    return money(value, this.i18n.lang());
  }

  protected when(utc: string | null | undefined): string {
    return dateTime(utc, this.i18n.lang());
  }

  protected tierLabel(tier: { payerNameEn: string; payerNameAr: string; nameEn: string; nameAr: string }): string {
    return `${this.name(tier.payerNameEn, tier.payerNameAr)} — ${this.name(tier.nameEn, tier.nameAr)}`;
  }

  protected tubeOf(line: OrderLinePreview): TubePreview | null {
    return line.tubeKey ? (this.tubesByKey().get(line.tubeKey) ?? null) : null;
  }

  protected issuesOf(serviceId: string): readonly OrderIssue[] {
    return this.issuesByService().get(serviceId) ?? [];
  }

  protected issueText(issue: OrderIssue): string {
    const key = `registration.error.${issue.code}`;
    const prefix = issue.serviceCode ? `${issue.serviceCode}: ` : '';
    return `${prefix}${this.i18n.has(key) ? this.i18n.t(key) : issue.message}`;
  }

  protected warningText(warning: OrderWarning): string {
    const service = warning.serviceCode ?? '';
    const detail = this.i18n.lang() === 'ar' ? (warning.detailAr ?? warning.detail ?? '') : (warning.detail ?? '');
    return this.i18n.tf(`registration.warning.${warning.code}`, {
      service,
      detail,
      date: warning.lastOrderedAtUtc ? this.when(warning.lastOrderedAtUtc) : '',
      accession: warning.lastAccession ?? '',
      days: warning.windowDays ?? '',
    });
  }

  protected warningTone(code: string): 'warn' | 'info' | 'bad' {
    return code === 'REG-201' || code === 'REG-207' ? 'bad' : code === 'REG-202' || code === 'REG-203' ? 'warn' : 'info';
  }

  protected blockerText(blocker: SaveBlocker): string {
    return this.i18n.tf(`registration.validation.${blocker}`, { cap: this.facade.settings.value()?.discountCapPercent ?? 0 });
  }

  protected answerOf(preparationId: string): string {
    return this.facade.answers()[preparationId] ?? '';
  }

  protected isManual(serviceId: string): boolean {
    return this.manualIds().has(serviceId);
  }

  protected isReserved(serviceId: string): boolean {
    return this.facade.lines().find((line) => line.serviceId === serviceId)?.reserved ?? false;
  }

  // ---- inputs ----------------------------------------------------------------------------------------------------

  protected value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  protected numberOrNull(event: Event): number | null {
    const raw = (event.target as HTMLInputElement).value.trim();
    const parsed = Number(raw);
    return raw === '' || !Number.isFinite(parsed) ? null : parsed;
  }

  protected onPatient(patient: PatientSummary): void {
    this.facade.setPatient(patient);
    queueMicrotask(() => this.entry()?.focus());
  }

  protected onAdded(services: readonly PickedService[]): void {
    const duplicates = this.facade.addServices(services);
    if (duplicates.length > 0) {
      this.toast.warn(this.i18n.tf('registration.services.duplicate', { codes: duplicates.join(', ') }));
    }
  }

  protected setDiscountKind(kind: string): void {
    this.facade.patchDiscount({ kind: kind === 'Amount' ? 'Amount' : 'Percent' });
  }

  protected setMethod(method: PaymentMethod): void {
    this.facade.patchPayment({ method });
  }

  // ---- actions ---------------------------------------------------------------------------------------------------

  protected clear(): void {
    if (this.facade.isEdit()) {
      void this.router.navigate(['/registration/new']);
      return;
    }

    this.facade.reset(true);
  }

  protected async release(line: VisitLineItem): Promise<void> {
    const visitId = this.facade.visitId();
    if (visitId === null) {
      return;
    }

    if ((await this.facade.release.execute({ visitId, lineIds: [line.id] })) !== null) {
      this.toast.success(this.i18n.tf('registration.released', { code: line.code }));
    } else {
      this.toast.error(this.problemText(this.facade.release.problem()));
    }
  }

  protected async save(): Promise<void> {
    if (this.saving() || this.readOnly()) {
      return;
    }

    const blockers = this.facade.blockers();
    if (blockers.length > 0) {
      this.toast.error(this.blockerText(blockers[0] ?? 'lines'));
      return;
    }

    let confirm = false;
    const alerts = this.facade.frequencyAlerts();
    if (alerts.length > 0) {
      confirm = await this.confirm.confirm({
        title: this.i18n.t('registration.frequency.title'),
        message: alerts.map((alert) => this.warningText(alert)).join('\n'),
        confirmLabel: this.i18n.t('registration.frequency.confirm'),
      });
      if (!confirm) {
        return;
      }
    }

    this.saving.set(true);
    try {
      if (this.facade.isEdit()) {
        await this.saveEdit(confirm);
      } else {
        await this.saveNew(confirm);
      }
    } finally {
      this.saving.set(false);
    }
  }

  protected async print(kind: 'labels' | 'jobOrder' | 'receipt'): Promise<void> {
    const success = this.success();
    if (success === null) {
      return;
    }

    try {
      if (kind === 'labels') {
        await this.printer.labels(success.visit.id);
      } else if (kind === 'jobOrder') {
        await this.printer.jobOrder(success.visit.id);
      } else if (success.payment !== null) {
        await this.printer.receipt(success.payment.id);
      }
    } catch {
      this.toast.error(this.i18n.t('registration.printError'));
    }
  }

  protected newRegistration(): void {
    this.successOpen.set(false);
    this.facade.reset(true);
  }

  protected openVisit(): void {
    const success = this.success();
    this.successOpen.set(false);
    if (success !== null) {
      void this.router.navigate(['/registration/visits', success.visit.id]);
    }
  }

  protected goToPayments(): void {
    const success = this.success();
    this.successOpen.set(false);
    void this.router.navigate(['/billing/payments'], { queryParams: success ? { visitId: success.visit.id } : {} });
  }

  protected problemText(problem: ProblemDetails | null): string {
    return problem === null ? this.i18n.t('registration.saveError') : problemMessage(this.i18n, 'registration', problem);
  }

  private async saveNew(confirm: boolean): Promise<void> {
    let created = await this.facade.register.execute(confirm);
    if (created === null && this.facade.register.problem()?.code === 'REG-102') {
      const again = await this.confirm.confirm({ title: this.i18n.t('registration.frequency.title'), message: this.problemText(this.facade.register.problem()), confirmLabel: this.i18n.t('registration.frequency.confirm') });
      created = again ? await this.facade.register.execute(true) : null;
      if (!again) {
        return;
      }
    }

    if (created === null) {
      this.toast.error(this.problemText(this.facade.register.problem()));
      return;
    }

    const patient = this.facade.patient();
    const amount = this.facade.paymentAmount();
    let payment: PaymentCreated | null = null;
    if (this.facade.canPay() && amount > 0) {
      payment = await this.facade.pay.execute(created.id);
    }

    this.success.set({ visit: created, patient, payment, paymentProblem: payment === null ? this.facade.pay.problem() : null, amount });
    this.successOpen.set(true);
    await this.autoPrint(created, payment);
  }

  private async saveEdit(confirm: boolean): Promise<void> {
    const visitId = this.facade.visitId();
    if (visitId === null) {
      return;
    }

    let changed = false;
    if (this.facade.lines().length > 0) {
      let added = await this.facade.addLines.execute({ visitId, confirm });
      if (added === null && this.facade.addLines.problem()?.code === 'REG-102') {
        const again = await this.confirm.confirm({ title: this.i18n.t('registration.frequency.title'), message: this.problemText(this.facade.addLines.problem()), confirmLabel: this.i18n.t('registration.frequency.confirm') });
        added = again ? await this.facade.addLines.execute({ visitId, confirm: true }) : null;
      }

      if (added === null) {
        this.toast.error(this.problemText(this.facade.addLines.problem()));
        return;
      }

      changed = true;
      this.facade.lines.set([]);
      if (added.newTubes > 0 && (this.facade.settings.value()?.autoPrintLabels ?? false)) {
        await this.printer.labels(visitId).catch(() => this.toast.error(this.i18n.t('registration.printError')));
      }
    }

    if (this.facade.detailsChanged()) {
      if ((await this.facade.updateDetails.execute(visitId)) === null) {
        this.toast.error(this.problemText(this.facade.updateDetails.problem()));
        return;
      }

      changed = true;
    }

    if (this.facade.discountChanged()) {
      if ((await this.facade.setDiscount.execute(visitId)) === null) {
        this.toast.error(this.problemText(this.facade.setDiscount.problem()));
        return;
      }

      changed = true;
    }

    this.facade.visit.reload();
    this.adoptedVisit = null;
    this.toast.success(this.i18n.t(changed ? 'registration.savedChanges' : 'registration.nothingChanged'));
  }

  private async autoPrint(created: VisitRegisteredResponse, payment: PaymentCreated | null): Promise<void> {
    const settings = this.facade.settings.value();
    try {
      if ((settings?.autoPrintLabels ?? false) && created.tubeCount > 0) {
        await this.printer.labels(created.id);
      }

      if (settings?.autoPrintJobOrder ?? false) {
        await this.printer.jobOrder(created.id);
      }

      if ((settings?.autoPrintReceipt ?? false) && payment !== null) {
        await this.printer.receipt(payment.id);
      }
    } catch {
      this.toast.error(this.i18n.t('registration.printError'));
    }
  }

  protected canRelease(): boolean {
    return this.auth.hasPermission(REGISTRATION_PERMISSIONS.edit) && this.facade.saved()?.status === 'Open';
  }
}
