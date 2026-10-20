import { Injectable, computed, inject, resource, signal } from '@angular/core';

import type { PaymentMethod, PaymentMethodOption } from '../../../core/api/billing.contracts';
import type { PatientSummary } from '../../../core/api/patients.contracts';
import type { TierOption } from '../../../core/api/pricing.contracts';
import type {
  CheckOrderRequest,
  DiscountKind,
  OrderCheckResult,
  OrderLinePreview,
  OrderWarning,
  PreparationAnswerRequest,
  RegisterVisitRequest,
  VisitDetail,
  VisitDiscountRequest,
} from '../../../core/api/registration.contracts';
import { AuthStore } from '../../../core/auth/auth.store';
import { mutation } from '../../../core/state/mutation';
import { RegistrationPaymentMethodsApi } from '../data/payment-methods.api';
import { RegistrationApi } from '../data/registration.api';
import { discountAmount, discountPercent, round2, utcOffsetMinutes } from '../data/registration-format';

/** A service the clerk typed (the planner adds rule and diagnosis services on the server). */
export interface DraftLine {
  readonly serviceId: string;
  readonly code: string;
  readonly nameEn: string;
  readonly nameAr: string;
  readonly reserved: boolean;
}

export interface VisitHeaderState {
  readonly tierId: string;
  readonly doctorId: string;
  readonly diagnosisId: string;
  readonly treatmentNo: string;
  readonly insuranceId: string;
  readonly urgent: boolean;
  readonly biohazard: boolean;
  readonly collectedOutside: boolean;
}

export interface DiscountState {
  readonly kind: DiscountKind;
  readonly value: number | null;
  readonly reasonId: string;
}

export interface PaymentState {
  /** The kind (the money semantics); with definitions (CP-30b, `selectMethod`) it follows the chosen one. */
  readonly method: PaymentMethod;
  /** null = the amount due. */
  readonly amount: number | null;
  readonly reference: string;
}

/** Why the page cannot save yet (each maps to `registration.validation.<key>`). */
export type SaveBlocker = 'patient' | 'contract' | 'lines' | 'issues' | 'checking' | 'doctor' | 'discountReason' | 'discountCap' | 'paymentReference' | 'paymentAmount';

const EMPTY_HEADER: VisitHeaderState = { tierId: '', doctorId: '', diagnosisId: '', treatmentNo: '', insuranceId: '', urgent: false, biohazard: false, collectedOutside: false };
const EMPTY_DISCOUNT: DiscountState = { kind: 'Percent', value: null, reasonId: '' };
const EMPTY_PAYMENT: PaymentState = { method: 'Cash', amount: null, reference: '' };

/**
 * State of the registration page (CP-06, WF-02; 01 §6.3): reference lists through `resource()`, the visit being typed
 * in signals, the server dry run (`POST …/visits/check`) re-run whenever the patient, contract, diagnosis or services
 * change, and the writes through `mutation()`. In edit mode (`visitId`) the saved visit is loaded and only added
 * services, released services, details and the discount are sent; the R1 payment panel is for new visits only (BR-26).
 */
@Injectable()
export class RegistrationFacade {
  private readonly api = inject(RegistrationApi);
  private readonly paymentMethods = inject(RegistrationPaymentMethodsApi);
  private readonly auth = inject(AuthStore);

  readonly visitId = signal<string | null>(null);
  readonly patient = signal<PatientSummary | null>(null);
  readonly header = signal<VisitHeaderState>(EMPTY_HEADER);
  readonly lines = signal<readonly DraftLine[]>([]);
  readonly answers = signal<Readonly<Record<string, string>>>({});
  readonly discount = signal<DiscountState>(EMPTY_DISCOUNT);
  readonly payment = signal<PaymentState>(EMPTY_PAYMENT);

  readonly settings = resource({ loader: ({ abortSignal }) => this.api.settings(abortSignal) });
  readonly tiers = resource({ loader: ({ abortSignal }) => this.api.tiers(abortSignal) });
  readonly doctors = resource({ params: () => ({ branch: this.auth.currentBranch() }), loader: ({ params, abortSignal }) => this.api.doctors(params.branch, abortSignal) });
  readonly diagnoses = resource({ loader: ({ abortSignal }) => this.api.diagnoses(abortSignal) });
  readonly discountReasons = resource({ loader: ({ abortSignal }) => this.api.reasons('Discount', abortSignal) });
  /** The laboratory's active payment methods (CP-30b, change request 2026-10-19), read for new visits only (the panel is for new visits). */
  readonly methods = resource({
    params: () => (this.visitId() === null ? { newVisit: true } : undefined),
    loader: ({ abortSignal }) => this.paymentMethods.options(abortSignal),
  });

  readonly visit = resource({
    params: () => this.visitId() ?? undefined,
    loader: ({ params, abortSignal }) => this.api.get(params, abortSignal),
  });

  readonly isEdit = computed(() => this.visitId() !== null);

  /** The saved visit when editing (null while it loads). */
  readonly saved = computed<VisitDetail | null>(() => (this.visit.hasValue() ? this.visit.value() : null));

  readonly tierOptions = computed<readonly TierOption[]>(() => (this.tiers.hasValue() ? this.tiers.value() : []));

  readonly tier = computed<TierOption | null>(() => this.tierOptions().find((tier) => tier.id === this.header().tierId) ?? null);

  /** The contract used for pricing: the saved visit's in edit mode, else the chosen tier. */
  readonly contract = computed<{ payerId: string; tierId: string } | null>(() => {
    const saved = this.saved();
    if (saved !== null) {
      return { payerId: saved.contract.payerId, tierId: saved.contract.contractTierId };
    }

    const tier = this.tier();
    return tier === null ? null : { payerId: tier.payerId, tierId: tier.id };
  });

  readonly checkRequest = computed<CheckOrderRequest | undefined>(() => {
    const contract = this.contract();
    const lines = this.lines();
    const diagnosisId = this.isEdit() ? null : this.header().diagnosisId || null;
    if (contract === null || (lines.length === 0 && diagnosisId === null)) {
      return undefined;
    }

    return {
      visitId: this.visitId(),
      patientId: this.patient()?.id ?? this.saved()?.patient.id ?? null,
      payerId: contract.payerId,
      contractTierId: contract.tierId,
      diagnosisId,
      lines: lines.map((line) => ({ serviceId: line.serviceId, reserved: line.reserved })),
      utcOffsetMinutes: utcOffsetMinutes(),
    };
  });

  readonly check = resource({
    params: () => this.checkRequest(),
    loader: ({ params, abortSignal }) => this.api.check(params, abortSignal),
  });

  readonly result = computed<OrderCheckResult | null>(() => (this.checkRequest() !== undefined && this.check.hasValue() ? this.check.value() : null));

  readonly plannedLines = computed<readonly OrderLinePreview[]>(() => this.result()?.lines ?? []);

  readonly warnings = computed<readonly OrderWarning[]>(() => this.result()?.warnings ?? []);

  readonly frequencyAlerts = computed(() => this.warnings().filter((warning) => warning.code === 'REG-201'));

  /** The base of the discount: the gross patient share of the new lines (new visit) or of the saved visit's live lines plus the new ones (edit). */
  readonly discountBase = computed(() => {
    const planned = this.result()?.totals.patientShareTotal ?? 0;
    const saved = this.saved();
    const savedGross = saved === null ? 0 : saved.lines.filter((line) => line.status !== 'Cancelled' && line.kind !== 'PackageMember').reduce((sum, line) => sum + line.patientShare + line.discount, 0);
    return round2(planned + savedGross);
  });

  readonly discountTotal = computed(() => {
    const discount = this.discount();
    return discount.value === null ? 0 : discountAmount(discount.kind, discount.value, this.discountBase());
  });

  readonly totals = computed(() => {
    const result = this.result();
    const saved = this.saved();
    const live = saved?.lines.filter((line) => line.status !== 'Cancelled') ?? [];
    const list = round2((result?.totals.totalListPrice ?? 0) + live.reduce((sum, line) => sum + line.listPrice, 0));
    const payer = round2((result?.totals.payerShareTotal ?? 0) + live.reduce((sum, line) => sum + line.payerShare, 0));
    const patient = this.discountBase();
    const discount = this.discountTotal();
    const due = round2(patient - discount);
    const paid = saved?.balance?.paid ?? 0;
    // Refunds return money already paid (UAT R3-01): what the laboratory holds is paid − refunded, as the server balance counts it.
    const refunded = saved?.balance?.refunded ?? 0;
    return { list, payer, patient, discount, due, paid, refunded, remaining: round2(due - (paid - refunded)) };
  });

  /** The definitions the panel offers in pick-list order; empty = the four kinds with the legacy rule. */
  readonly methodOptions = computed<readonly PaymentMethodOption[]>(() => this.methods.value() ?? []);

  /** The chosen definition's code (CP-30b); '' = the first offered one. */
  readonly methodCode = signal('');

  /** The chosen definition (the first one until the cashier picks another), or null without definitions. */
  readonly selectedMethod = computed<PaymentMethodOption | null>(() => {
    const options = this.methodOptions();
    const code = this.methodCode();
    return options.find((option) => option.code === code) ?? options[0] ?? null;
  });

  /** BIL-003 as the panel knows it: the definition's rule, or "every kind but cash" without definitions (BR-26). */
  readonly referenceRequired = computed(() => {
    const selected = this.selectedMethod();
    return selected === null ? this.payment().method !== 'Cash' : selected.requiresReference;
  });

  /**
   * The amount the payment panel records (the due amount unless typed), never negative. The cashier is the session user and the
   * branch the session branch (change request LIS-CR-004, 2026-10-20: no treasury), so the panel is offered whenever there is
   * something to pay.
   */
  readonly paymentAmount = computed(() => {
    const typed = this.payment().amount;
    return round2(Math.max(0, typed === null ? this.totals().due : typed));
  });

  readonly canPay = computed(() => !this.isEdit() && this.auth.hasPermission('billing.create') && (this.settings.value()?.canPay ?? false));

  readonly blockers = computed<readonly SaveBlocker[]>(() => {
    const blockers: SaveBlocker[] = [];
    const settings = this.settings.value();
    const edit = this.isEdit();
    if (!edit && this.patient() === null) {
      blockers.push('patient');
    }

    if (this.contract() === null) {
      blockers.push('contract');
    }

    if (!edit && this.lines().length === 0 && this.header().diagnosisId === '') {
      blockers.push('lines');
    }

    if (this.checkRequest() !== undefined && this.check.isLoading()) {
      blockers.push('checking');
    }

    if ((this.result()?.issues.length ?? 0) > 0) {
      blockers.push('issues');
    }

    if ((settings?.mandatoryDoctor ?? false) && this.header().doctorId === '') {
      blockers.push('doctor');
    }

    const discount = this.discount();
    if (discount.value !== null && discount.value > 0) {
      if (discount.reasonId === '') {
        blockers.push('discountReason');
      }

      if (discountPercent(this.discountTotal(), this.discountBase()) > (settings?.discountCapPercent ?? 0) + 1e-9) {
        blockers.push('discountCap');
      }
    }

    if (this.canPay() && this.paymentAmount() > 0) {
      const payment = this.payment();
      if (this.referenceRequired() && payment.reference.trim() === '') {
        blockers.push('paymentReference');
      }

      if (this.paymentAmount() > this.totals().due + 1e-9) {
        blockers.push('paymentAmount');
      }
    }

    return blockers;
  });

  readonly register = mutation((confirm: boolean) => this.api.register(this.registerRequest(confirm)));

  readonly addLines = mutation((input: { visitId: string; confirm: boolean }) =>
    this.api.addLines(input.visitId, {
      lines: this.lines().map((line) => ({ serviceId: line.serviceId, reserved: line.reserved })),
      preparationAnswers: this.answerRequests(),
      confirmFrequencyWarnings: input.confirm,
    }),
  );

  readonly release = mutation((input: { visitId: string; lineIds: readonly string[] }) => this.api.release(input.visitId, input.lineIds), { then: () => this.visit.reload() });

  readonly updateDetails = mutation((visitId: string) => {
    const header = this.header();
    return this.api.updateDetails(visitId, {
      treatmentNo: blank(header.treatmentNo),
      referringDoctorId: blank(header.doctorId),
      insuranceId: blank(header.insuranceId),
      urgent: header.urgent,
      biohazard: header.biohazard,
      collectedOutside: header.collectedOutside,
    });
  });

  readonly setDiscount = mutation((visitId: string) => this.api.setDiscount(visitId, { discount: this.discountRequest(), reason: null }));

  readonly pay = mutation((visitId: string) => {
    const payment = this.payment();
    const selected = this.selectedMethod();
    return this.api.pay({
      visitId,
      amount: this.paymentAmount(),
      method: selected?.kind ?? payment.method,
      ...(selected === null ? {} : { methodCode: selected.code }),
      reference: this.referenceRequired() ? blank(payment.reference) : null,
    });
  });

  /** Edit mode: the page opens a saved visit. */
  open(visitId: string | null): void {
    this.visitId.set(visitId);
    this.reset(false);
  }

  /** Copies the saved visit's editable data into the page state (after it loaded). */
  adopt(detail: VisitDetail): void {
    this.header.set({
      tierId: detail.contract.contractTierId,
      doctorId: detail.referringDoctorId ?? '',
      diagnosisId: detail.diagnosisId ?? '',
      treatmentNo: detail.treatmentNo ?? '',
      insuranceId: detail.insuranceId ?? '',
      urgent: detail.urgent,
      biohazard: detail.biohazard,
      collectedOutside: detail.collectedOutside,
    });
    this.discount.set(detail.discountKind === null ? EMPTY_DISCOUNT : { kind: detail.discountKind, value: detail.discountValue, reasonId: detail.discountReasonId ?? '' });
    this.answers.set(Object.fromEntries(detail.preparationAnswers.map((answer) => [answer.preparationId, answer.answer])));
  }

  /** The default contract: the cash payer's default tier, else the first active tier. */
  defaultTier(): void {
    if (this.header().tierId !== '') {
      return;
    }

    const options = this.tierOptions();
    const preferred = options.find((tier) => tier.isDefault && tier.payerType === 'Cash') ?? options.find((tier) => tier.isDefault) ?? options[0];
    if (preferred) {
      this.patchHeader({ tierId: preferred.id });
    }
  }

  setPatient(patient: PatientSummary | null): void {
    this.patient.set(patient);
  }

  patchHeader(change: Partial<VisitHeaderState>): void {
    this.header.update((header) => ({ ...header, ...change }));
  }

  patchDiscount(change: Partial<DiscountState>): void {
    this.discount.update((discount) => ({ ...discount, ...change }));
  }

  patchPayment(change: Partial<PaymentState>): void {
    this.payment.update((payment) => ({ ...payment, ...change }));
  }

  /** The cashier picks one of the laboratory's methods (CP-30b): its kind becomes the payment's kind. */
  selectMethod(option: PaymentMethodOption): void {
    this.methodCode.set(option.code);
    this.payment.update((payment) => ({ ...payment, method: option.kind }));
  }

  /** Adds typed services that are not on the draft yet; returns the codes that were already there. */
  addServices(services: readonly Omit<DraftLine, 'reserved'>[]): readonly string[] {
    const present = new Set(this.lines().map((line) => line.serviceId));
    const saved = new Set((this.saved()?.lines ?? []).filter((line) => line.status !== 'Cancelled').map((line) => line.serviceId));
    const duplicates = services.filter((service) => present.has(service.serviceId) || saved.has(service.serviceId)).map((service) => service.code);
    const fresh = services.filter((service, index) => !present.has(service.serviceId) && !saved.has(service.serviceId) && services.findIndex((other) => other.serviceId === service.serviceId) === index);
    if (fresh.length > 0) {
      this.lines.update((lines) => [...lines, ...fresh.map((service) => ({ ...service, reserved: false }))]);
    }

    return duplicates;
  }

  removeLine(serviceId: string): void {
    this.lines.update((lines) => lines.filter((line) => line.serviceId !== serviceId));
  }

  setReserved(serviceId: string, reserved: boolean): void {
    this.lines.update((lines) => lines.map((line) => (line.serviceId === serviceId ? { ...line, reserved } : line)));
  }

  setAnswer(preparationId: string, answer: string): void {
    this.answers.update((answers) => ({ ...answers, [preparationId]: answer }));
  }

  /**
   * Re-reads the parameters and rights, the reference lists, the payment methods, the saved visit and the dry run when the page
   * opens again (what was typed stays): the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14, A-01). A load in flight is left alone.
   */
  refresh(): void {
    this.settings.reload();
    this.tiers.reload();
    this.doctors.reload();
    this.diagnoses.reload();
    this.discountReasons.reload();
    this.methods.reload();
    this.visit.reload();
    this.check.reload();
  }

  /** Clears the visit being typed; `keepContract` keeps the chosen tier for the next patient. */
  reset(keepContract = true): void {
    const tierId = keepContract ? this.header().tierId : '';
    this.patient.set(null);
    this.header.set({ ...EMPTY_HEADER, tierId });
    this.lines.set([]);
    this.answers.set({});
    this.discount.set(EMPTY_DISCOUNT);
    this.payment.set(EMPTY_PAYMENT);
    this.methodCode.set('');
    this.register.reset();
    this.addLines.reset();
    this.pay.reset();
  }

  registerRequest(confirm: boolean): RegisterVisitRequest {
    const header = this.header();
    const contract = this.contract();
    return {
      patientId: this.patient()?.id ?? '',
      treatmentNo: blank(header.treatmentNo),
      referringDoctorId: blank(header.doctorId),
      payerId: contract?.payerId ?? '',
      contractTierId: contract?.tierId ?? '',
      insuranceId: blank(header.insuranceId),
      diagnosisId: blank(header.diagnosisId),
      bookingId: null,
      urgent: header.urgent,
      biohazard: header.biohazard,
      collectedOutside: header.collectedOutside,
      lines: this.lines().map((line) => ({ serviceId: line.serviceId, reserved: line.reserved })),
      preparationAnswers: this.answerRequests(),
      discount: this.discountRequest(),
      confirmFrequencyWarnings: confirm,
      utcOffsetMinutes: utcOffsetMinutes(),
    };
  }

  /** True when the edit page changed the details or the discount of the saved visit. */
  detailsChanged(): boolean {
    const saved = this.saved();
    const header = this.header();
    return (
      saved !== null &&
      ((saved.referringDoctorId ?? '') !== header.doctorId ||
        (saved.treatmentNo ?? '') !== header.treatmentNo.trim() ||
        (saved.insuranceId ?? '') !== header.insuranceId.trim() ||
        saved.urgent !== header.urgent ||
        saved.biohazard !== header.biohazard ||
        saved.collectedOutside !== header.collectedOutside)
    );
  }

  discountChanged(): boolean {
    const saved = this.saved();
    const discount = this.discount();
    if (saved === null) {
      return false;
    }

    const active = discount.value !== null && discount.value > 0;
    return active
      ? saved.discountKind !== discount.kind || saved.discountValue !== discount.value || (saved.discountReasonId ?? '') !== discount.reasonId
      : saved.discountKind !== null;
  }

  private discountRequest(): VisitDiscountRequest | null {
    const discount = this.discount();
    return discount.value !== null && discount.value > 0 && discount.reasonId !== '' ? { kind: discount.kind, value: discount.value, reasonId: discount.reasonId } : null;
  }

  private answerRequests(): readonly PreparationAnswerRequest[] | null {
    const applicable = new Set((this.result()?.preparations ?? []).map((question) => question.preparationId));
    const answers = Object.entries(this.answers())
      .filter(([id, answer]) => answer.trim() !== '' && (applicable.size === 0 || applicable.has(id)))
      .map(([preparationId, answer]) => ({ preparationId, answer: answer.trim() }));
    return answers.length === 0 ? null : answers;
  }
}

function blank(value: string): string | null {
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}
