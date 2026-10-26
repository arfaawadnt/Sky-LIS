import type {
  LabTrackerAttention,
  LabTrackerBalanceStatus,
  LabTrackerDelayState,
  LabTrackerItem,
  LabTrackerNextStep,
  LabTrackerStage,
} from '../../../core/api/tracker.contracts';
import { TRACKER_LINKS } from '../../../core/api/tracker.contracts';
import type { I18nService } from '../../../core/i18n/i18n.service';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';
import type { IconName } from '../../../shared/ui/icon/icon.component';

/** A status as the tracker shows it everywhere: icon + text + colour (never colour alone). */
export interface StatusLook {
  readonly tone: BadgeTone;
  readonly icon: IconName;
  readonly text: string;
  readonly title?: string;
}

/** The module permissions behind the sections and the next-step pages. */
export const TRACKER_PERMISSIONS = {
  view: 'registration.view',
  samples: 'samples.view',
  results: 'results.view',
  billing: 'billing.view',
} as const;

function locale(lang: string): string {
  return lang === 'ar' ? 'ar-EG' : 'en-GB';
}

/** A count in the page language's digits. */
export function num(value: number, lang: string): string {
  return new Intl.NumberFormat(locale(lang), { maximumFractionDigits: 0 }).format(value);
}

/** An amount with two decimals in the page language's digits. */
export function money(value: number, lang: string): string {
  return new Intl.NumberFormat(locale(lang), { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);
}

/** The local date and time of an instant (`26/10/2026, 09:42`), as the worklist shows its times. */
export function dateTime(iso: string, lang: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : new Intl.DateTimeFormat(locale(lang), { dateStyle: 'short', timeStyle: 'short' }).format(date);
}

/** The local time of an instant (`09:42`). */
export function clockTime(iso: string, lang: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : new Intl.DateTimeFormat(locale(lang), { timeStyle: 'short' }).format(date);
}

/** The name in the page language: the Arabic one in Arabic when there is one, else the English one. */
export function pickName(en: string | null, ar: string | null, lang: string): string | null {
  return lang === 'ar' && ar ? ar : en;
}

/** Today as `yyyy-MM-dd` in the browser's zone (the day picker's value when no day is chosen). */
export function localToday(now: Date = new Date()): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

export const STAGE_ICONS: Readonly<Record<LabTrackerStage, IconName>> = {
  Cancelled: 'x',
  AwaitingSample: 'barcode',
  InLab: 'flask',
  AwaitingVerification: 'eye',
  Ready: 'check',
  Delivered: 'printer',
};

export const ATTENTION_ICONS: Readonly<Record<LabTrackerAttention, IconName>> = {
  Delayed: 'alert',
  Panic: 'zap',
  Rejected: 'x',
  ReadyUnpaid: 'coins',
};

export const NEXT_STEP_ICONS: Readonly<Record<LabTrackerNextStep, IconName>> = {
  CollectSample: 'barcode',
  RecollectSample: 'refresh',
  EnterResults: 'edit',
  ReviewResults: 'eye',
  VerifyResults: 'check',
  TakePayment: 'coins',
  PrintReport: 'printer',
  None: 'check',
};

/** The page that does a next step, with the permission that page needs (CR-006 §3). */
export interface NextStepLink {
  readonly path: string;
  readonly queryParams: Readonly<Record<string, string>>;
  readonly permission: string;
}

export function nextStepLink(item: Pick<LabTrackerItem, 'nextStep' | 'visitId' | 'accession'>): NextStepLink | null {
  switch (item.nextStep) {
    case 'CollectSample':
    case 'RecollectSample':
      return { path: TRACKER_LINKS.accession, queryParams: { visitId: item.visitId }, permission: TRACKER_PERMISSIONS.view };
    case 'EnterResults':
    case 'ReviewResults':
    case 'VerifyResults':
    case 'PrintReport':
      return { path: TRACKER_LINKS.results, queryParams: { accession: item.accession }, permission: TRACKER_PERMISSIONS.results };
    case 'TakePayment':
      return { path: TRACKER_LINKS.payments, queryParams: { visitId: item.visitId }, permission: TRACKER_PERMISSIONS.billing };
    case 'None':
      return null;
  }
}

/** Duration of a number of minutes: `1 h 20 min` / `35 min`. */
export function duration(minutes: number, i18n: I18nService): string {
  const lang = i18n.lang();
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours === 0) {
    return i18n.tf('tracker.time.minutes', { m: num(rest, lang) });
  }

  return rest === 0 ? i18n.tf('tracker.time.hours', { h: num(hours, lang) }) : i18n.tf('tracker.time.hoursMinutes', { h: num(hours, lang), m: num(rest, lang) });
}

/** The time-left pill: `1 h 20 min left` (on time / warning), `35 min late`, `Done`; null without a clock. */
export function timeLeftLook(minutesLeft: number | null, delayState: LabTrackerDelayState | null, i18n: I18nService): StatusLook | null {
  if (delayState === 'Done') {
    return { tone: 'neutral', icon: 'check', text: i18n.t('tracker.time.done') };
  }

  if (minutesLeft === null) {
    return null;
  }

  if (minutesLeft < 0 || delayState === 'Late') {
    return { tone: 'bad', icon: 'alert', text: i18n.tf('tracker.time.late', { time: duration(Math.abs(minutesLeft), i18n) }) };
  }

  const text = i18n.tf('tracker.time.left', { time: duration(minutesLeft, i18n) });
  return delayState === 'Warning' ? { tone: 'warn', icon: 'alert', text } : { tone: 'ok', icon: 'check', text };
}

/** The table row's edge colour. */
export function delayEdge(delayState: LabTrackerDelayState | null, minutesLeft: number | null): string | null {
  if (delayState === 'Late' || (minutesLeft !== null && minutesLeft < 0 && delayState !== 'Done')) {
    return 'edge-late';
  }

  return delayState === 'Warning' ? 'edge-warn' : delayState === 'OnTime' ? 'edge-ok' : null;
}

export function samplesLook(item: LabTrackerItem, i18n: I18nService): StatusLook | null {
  const samples = item.samples;
  if (samples === null) {
    return null;
  }

  const lang = i18n.lang();
  if (samples.total === 0) {
    return { tone: 'neutral', icon: 'barcode', text: i18n.t('tracker.samples.none') };
  }

  if (samples.rejected > 0) {
    return { tone: 'bad', icon: 'x', text: i18n.tc('tracker.samples.rejected', samples.rejected, { count: num(samples.rejected, lang) }), ...(samples.firstRejectionReason ? { title: pickName(samples.firstRejectionReason, samples.firstRejectionReasonAr, lang) ?? '' } : {}) };
  }

  if (samples.ordered > 0) {
    return { tone: 'warn', icon: 'barcode', text: i18n.tf('tracker.samples.toCollect', { done: num(samples.total - samples.ordered, lang), total: num(samples.total, lang) }) };
  }

  if (samples.received >= samples.total) {
    return { tone: 'ok', icon: 'check', text: i18n.t('tracker.samples.allReceived') };
  }

  return { tone: 'violet', icon: 'flask', text: i18n.tf('tracker.samples.received', { done: num(samples.received, lang), total: num(samples.total, lang) }) };
}

/** The "ext" tag (LIS-CR-008 C-02): a live sample of the visit was collected outside the laboratory; null otherwise or without `samples.view`. */
export function outsideLook(item: LabTrackerItem, i18n: I18nService): StatusLook | null {
  const outside = item.samples?.collectedOutside ?? 0;
  return outside > 0 ? { tone: 'info', icon: 'info', text: i18n.t('tracker.samples.ext'), title: i18n.t('tracker.samples.extTitle') } : null;
}

export function resultsLook(item: LabTrackerItem, i18n: I18nService): StatusLook | null {
  const results = item.results;
  if (results === null) {
    return null;
  }

  const lang = i18n.lang();
  if (results.total === 0) {
    return { tone: 'neutral', icon: 'flask', text: i18n.t(results.hidden > 0 ? 'tracker.results.allHidden' : 'tracker.results.none') };
  }

  if (results.printed >= results.total) {
    return { tone: 'neutral', icon: 'printer', text: i18n.t('tracker.results.printed') };
  }

  const signed = results.verified + results.printed;
  if (signed >= results.total) {
    return { tone: 'ok', icon: 'check', text: i18n.t('tracker.results.allVerified') };
  }

  if (results.ordered > 0) {
    return { tone: 'info', icon: 'edit', text: i18n.tf('tracker.results.entered', { done: num(results.total - results.ordered, lang), total: num(results.total, lang) }) };
  }

  return { tone: 'violet', icon: 'eye', text: i18n.tf('tracker.results.verified', { done: num(signed, lang), total: num(results.total, lang) }) };
}

const BALANCE_LOOK: Readonly<Record<LabTrackerBalanceStatus, { tone: BadgeTone; icon: IconName }>> = {
  Paid: { tone: 'ok', icon: 'check' },
  Partial: { tone: 'warn', icon: 'coins' },
  Unpaid: { tone: 'bad', icon: 'coins' },
  RefundDue: { tone: 'warn', icon: 'refresh' },
};

export function paymentLook(item: LabTrackerItem, i18n: I18nService): StatusLook | null {
  const payment = item.payment;
  if (payment === null) {
    return null;
  }

  const look = (BALANCE_LOOK as Readonly<Record<string, { tone: BadgeTone; icon: IconName } | undefined>>)[payment.status] ?? { tone: 'neutral', icon: 'coins' };
  const status = i18n.t(`tracker.payment.${payment.status}`);
  const text = payment.remaining > 0 && payment.status !== 'RefundDue' ? i18n.tf('tracker.payment.due', { status, amount: money(payment.remaining, i18n.lang()) }) : status;
  return { ...look, text };
}

/** The one flag of a board card, most serious first: Panic, Rejected, Collect first (payment before the next step). */
export function cardFlag(item: LabTrackerItem, i18n: I18nService): StatusLook | null {
  if (item.results !== null && item.results.panic > 0) {
    return { tone: 'bad', icon: 'zap', text: i18n.t('tracker.flag.panic'), ...(item.results.firstPanicTest ? { title: pickName(item.results.firstPanicTest, item.results.firstPanicTestAr, i18n.lang()) ?? '' } : {}) };
  }

  if (item.samples !== null && item.samples.rejected > 0) {
    return { tone: 'bad', icon: 'x', text: i18n.t('tracker.flag.rejected'), ...(item.samples.firstRejectionReason ? { title: pickName(item.samples.firstRejectionReason, item.samples.firstRejectionReasonAr, i18n.lang()) ?? '' } : {}) };
  }

  return item.nextStep === 'TakePayment' ? { tone: 'warn', icon: 'coins', text: i18n.t('tracker.flag.collectFirst') } : null;
}

/** The state of one segment of the four-segment progress bar. */
export type ProgressState = 'done' | 'partial' | 'todo' | 'bad' | 'unknown';

export interface ProgressSegment {
  readonly part: 'registration' | 'samples' | 'results' | 'payment';
  readonly state: ProgressState;
}

export function progressOf(item: LabTrackerItem): readonly ProgressSegment[] {
  const live = item.registration.lines - item.registration.cancelledLines;
  return [
    { part: 'registration', state: live > 0 ? 'done' : 'bad' },
    { part: 'samples', state: samplesProgress(item) },
    { part: 'results', state: resultsProgress(item) },
    { part: 'payment', state: paymentProgress(item) },
  ];
}

function samplesProgress(item: LabTrackerItem): ProgressState {
  const samples = item.samples;
  if (samples === null) {
    return 'unknown';
  }

  if (samples.rejected > 0) {
    return 'bad';
  }

  return samples.ordered === 0 ? 'done' : samples.ordered < samples.total ? 'partial' : 'todo';
}

function resultsProgress(item: LabTrackerItem): ProgressState {
  const results = item.results;
  if (results === null) {
    return 'unknown';
  }

  if (results.total === 0) {
    return 'todo';
  }

  const signed = results.verified + results.printed;
  return signed >= results.total ? 'done' : signed > 0 || results.ordered < results.total ? 'partial' : 'todo';
}

function paymentProgress(item: LabTrackerItem): ProgressState {
  const payment = item.payment;
  if (payment === null) {
    return 'unknown';
  }

  return payment.status === 'Paid' ? 'done' : payment.status === 'Unpaid' ? 'todo' : 'partial';
}

/** The accessible name of the progress bar: "Registration: done · Samples: partly · …". */
export function progressLabel(segments: readonly ProgressSegment[], i18n: I18nService): string {
  const parts = segments.map((segment) => i18n.tf('tracker.progress.part', { part: i18n.t(`tracker.progress.${segment.part}`), state: i18n.t(`tracker.progress.state.${segment.state}`) }));
  return i18n.tf('tracker.progress.label', { parts: parts.join(' · ') });
}

/** The patient's name in the page language; a masked VIP file reads the dictionary's "VIP patient". */
export function patientName(item: LabTrackerItem, i18n: I18nService): string {
  return item.patientMasked ? i18n.t('tracker.vipMasked') : (pickName(item.patientName, item.patientNameAr, i18n.lang()) ?? item.patientName);
}

/** The server's age (`34y`, `5m`, `12d`) in the page language and digits; anything else as given. */
export function ageText(text: string, i18n: I18nService): string {
  const match = /^(\d+)\s*([ymd])$/.exec(text.trim());
  return match ? i18n.tf(`tracker.age.${match[2] ?? 'y'}`, { n: num(Number(match[1]), i18n.lang()) }) : text;
}

/** The payer and the tier in the page language: "Cash · Gold". */
export function payerLine(item: LabTrackerItem, lang: string): string | null {
  const parts = [pickName(item.payerName, item.payerNameAr, lang), pickName(item.tierName, item.tierNameAr, lang)].filter((part): part is string => !!part);
  return parts.length === 0 ? null : parts.join(' · ');
}

/** "Mona Adel · F · 34 y". */
export function patientLine(item: LabTrackerItem, i18n: I18nService): string {
  const parts = [patientName(item, i18n)];
  if (item.gender) {
    const key = `tracker.gender.${item.gender}`;
    parts.push(i18n.has(key) ? i18n.t(key) : item.gender);
  }

  if (item.ageText) {
    parts.push(ageText(item.ageText, i18n));
  }

  return parts.join(' · ');
}
