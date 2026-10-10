import { DOCUMENT } from '@angular/common';
import { EnvironmentInjector, Injectable, type Type, createComponent, inject } from '@angular/core';

import { I18nService } from '../../core/i18n/i18n.service';
import { type PageSize, composePrintDocument } from './print-document';

export interface PrintComponentRequest {
  readonly type: Type<unknown>;
  readonly inputs?: Readonly<Record<string, unknown>>;
}

export interface PrintRequest {
  /** The frame/document title (also the default file name of a "save as PDF"). */
  readonly title: string;
  readonly size: PageSize;
  readonly landscape?: boolean;
  /** Ready HTML (already rendered), or a component to render with its inputs. */
  readonly html?: string;
  readonly component?: PrintComponentRequest;
  /** Defaults to the application's current direction and language. */
  readonly dir?: 'ltr' | 'rtl';
  readonly lang?: string;
}

const ASSET_TIMEOUT_MS = 3000;
const FRAME_TIMEOUT_MS = 120000;

/**
 * Prints a document through a hidden iframe (MODULES.md §1 "Documents"): the application's stylesheets
 * (`print.css` and the component styles) are cloned into the frame, the page size becomes an `@page` rule, images
 * and fonts are awaited, then the browser's print dialog opens (PDF comes from "save as PDF" there). A component
 * is rendered off-screen with its inputs (`setInput`), so documents are ordinary OnPush components fed with the
 * DTO of `GET …/documents/<kind>`.
 */
@Injectable({ providedIn: 'root' })
export class PrintService {
  private readonly document = inject(DOCUMENT);
  private readonly environmentInjector = inject(EnvironmentInjector);
  private readonly i18n = inject(I18nService);

  /** Renders a component with inputs to static HTML (used by `print`, and by previews). */
  render(request: PrintComponentRequest): string {
    const host = this.document.createElement('div');
    const ref = createComponent(request.type, { environmentInjector: this.environmentInjector, hostElement: host });
    try {
      for (const [name, value] of Object.entries(request.inputs ?? {})) {
        ref.setInput(name, value);
      }

      ref.changeDetectorRef.detectChanges();
      return host.innerHTML;
    } finally {
      ref.destroy();
    }
  }

  /** The complete frame document for a request (exposed for tests and previews). */
  compose(request: PrintRequest): string {
    const body = request.html ?? (request.component ? this.render(request.component) : '');
    return composePrintDocument({
      title: request.title,
      size: request.size,
      landscape: request.landscape ?? false,
      dir: request.dir ?? this.i18n.dir(),
      lang: request.lang ?? this.i18n.lang(),
      baseHref: this.document.baseURI,
      styles: this.collectStyles(),
      body,
    });
  }

  /** Opens the print dialog for the request; resolves when the dialog closes (or after a safety timeout). */
  async print(request: PrintRequest): Promise<void> {
    const html = this.compose(request);
    const frame = this.document.createElement('iframe');
    frame.className = 'app-print-frame';
    frame.setAttribute('aria-hidden', 'true');
    frame.title = request.title;
    this.document.body.appendChild(frame);

    const frameDocument = frame.contentDocument;
    const frameWindow = frame.contentWindow;
    if (!frameDocument || !frameWindow) {
      frame.remove();
      throw new Error('The print frame could not be created.');
    }

    frameDocument.open();
    frameDocument.write(html);
    frameDocument.close();
    await waitForAssets(frameDocument);

    await new Promise<void>((resolve) => {
      let settled = false;
      const finish = (): void => {
        if (settled) {
          return;
        }

        settled = true;
        frame.remove();
        resolve();
      };

      frameWindow.addEventListener('afterprint', finish);
      setTimeout(finish, FRAME_TIMEOUT_MS);
      frameWindow.focus();
      frameWindow.print();
    });
  }

  private collectStyles(): string {
    return [...this.document.querySelectorAll('style, link[rel="stylesheet"]')].map((element) => element.outerHTML).join('\n');
  }
}

/** Stylesheets and images of the frame document, with a cap so a missing asset never blocks printing. */
async function waitForAssets(frameDocument: Document): Promise<void> {
  const pending: Promise<unknown>[] = [];
  for (const link of frameDocument.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]')) {
    if (!link.sheet) {
      pending.push(new Promise((resolve) => link.addEventListener('load', resolve, { once: true })));
    }
  }

  for (const image of frameDocument.images) {
    if (!image.complete) {
      pending.push(image.decode().catch(() => undefined));
    }
  }

  if (frameDocument.fonts?.ready) {
    pending.push(frameDocument.fonts.ready);
  }

  await Promise.race([Promise.all(pending), new Promise((resolve) => setTimeout(resolve, ASSET_TIMEOUT_MS))]);
}
