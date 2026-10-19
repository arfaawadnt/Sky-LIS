import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
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
import { count, dateTime, money, pick } from '../data/registration-format';
import { PatientLookupApi } from '../../../shared/patient/patient-lookup.api';
import { toSummary } from '../../../shared/patient/patient-demographics';
import { PatientPickerComponent } from '../../../shared/patient/patient-picker.component';
import { RegistrationFacade, type SaveBlocker } from '../state/registration.facade';
import { RegistrationPrinter } from '../state/registration-printer';
import { type BarProblem, RegistrationActionBarComponent } from '../ui/registration-action-bar.component';
import { type ChecklistItem, RegistrationReadinessComponent } from '../ui/registration-readiness.component';
import { type PickedService, ServiceEntryComponent } from '../ui/service-entry.component';
import { type VisitFlag, VisitFlagsComponent } from '../ui/visit-flags.component';

/** The field each save blocker sends the clerk to (first match wins). */
const BLOCKER_TARGETS: Readonly<Record<SaveBlocker, string | null>> = {
  patient: '#reg-patient input, #reg-patient button',
  contract: '#reg-contract',
  lines: '#reg-service-entry',
  issues: '#reg-services tr.has-issue .icon-btn, #reg-services tr.pending .icon-btn, #reg-service-entry',
  checking: null,
  doctor: '#reg-doctor',
  discountReason: '#reg-discount-reason',
  discountCap: '#reg-discount-value',
  paymentReference: '#reg-pay-reference',
  paymentTreasury: '#reg-pay-treasury',
  paymentAmount: '#reg-pay-amount',
};

interface SuccessState {
  readonly visit: VisitRegisteredResponse;
  readonly patient: PatientSummary | null;
  readonly payment: PaymentCreated | null;
  readonly paymentProblem: ProblemDetails | null;
  readonly amount: number;
}

/**
 * Registration (SRS CP-06, WF-02; Release 1: no location/room, payment captured on the page, BR-26), laid out as a desk
 * console (UX option B): one strip for the patient picker (search, similar-name warning, new file) and the visit data with
 * the flags as toggles; the service entry (suggestions while typing; Enter resolves a code, shortcut number, alias or set;
 * F2 opens the picker); the lines priced and planned by the server dry run (tubes BR-02, expected dates BR-13, frequency
 * BR-12, contract alerts, rules, preparations). Beside them the "Ready to save" checklist and the checkout (totals, the
 * discount folded until needed and kept within the user's cap, the payment panel). At the bottom a sticky bar lists every
 * open item (each jumps to its field, which is marked once a save was tried) with the amount due and Save (F10). After
 * saving, a dialog shows the accession, the payment is posted to Billing and labels, job order and receipt print per the
 * parameters. `/visits/:visitId` opens a saved visit to add or release
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
    RegistrationReadinessComponent,
    RegistrationActionBarComponent,
    VisitFlagsComponent,
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

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
  /** True once a save was tried with something missing: the open fields are then marked. */
  protected readonly attempted = signal(false);
  private readonly discountOpen = signal(false);
  private adoptedVisit: string | null = null;

  protected readonly title = computed(() => {
    const saved = this.facade.saved();
    return this.facade.isEdit() ? this.i18n.tf('registration.editTitle', { accession: saved?.accession ?? '…' }) : this.i18n.t('registration.title');
  });

  /** The visit's branch when editing, else the session's branch from the page settings (validation 2026-10-11: the new-registration header showed none). */
  protected readonly branchName = computed(() => {
    const saved = this.facade.saved();
    const settings = this.facade.settings.value();
    return saved ? pick(saved.branchNameEn, saved.branchNameAr, this.i18n.lang()) : pick(settings?.branchNameEn ?? null, settings?.branchNameAr ?? null, this.i18n.lang());
  });

  protected readonly subtitle = computed(() => {
    const branch = this.branchName();
    const text = this.i18n.t(this.facade.isEdit() ? 'registration.editSubtitle' : 'registration.deskSubtitle');
    return branch === '' ? text : `${text} · ${branch}`;
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

  protected readonly doctorRequired = computed(() => this.facade.settings.value()?.mandatoryDoctor ?? false);

  protected readonly discountActive = computed(() => (this.facade.discount().value ?? 0) > 0);

  /** The discount is folded away until the clerk opens it, a discount is set (also a saved one) or it blocks the save. */
  protected readonly showDiscount = computed(() =>
    this.discountOpen() || this.discountActive() || this.facade.blockers().some((blocker) => blocker === 'discountReason' || blocker === 'discountCap'));

  /** Services of a frequency alert (REG-201), tinted in the lines table. */
  protected readonly warnedServices = computed(() => new Set(this.facade.frequencyAlerts().map((alert) => alert.serviceId ?? '')));

  protected readonly checklist = computed<readonly ChecklistItem[]>(() => {
    const blockers = new Set(this.facade.blockers());
    const edit = this.facade.isEdit();
    const items: ChecklistItem[] = [];
    const add = (key: string, doneLabel: string, keys: readonly SaveBlocker[]): void => {
      const open = keys.find((blocker) => blocker !== 'checking' && blockers.has(blocker));
      if (open !== undefined) {
        items.push({ key, state: 'open', label: this.blockerText(open), blocker: open });
      } else if (keys.includes('checking') && blockers.has('checking')) {
        items.push({ key, state: 'pending', label: this.i18n.t('registration.ready.checking'), blocker: null });
      } else {
        items.push({ key, state: 'done', label: doneLabel, blocker: null });
      }
    };

    if (!edit) {
      add('patient', this.i18n.t('registration.ready.patient'), ['patient']);
    }

    add('contract', this.i18n.t('registration.ready.contract'), ['contract']);
    add('services', edit ? this.i18n.t('registration.ready.servicesEdit') : this.i18n.tf('registration.ready.services', { count: this.num(this.facade.plannedLines().length) }), ['lines', 'issues', 'checking']);
    if (this.doctorRequired()) {
      add('doctor', this.i18n.t('registration.ready.doctor'), ['doctor']);
    }

    if (this.discountActive()) {
      add('discount', this.i18n.t('registration.ready.discount'), ['discountReason', 'discountCap']);
    }

    if (this.facade.canPay() && this.facade.paymentAmount() > 0) {
      add('payment', this.i18n.t('registration.ready.payment'), ['paymentTreasury', 'paymentReference', 'paymentAmount']);
    }

    return items;
  });

  protected readonly barProblems = computed<readonly BarProblem[]>(() => this.facade.blockers().map((blocker) => ({ blocker, text: this.blockerText(blocker) })));

  protected readonly barNotices = computed(() =>
    this.facade.frequencyAlerts().map((alert) => this.i18n.tf('registration.bar.confirmOnSave', { service: alert.serviceCode ?? '' })));

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

  /** Counts and percentages in the digits of the language (UAT A-07). */
  protected num(value: number): string {
    return count(value, this.i18n.lang());
  }

  /** "1 tube was issued." / "3 tubes were issued." in the language's grammar and digits (validation 2026-10-11). */
  protected tubesText(tubes: number): string {
    return this.countText('registration.success.tubes', tubes);
  }

  /** A counted sentence in its grammatical form (`I18nService.tc`, validation v0.5.0 F-11) with the language's digits. */
  protected countText(key: string, value: number): string {
    return this.i18n.tc(key, value, { count: this.num(value) });
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
    // REG-109 names the repeated tests and the services that order them (validation O-3).
    return `${prefix}${this.i18n.has(key) ? this.i18n.tf(key, { detail: issue.detail ?? '' }) : issue.message}`;
  }

  protected warningText(warning: OrderWarning): string {
    const service = warning.serviceCode ?? '';
    const detail = this.i18n.lang() === 'ar' ? (warning.detailAr ?? warning.detail ?? '') : (warning.detail ?? '');
    return this.i18n.tf(`registration.warning.${warning.code}`, {
      service,
      detail,
      date: warning.lastOrderedAtUtc ? this.when(warning.lastOrderedAtUtc) : '',
      accession: warning.lastAccession ?? '',
      days: warning.windowDays === null ? '' : this.countText('registration.dayCount', warning.windowDays),
    });
  }

  protected warningTone(code: string): 'warn' | 'info' | 'bad' {
    return code === 'REG-201' || code === 'REG-207' ? 'bad' : code === 'REG-202' || code === 'REG-203' || code === 'REG-206' ? 'warn' : 'info';
  }

  protected blockerText(blocker: SaveBlocker): string {
    return this.i18n.tf(`registration.validation.${blocker}`, { cap: this.num(this.facade.settings.value()?.discountCapPercent ?? 0) });
  }

  /** Whether a field is marked: only after a save was tried while its blocker is open. */
  protected invalid(blocker: SaveBlocker): boolean {
    return this.attempted() && this.facade.blockers().includes(blocker);
  }

  /** The services card is marked when no service was added or one was refused (UAT A-08). */
  protected servicesInvalid(): boolean {
    return this.invalid('lines') || this.invalid('issues');
  }

  /** The codes of the planned services that ask a preparation question ("" while none of them is planned). */
  protected askedBy(question: PreparationQuestionItem): string {
    const asking = new Set(question.serviceIds);
    return this.facade.plannedLines().filter((line) => asking.has(line.serviceId)).map((line) => line.code).join(', ');
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

  protected toggleFlag(flag: VisitFlag): void {
    this.facade.patchHeader({ [flag]: !this.facade.header()[flag] });
  }

  protected toggleDiscount(): void {
    this.discountOpen.set(!this.showDiscount());
  }

  /** Brings the field of an open item into view and focuses it (the folded discount opens first). */
  protected focusBlocker(blocker: SaveBlocker): void {
    const selector = BLOCKER_TARGETS[blocker];
    if (selector === null) {
      return;
    }

    if (blocker === 'discountReason' || blocker === 'discountCap') {
      this.discountOpen.set(true);
    }

    afterNextRender(() => {
      const target = this.host.nativeElement.querySelector<HTMLElement>(selector);
      if (target === null) {
        return;
      }

      try {
        target.scrollIntoView({ block: 'center', behavior: 'smooth' });
      } catch {
        // jsdom (unit tests) does not scroll; the focus below is what matters
      }

      target.focus({ preventScroll: true });
    }, { injector: this.injector });
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

    this.attempted.set(false);
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
    const first = blockers[0];
    if (first !== undefined) {
      this.attempted.set(true);
      this.toast.error(blockers.length === 1 ? this.blockerText(first) : this.countText('registration.validation.many', blockers.length));
      this.focusBlocker(first);
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
    this.attempted.set(false);
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

  /** The success panel's *Accession details* (CP-10b): the saved visit on the accession page. */
  protected accessionDetails(): void {
    const success = this.success();
    this.successOpen.set(false);
    if (success !== null) {
      void this.router.navigate(['/registration/accession'], { queryParams: { visitId: success.visit.id } });
    }
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

    this.attempted.set(false);
    this.success.set({ visit: created, patient, payment, paymentProblem: payment === null ? this.facade.pay.problem() : null, amount });
    // The draft is spent: the dialog keeps its own copy. The route-provided facade outlives the page, so a draft left here
    // came back as "ready to save" after "Open the visit" / "Payments" / closing the dialog (UAT A-01: duplicate visit).
    this.facade.reset(true);
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
    this.attempted.set(false);
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
