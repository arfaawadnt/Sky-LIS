import { NgComponentOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, linkedSignal, output, resource, signal, untracked } from '@angular/core';

import {
  CATALOGUE_B_PERMISSIONS,
  REPORT_TEMPLATE_IMAGE_ACCEPT,
  REPORT_TEMPLATE_IMAGE_KINDS,
  type ReportTemplateImageKind,
  type ReportTemplateListItem,
} from '../../../core/api/catalogue-b.contracts';
import { ProblemError } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { problemMessage } from '../../../shared/forms/problem-text';
import { DocumentTextsLoader } from '../../../shared/print/document-texts';
import { type LoadedTemplateImages, REPORT_DOCUMENT_RENDERER, ReportTemplateImageLoader } from '../../../shared/print/report-template-images';
import { CanDirective } from '../../../shared/ui/can.directive';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { CatalogueBApi } from '../data-b/catalogue-b.api';
import { type PreviewLanguage, previewLanguageOf, sampleReportDocument } from '../data-b/report-template-preview';

/**
 * `<app-report-template-images [template]="item" (changed)="…" />`: the logo and the signature of a report template
 * (SRS CP-54 / SA-12, validation F-02, intake A10) — a thumbnail of each, and for a holder of `catalogue.edit` upload,
 * replace and remove (PNG, JPEG or WebP up to 512 KB; the server checks the bytes) — and a preview of a sample result
 * report laid out with the template, drawn by the results report renderer itself (`REPORT_DOCUMENT_RENDERER`).
 */
@Component({
  selector: 'app-report-template-images',
  imports: [CanDirective, NgComponentOutlet],
  template: `
    <div class="images">
      @for (kind of kinds; track kind) {
        <section class="slot" [attr.aria-label]="i18n.t('catalogue-b.reportTemplates.' + kind)">
          <h3>{{ i18n.t('catalogue-b.reportTemplates.' + kind) }}</h3>
          <div class="thumb" [class.signature]="kind === 'signature'">
            @if (urlOf(kind); as url) {
              <img [src]="url" [alt]="i18n.t('catalogue-b.reportTemplates.' + kind)" />
            } @else {
              <span class="muted small">{{ i18n.t(attached(kind) ? 'catalogue-b.reportTemplates.images.loading' : 'catalogue-b.reportTemplates.images.none') }}</span>
            }
          </div>
          <div class="actions" *appCan="permissions.edit">
            <label class="btn sm" [class.disabled]="busy()">
              <input class="visually-hidden" type="file" [accept]="accept" [disabled]="busy()" (change)="onSelected(kind, $event)" />
              {{ i18n.t(attached(kind) ? 'catalogue-b.reportTemplates.images.replace' : 'catalogue-b.reportTemplates.images.upload') }}
            </label>
            @if (attached(kind)) {
              <button type="button" class="btn sm ghost" [disabled]="busy()" (click)="remove(kind)">{{ i18n.t('catalogue-b.reportTemplates.images.remove') }}</button>
            }
          </div>
        </section>
      }
    </div>
    <p class="muted small">{{ i18n.t('catalogue-b.reportTemplates.images.hint') }}</p>

    <section class="preview" [attr.aria-label]="i18n.t('catalogue-b.reportTemplates.preview.title')">
      <div class="preview-head">
        <h3>{{ i18n.t('catalogue-b.reportTemplates.preview.title') }}</h3>
        @if (current().language === 'Both') {
          <div class="langs" role="group" [attr.aria-label]="i18n.t('catalogue-b.reportTemplates.field.language')">
            <button type="button" class="btn sm" [class.primary]="previewLang() === 'en'" [attr.aria-pressed]="previewLang() === 'en'" (click)="previewLang.set('en')">English</button>
            <button type="button" class="btn sm" [class.primary]="previewLang() === 'ar'" [attr.aria-pressed]="previewLang() === 'ar'" (click)="previewLang.set('ar')">العربية</button>
          </div>
        }
      </div>
      <p class="muted small">{{ i18n.t('catalogue-b.reportTemplates.preview.hint') }}</p>
      @if (renderer.hasValue() && renderer.value(); as type) {
        @if (previewInputs().texts !== null) {
          <div class="paper"><ng-container *ngComponentOutlet="type; inputs: previewInputs()" /></div>
        } @else {
          <p class="muted small">{{ i18n.t('catalogue-b.reportTemplates.images.loading') }}</p>
        }
      } @else if (renderer.isLoading()) {
        <p class="muted small">{{ i18n.t('catalogue-b.reportTemplates.images.loading') }}</p>
      } @else {
        <p class="muted small">{{ i18n.t('catalogue-b.reportTemplates.preview.unavailable') }}</p>
      }
    </section>
  `,
  styles: `
    :host { display: grid; gap: var(--sp-3); }
    .images { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: var(--sp-3); }
    .slot { display: grid; gap: var(--sp-2); }
    h3 { margin: 0; font-size: 13px; }
    .thumb { display: grid; place-items: center; height: 96px; border: 1px dashed var(--line); border-radius: var(--r); background: var(--surface-2); }
    .thumb img { max-width: 90%; max-height: 84px; object-fit: contain; }
    .thumb.signature { background: #fff; }
    .actions, .langs, .preview-head { display: flex; flex-wrap: wrap; gap: var(--sp-2); align-items: center; }
    .preview-head { justify-content: space-between; }
    .actions label:has(input:focus-visible) { box-shadow: var(--focus-ring); }
    .actions label.disabled { opacity: 0.6; pointer-events: none; }
    .preview { display: grid; gap: var(--sp-2); }
    .paper { padding: 16px; border: 1px solid var(--line); border-radius: var(--r); background: #fff; overflow-x: auto; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportTemplateImagesComponent {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(CatalogueBApi);
  private readonly loader = inject(ReportTemplateImageLoader);
  private readonly toast = inject(ToastService);
  private readonly rendererLoader = inject(REPORT_DOCUMENT_RENDERER, { optional: true });
  private readonly documentTexts = inject(DocumentTextsLoader);

  /** The template whose images are shown (the register row); replaced by a fresh read after every change. */
  readonly template = input.required<ReportTemplateListItem>();

  /** The template after an upload or a removal (new file ids), so the page can refresh its register. */
  readonly changed = output<ReportTemplateListItem>();

  protected readonly kinds = REPORT_TEMPLATE_IMAGE_KINDS;
  protected readonly accept = REPORT_TEMPLATE_IMAGE_ACCEPT;
  protected readonly permissions = CATALOGUE_B_PERMISSIONS;
  protected readonly busy = signal(false);

  /** The template as last read from the server. */
  protected readonly current = linkedSignal(() => this.template());

  protected readonly previewLang = linkedSignal<PreviewLanguage>(() => previewLanguageOf(this.current(), untracked(() => this.i18n.lang())));

  private readonly images = signal<LoadedTemplateImages | null>(null);
  private loadSequence = 0;

  protected readonly renderer = resource({
    loader: () => (this.rendererLoader ? this.rendererLoader() : Promise.resolve(null)),
  });

  /**
   * The sample report's captions in the preview language (the template's, or the EN/AR switch of a bilingual one), never
   * the screen's: frame, direction and captions follow it as a printed report does (validation v0.5.0 F-12).
   */
  protected readonly texts = resource({
    params: () => this.previewLang(),
    loader: ({ params }) => this.documentTexts.load(params, ['results', 'shared']),
  });

  protected readonly previewInputs = computed(() => {
    const images = this.images();
    const texts = this.texts.hasValue() ? this.texts.value() : null;
    return {
      texts: texts !== null && texts.lang === this.previewLang() ? texts : null,
      doc: sampleReportDocument(this.current(), this.previewLang(), { labNameEn: this.i18n.t('catalogue-b.reportTemplates.preview.labEn'), labNameAr: this.i18n.t('catalogue-b.reportTemplates.preview.labAr') }, this.nowUtc),
      images: images === null ? null : { logoUrl: images.logoUrl, signatureUrl: images.signatureUrl },
    };
  });

  private readonly nowUtc = new Date().toISOString();

  constructor() {
    void this.i18n.load('results');   // the report renderer speaks the results dictionary
    effect(() => {
      const template = this.current();
      untracked(() => void this.loadImages(template));
    });
    inject(DestroyRef).onDestroy(() => {
      this.loadSequence++;
      this.images()?.release();
    });
  }

  protected attached(kind: ReportTemplateImageKind): boolean {
    const template = this.current();
    return (kind === 'logo' ? template.logoFileId : template.signatureFileId) !== null;
  }

  protected urlOf(kind: ReportTemplateImageKind): string | null {
    const images = this.images();
    return images === null ? null : kind === 'logo' ? images.logoUrl : images.signatureUrl;
  }

  /** Uploads the chosen image; the server checks the type (by its bytes) and the size and answers with a coded refusal. */
  protected async onSelected(kind: ReportTemplateImageKind, event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';   // the same file can be chosen again after a refusal
    if (file !== undefined && !this.busy()) {
      await this.run(() => this.api.uploadReportTemplateImage(this.current().id, kind, file), 'catalogue-b.reportTemplates.images.saved');
    }
  }

  protected async remove(kind: ReportTemplateImageKind): Promise<void> {
    if (!this.busy()) {
      await this.run(() => this.api.removeReportTemplateImage(this.current().id, kind), 'catalogue-b.reportTemplates.images.removed');
    }
  }

  private async run(action: () => Promise<unknown>, doneKey: string): Promise<void> {
    this.busy.set(true);
    try {
      await action();
      const fresh = await this.api.reportTemplates.get(this.current().id);
      this.current.set(fresh);
      this.changed.emit(fresh);
      this.toast.success(this.i18n.t(doneKey));
    } catch (error) {
      this.toast.error(error instanceof ProblemError ? problemMessage(this.i18n, 'catalogue-b.reportTemplates', error.problem) : this.i18n.t('shared.error.generic'));
    } finally {
      this.busy.set(false);
    }
  }

  /** Reads the attached images; a slower earlier read never overwrites a later one, and replaced URLs are revoked. */
  private async loadImages(template: ReportTemplateListItem): Promise<void> {
    const sequence = ++this.loadSequence;
    const loaded = await this.loader.load({ templateId: template.id, logoFileId: template.logoFileId, signatureFileId: template.signatureFileId });
    if (sequence !== this.loadSequence) {
      loaded.release();
      return;
    }

    this.images()?.release();
    this.images.set(loaded);
  }
}
