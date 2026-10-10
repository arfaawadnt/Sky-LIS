import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { type AbstractControl, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import { PLATFORM_PERMISSIONS } from '../../../core/api/platform.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import type { SettingValueItem, TenantSettingItem } from '../../../core/api/settings.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ParametersFacade } from '../state/reference.facades';
import { formatDateTime } from '../ui/format';

/** How a parameter is edited: the server's type plus the ranges and closed sets of `SettingKeys`. */
interface ParameterSpec {
  readonly min?: number;
  readonly max?: number;
  readonly options?: readonly string[];
  readonly pattern?: RegExp;
}

const SPECS: Readonly<Record<string, ParameterSpec>> = {
  discountCapPercent: { min: 0, max: 100 },
  labelCopies: { min: 1, max: 10 },
  delayWarnPercent: { min: 0, max: 1000 },
  delayLatePercent: { min: 0, max: 1000 },
  sessionTimeoutMinutes: { min: 1, max: 720 },
  otpLength: { min: 4, max: 10 },
  passwordMinLength: { min: 8, max: 64 },
  trialDays: { min: 0, max: 365 },
  resultLanguage: { options: ['en', 'ar'] },
  defaultLanguage: { options: ['en', 'ar'] },
  masterDataUpdatePolicy: { options: ['push', 'propose', 'ignore'] },
  defaultCurrency: { pattern: /^[A-Za-z]{3}$/ },
};

/** SA-19 first (the platform-wide defaults the SRS names), then the laboratory defaults by area. */
const GROUPS: readonly { readonly id: string; readonly keys: readonly string[] }[] = [
  { id: 'security', keys: ['otpLength', 'passwordMinLength', 'sessionTimeoutMinutes'] },
  { id: 'subscription', keys: ['trialDays', 'defaultCurrency', 'defaultLanguage', 'masterDataUpdatePolicy'] },
  { id: 'thresholds', keys: ['delayWarnPercent', 'delayLatePercent', 'discountCapPercent'] },
  { id: 'registration', keys: ['mandatoryMobile', 'mandatoryDoctor', 'mandatoryNationalId', 'similarNameWarning', 'accessionFormat'] },
  { id: 'printing', keys: ['autoPrintLabels', 'autoPrintJobOrder', 'autoPrintReceipt', 'printUnverified', 'labelCopies', 'resultLanguage'] },
  { id: 'samples', keys: ['autoCollect', 'autoReceive', 'newBarcodeOnReject', 'reviewBeforeVerify'] },
];

type ParameterForm = FormGroup<Record<string, AbstractControl<string | number | boolean | null>>>;

interface ParameterGroupView {
  readonly id: string;
  readonly items: readonly TenantSettingItem[];
}

/**
 * SA-19 Platform parameters: the platform tenant's settings, which are the defaults every laboratory starts from
 * (OTP length, password minimum, session timeout, trial days, currency, language, master-data update policy, delay
 * thresholds, discount cap, and the laboratory defaults of CP-57). Typed controls; only changed keys are saved.
 */
@Component({
  selector: 'app-platform-parameters-page',
  imports: [ReactiveFormsModule, CanDirective, SaveShortcutDirective, PageHeaderComponent, SectionCardComponent, FormFieldComponent, BadgeComponent, IconComponent],
  templateUrl: './parameters.page.html',
  styleUrl: './platform-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ParametersPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(ParametersFacade);
  private readonly toast = inject(ToastService);

  protected readonly permissions = PLATFORM_PERMISSIONS;
  protected readonly form = signal<ParameterForm | null>(null);
  protected readonly saveProblem = signal<ProblemDetails | null>(null);

  protected readonly groups = computed<readonly ParameterGroupView[]>(() => {
    const items = this.facade.items();
    const byKey = new Map(items.map((item) => [item.key, item]));
    const known = new Set(GROUPS.flatMap((group) => group.keys));
    const views = GROUPS.map((group) => ({ id: group.id, items: group.keys.map((key) => byKey.get(key)).filter((item): item is TenantSettingItem => item !== undefined) }));
    const others = items.filter((item) => !known.has(item.key));
    return [...views, ...(others.length > 0 ? [{ id: 'other', items: others }] : [])].filter((group) => group.items.length > 0);
  });

  constructor() {
    void this.i18n.load('platform');
    effect(() => {
      const items = this.facade.items();
      untracked(() => this.form.set(items.length === 0 ? null : this.buildForm(items)));
    });
  }

  protected spec(key: string): ParameterSpec {
    return SPECS[key] ?? {};
  }

  protected label(item: TenantSettingItem): string {
    const key = `platform.parameters.key.${item.key}`;
    return this.i18n.has(key) ? this.i18n.t(key) : item.key;
  }

  protected hint(item: TenantSettingItem): string {
    const key = `platform.parameters.hint.${item.key}`;
    const text = this.i18n.has(key) ? this.i18n.t(key) : item.description;
    return `${text} ${this.i18n.tf('platform.parameters.default', { value: this.display(item, item.defaultValue) })}`;
  }

  protected optionLabel(key: string, option: string): string {
    const text = `platform.parameters.option.${key}.${option}`;
    return this.i18n.has(text) ? this.i18n.t(text) : option;
  }

  protected updated(item: TenantSettingItem): string {
    return formatDateTime(item.updatedAtUtc, this.i18n.lang());
  }

  protected isModified(form: ParameterForm, item: TenantSettingItem): boolean {
    return this.textOf(item, form.controls[item.key]?.value ?? null) !== item.defaultValue;
  }

  protected resetToDefault(form: ParameterForm, item: TenantSettingItem): void {
    const control = form.controls[item.key];
    control?.setValue(this.valueOf(item, item.defaultValue));
    control?.markAsDirty();
  }

  protected reload(): void {
    this.saveProblem.set(null);
    this.facade.settings.reload();
  }

  protected async save(): Promise<void> {
    const form = this.form();
    if (form === null || this.facade.save.pending() || !markAllTouched(form)) {
      return;
    }

    const changes: SettingValueItem[] = [];
    for (const item of this.facade.items()) {
      const text = this.textOf(item, form.controls[item.key]?.value ?? null);
      if (text !== item.value) {
        changes.push({ key: item.key, value: text });
      }
    }

    if (changes.length === 0) {
      this.toast.info(this.i18n.t('platform.parameters.nothingChanged'));
      return;
    }

    this.saveProblem.set(null);
    if ((await this.facade.save.execute(changes)) !== null) {
      this.toast.success(this.i18n.tf('platform.parameters.saved', { count: changes.length }));
    } else {
      this.saveProblem.set(this.facade.save.problem());
    }
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'platform.parameters', problem);
  }

  private display(item: TenantSettingItem, text: string): string {
    if (item.type === 'Bool') {
      return this.i18n.t(text === 'true' ? 'platform.yes' : 'platform.no');
    }

    return this.spec(item.key).options ? this.optionLabel(item.key, text) : text;
  }

  private textOf(item: TenantSettingItem, value: string | number | boolean | null): string {
    if (item.type === 'Bool') {
      return value === true ? 'true' : 'false';
    }

    if (item.type === 'Int') {
      return value === null || value === '' ? '' : String(Math.trunc(Number(value)));
    }

    const text = typeof value === 'string' ? value.trim() : String(value ?? '');
    return item.key === 'defaultCurrency' ? text.toUpperCase() : text;
  }

  private valueOf(item: TenantSettingItem, text: string): string | number | boolean {
    if (item.type === 'Bool') {
      return text === 'true';
    }

    return item.type === 'Int' ? Number.parseInt(text, 10) : text;
  }

  private buildForm(items: readonly TenantSettingItem[]): ParameterForm {
    const controls: Record<string, AbstractControl<string | number | boolean | null>> = {};
    for (const item of items) {
      const spec = this.spec(item.key);
      if (item.type === 'Bool') {
        controls[item.key] = new FormControl<boolean>(item.value === 'true', { nonNullable: true }) as AbstractControl<string | number | boolean | null>;
      } else if (item.type === 'Int') {
        const validators = [Validators.required];
        if (spec.min !== undefined) {
          validators.push(Validators.min(spec.min));
        }

        if (spec.max !== undefined) {
          validators.push(Validators.max(spec.max));
        }

        controls[item.key] = new FormControl<number | null>(Number.parseInt(item.value, 10), { validators }) as AbstractControl<string | number | boolean | null>;
      } else {
        const validators = [Validators.required, Validators.maxLength(200)];
        if (spec.pattern) {
          validators.push(Validators.pattern(spec.pattern));
        }

        controls[item.key] = new FormControl<string>(item.value, { nonNullable: true, validators }) as AbstractControl<string | number | boolean | null>;
      }
    }

    return new FormGroup(controls);
  }
}
