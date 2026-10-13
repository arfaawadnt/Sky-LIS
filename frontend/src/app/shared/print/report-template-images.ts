import { HttpClient } from '@angular/common/http';
import { Injectable, InjectionToken, type Provider, type Type, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { type ReportTemplateImageKind, reportTemplateImageUrl } from '../../core/api/catalogue-b.contracts';

/** Object URLs of a report template's logo and signature for a document's `<img>`; null = none attached (or unreadable). */
export interface ReportTemplateImageUrls {
  readonly logoUrl: string | null;
  readonly signatureUrl: string | null;
}

export const NO_TEMPLATE_IMAGES: ReportTemplateImageUrls = { logoUrl: null, signatureUrl: null };

/** What the loader needs of a template: its id and whether each image is attached (its file id). */
export interface ReportTemplateImageRefs {
  readonly templateId: string;
  readonly logoFileId: string | null;
  readonly signatureFileId: string | null;
}

/** Loaded images; `release()` revokes the object URLs once the document is printed or closed (idempotent). */
export interface LoadedTemplateImages extends ReportTemplateImageUrls {
  release(): void;
}

/**
 * Reads the logo and the signature of a report template (validation F-02, intake LIS-INT-CTB-001 A10). The images need
 * the bearer token, so they are fetched as blobs and handed out as object URLs (the site's CSP allows `img-src blob:`);
 * a print frame written by `PrintService` shares the page's origin and shows them too. A missing or unreadable image is
 * left out (null): a report still prints without it.
 */
@Injectable({ providedIn: 'root' })
export class ReportTemplateImageLoader {
  private readonly http = inject(HttpClient);

  async load(template: ReportTemplateImageRefs | null | undefined): Promise<LoadedTemplateImages> {
    if (!template) {
      return { ...NO_TEMPLATE_IMAGES, release: () => undefined };
    }

    const [logo, signature] = await Promise.all([
      template.logoFileId ? this.blob(template.templateId, 'logo') : Promise.resolve(null),
      template.signatureFileId ? this.blob(template.templateId, 'signature') : Promise.resolve(null),
    ]);
    const logoUrl = objectUrl(logo);
    const signatureUrl = objectUrl(signature);
    let released = false;
    return {
      logoUrl,
      signatureUrl,
      release: () => {
        if (released) {
          return;
        }

        released = true;
        for (const url of [logoUrl, signatureUrl]) {
          if (url !== null) {
            URL.revokeObjectURL(url);
          }
        }
      },
    };
  }

  /** One image as a blob, or null when it is not attached or cannot be read. */
  async blob(templateId: string, image: ReportTemplateImageKind): Promise<Blob | null> {
    try {
      return await firstValueFrom(this.http.get(reportTemplateImageUrl(templateId, image), { responseType: 'blob' }));
    } catch {
      return null;
    }
  }
}

function objectUrl(blob: Blob | null): string | null {
  return blob === null || typeof URL.createObjectURL !== 'function' ? null : URL.createObjectURL(blob);
}

/**
 * The results report renderer (`features/results/ui/report.document`), lazily loaded, for pages of other features that
 * show how a report looks (the report template preview, SA-12 / CP-54). Features never import each other, so the
 * application root provides it (`provideReportDocumentRenderer` in `app.config.ts`). The component takes the inputs
 * `doc` (`LaboratoryReportDocument`) and `images` (`ReportTemplateImageUrls`).
 */
export type ReportDocumentRendererLoader = () => Promise<Type<unknown>>;

export const REPORT_DOCUMENT_RENDERER = new InjectionToken<ReportDocumentRendererLoader>('REPORT_DOCUMENT_RENDERER');

export function provideReportDocumentRenderer(loader: ReportDocumentRendererLoader): Provider {
  return { provide: REPORT_DOCUMENT_RENDERER, useValue: loader };
}
