import { Directive, computed, inject, input } from '@angular/core';

import type { LaboratoryReportDocument, ReportLine, ReportSection } from '../../../../../core/api/results.contracts';
import { I18nService } from '../../../../../core/i18n/i18n.service';
import type { DocumentTexts } from '../../../../../shared/print/document-texts';
import type { ReportTemplateImageUrls } from '../../../../../shared/print/report-template-images';
import { ageText, flagSymbol, formatPrintDateTime, pickName } from '../../result-format';
import { type FlagTone, flagToneOf, isRichLine } from './report-design-format';

/**
 * The language side every piece of a printed report shares: the texts of the report's own language (`texts`, the
 * result-language parameter or the template's, validation v0.5.0 F-12), else the screen's, so captions, frame language
 * and direction never mix. Numbers and dates keep Latin digits.
 */
@Directive()
export abstract class ReportTextsBase {
  protected readonly i18n = inject(I18nService);

  readonly texts = input<DocumentTexts | null>(null);

  protected readonly lang = computed(() => this.texts()?.lang ?? this.i18n.lang());

  protected readonly dir = computed(() => (this.lang() === 'ar' ? 'rtl' : 'ltr'));

  /** The texts of the document follow the report language (a laboratory prints Arabic reports from an English screen). */
  protected t(key: string): string {
    return this.texts()?.t(key) ?? this.i18n.t(key);
  }

  /** A counted sentence in its form (one, two, 3–10, 11+; F-11), the number in Latin digits like every printed figure. */
  protected tc(key: string, count: number): string {
    return this.texts()?.tc(key, count) ?? this.i18n.tc(key, count, { count: String(count) });
  }

  protected name(en: string | null, ar: string | null): string {
    return pickName(en, ar, this.lang());
  }

  protected at(value: string | null | undefined): string {
    return formatPrintDateTime(value, this.lang());
  }
}

/**
 * The shared view model of the five report designs (LIS-CR-010, C-14): every design takes the same inputs as the
 * dispatcher (`doc`, `images`, `texts`) and reads the same derived values; only the layout differs.
 */
@Directive()
export abstract class ReportDesignBase extends ReportTextsBase {
  readonly doc = input.required<LaboratoryReportDocument>();

  /** The template's logo and signature as object URLs (validation F-02). */
  readonly images = input<ReportTemplateImageUrls | null>(null);

  /** The signature image prints on a verified, final report only (never on a preliminary one). */
  protected readonly signatureUrl = computed(() => (this.doc().isPreliminary ? null : (this.images()?.signatureUrl ?? null)));

  protected readonly title = computed(() => (this.lang() === 'ar' ? this.doc().titleAr : this.doc().titleEn));

  protected readonly subtitle = computed(() => {
    const template = this.doc().template;
    return template ? (this.lang() === 'ar' ? template.headerTextAr : template.headerTextEn) : null;
  });

  protected readonly footer = computed(() => {
    const template = this.doc().template;
    return template ? (this.lang() === 'ar' ? template.footerTextAr : template.footerTextEn) : null;
  });

  protected readonly branch = computed(() => {
    const letterhead = this.doc().letterhead;
    const picked = pickName(letterhead.branchNameEn, letterhead.branchNameAr, this.lang());
    return picked === '—' ? null : picked;
  });

  /** A VIP patient previewed without the See-VIP right reads "VIP patient" (F-06; such a report does not print, RES-056). */
  protected readonly patientName = computed(() =>
    this.doc().patient.isMasked === true ? this.t('results.vipMasked') : pickName(this.doc().patient.fullName, this.doc().patient.nameAr, this.lang()),
  );

  protected readonly age = computed(() => {
    const patient = this.doc().patient;
    return ageText(patient.ageYears, patient.ageMonths, patient.ageDays, { y: this.t('results.age.y'), m: this.t('results.age.m'), d: this.t('results.age.d') });
  });

  protected readonly gender = computed(() => {
    const gender = this.doc().patient.gender;
    return gender ? this.t(`results.gender.${gender}`) : '—';
  });

  protected readonly samples = computed(() => this.doc().samples.map((sample) => `${sample.sampleNumber ?? '—'} (${this.at(sample.collectedAtUtc)})`).join(', '));

  protected readonly showQr = computed(() => this.doc().template?.showQr ?? true);

  protected heading(section: ReportSection): string | null {
    const heading = this.lang() === 'ar' ? (section.headingAr ?? section.headingEn) : (section.headingEn ?? section.headingAr);
    return section.isProfile && heading ? heading : null;
  }

  protected lineName(line: ReportLine): string {
    return pickName(line.nameEn, line.nameAr, this.lang());
  }

  protected symbol(line: ReportLine): string {
    return flagSymbol(line.flag);
  }

  protected isRich(line: ReportLine): boolean {
    return isRichLine(line);
  }

  protected isAbnormal(line: ReportLine): boolean {
    return line.flag !== null && line.flag !== 'Normal';
  }

  protected tone(line: ReportLine): FlagTone {
    return flagToneOf(line.flag, line.isPanic);
  }
}
