import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';

import type { OrderIssue, PriceQuoteDocumentLine } from '../../../core/api/registration.contracts';
import { REGISTRATION_PERMISSIONS } from '../../../core/api/registration.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { dateTime, money, pick } from '../data/registration-format';
import { PriceQueryFacade } from '../state/price-query.facade';
import { RegistrationPrinter } from '../state/registration-printer';
import { type PickedService, ServiceEntryComponent } from '../ui/service-entry.component';

/**
 * Price query (SRS CP-12, WF-10; LDM §7.2 "Query"): the contract, the services (Enter / F2 as on registration), the
 * priced lines with shares, expected result and preparation, the totals, "print quote" and "continue to registration"
 * (the services and the tier travel in the query string of CP-06). Nothing is saved.
 */
@Component({
  selector: 'app-registration-price-query-page',
  imports: [PageHeaderComponent, SectionCardComponent, BadgeComponent, CanDirective, IconComponent, ServiceEntryComponent],
  templateUrl: './price-query.page.html',
  styleUrl: './price-query.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PriceQueryPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(PriceQueryFacade);
  private readonly printer = inject(RegistrationPrinter);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);

  protected readonly permissions = REGISTRATION_PERMISSIONS;
  protected readonly printing = signal(false);

  protected readonly lines = computed<readonly PriceQuoteDocumentLine[]>(() => this.facade.document()?.lines ?? []);

  /** Typed services the quote has not priced yet (while it loads or when it failed). */
  protected readonly pendingLines = computed(() => {
    const quoted = new Set(this.lines().map((line) => line.serviceId));
    return this.facade.lines().filter((line) => !quoted.has(line.serviceId));
  });

  protected readonly issues = computed<readonly OrderIssue[]>(() => this.facade.document()?.issues ?? []);

  protected readonly currency = computed(() => this.facade.document()?.currency ?? '');

  constructor() {
    void this.i18n.load('registration');

    effect(() => {
      const options = this.facade.tierOptions();
      untracked(() => (options.length > 0 ? this.facade.defaultTier() : undefined));
    });
  }

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

  protected issueText(issue: OrderIssue): string {
    const key = `registration.error.${issue.code}`;
    const prefix = issue.serviceCode ? `${issue.serviceCode}: ` : '';
    return `${prefix}${this.i18n.has(key) ? this.i18n.t(key) : issue.message}`;
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected onGender(event: Event): void {
    const value = this.value(event);
    this.facade.gender.set(value === 'Male' || value === 'Female' ? value : null);
  }

  protected onAdded(services: readonly PickedService[]): void {
    const duplicates = this.facade.addServices(services);
    if (duplicates.length > 0) {
      this.toast.warn(this.i18n.tf('registration.services.duplicate', { codes: duplicates.join(', ') }));
    }
  }

  protected async print(): Promise<void> {
    const document = this.facade.document();
    if (document === null || this.printing()) {
      return;
    }

    this.printing.set(true);
    try {
      await this.printer.quote(document);
    } catch {
      this.toast.error(this.i18n.t('registration.printError'));
    } finally {
      this.printing.set(false);
    }
  }

  /** WF-10 step 4: the services and the tier go to CP-06 as `?services=a,b&tierId=…`. */
  protected continueToRegistration(): void {
    const services = this.facade.lines().map((line) => line.serviceId);
    if (services.length === 0) {
      return;
    }

    const tierId = this.facade.tierId();
    void this.router.navigate(['/registration/new'], { queryParams: { services: services.join(','), ...(tierId === '' ? {} : { tierId }) } });
  }
}
