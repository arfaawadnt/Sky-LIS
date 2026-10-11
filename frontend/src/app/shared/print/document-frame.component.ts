import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import { I18nService } from '../../core/i18n/i18n.service';

/**
 * `<app-document-frame [labNameEn]="…" [labNameAr]="…" [branchEn]="…" [branchAr]="…" [title]="…" [printedBy]="…" [printedAt]="…">
 *   <img documentLogo … /> …body… <span documentFooter>…</span></app-document-frame>`:
 * the bilingual header (laboratory name in both languages around the logo, branch, document title) and the footer
 * (printed by / at) shared by receipts, job orders, reports and sheets. Labels are passed already formatted. Each side
 * of the header speaks its own language: the English side shows `branchEn`, the Arabic side `branchAr` (UAT A-10: the
 * Arabic laboratory name was followed by the English branch name); either falls back to the other, then to `branch`.
 */
@Component({
  selector: 'app-document-frame',
  template: `
    <article class="print-document">
      <header class="doc-head">
        <div class="lab" dir="ltr" lang="en">
          <b>{{ labNameEn() }}</b>
          @if (branchLineEn(); as branch) {
            <span>{{ branch }}</span>
          }
        </div>
        <div class="logo"><ng-content select="[documentLogo]" /></div>
        <div class="lab" dir="rtl" lang="ar">
          <b>{{ labNameAr() }}</b>
          @if (branchLineAr(); as branch) {
            <span>{{ branch }}</span>
          }
        </div>
      </header>
      <div class="doc-title-row">
        <h1 class="doc-title">{{ title() }}</h1>
        @if (reference(); as reference) {
          <span class="doc-ref">{{ reference }}</span>
        }
      </div>
      @if (subtitle(); as subtitle) {
        <p class="doc-sub">{{ subtitle }}</p>
      }
      <div class="doc-body"><ng-content /></div>
      <footer class="doc-foot">
        @if (printedBy(); as printedBy) {
          <span>{{ i18n.t('shared.print.printedBy') }}: {{ printedBy }}</span>
        }
        @if (printedAt(); as printedAt) {
          <span>{{ i18n.t('shared.print.printedAt') }}: {{ printedAt }}</span>
        }
        <ng-content select="[documentFooter]" />
      </footer>
    </article>
  `,
  styles: `
    :host {
      display: block;
    }

    .print-document {
      background: #fff;
      color: #111;
      font-size: 12px;
      line-height: 1.45;
    }

    .doc-head {
      display: grid;
      grid-template-columns: 1fr auto 1fr;
      align-items: center;
      gap: 12px;
      border-bottom: 2px solid #0b2a5b;
      padding-bottom: 8px;
      margin-bottom: 10px;
    }

    .lab {
      display: grid;
      gap: 2px;
      font-size: 12px;
    }

    .lab b {
      font-size: 14px;
      color: #0b2a5b;
    }

    .lab[dir='rtl'] {
      text-align: right;
    }

    .logo {
      display: grid;
      place-items: center;
      min-width: 60px;
    }

    .logo:empty {
      display: none;
    }

    .doc-title-row {
      display: flex;
      justify-content: space-between;
      align-items: baseline;
      gap: 12px;
    }

    .doc-title {
      margin: 0;
      font-size: 17px;
      color: #0b2a5b;
    }

    .doc-ref {
      font-family: 'Cascadia Mono', Consolas, monospace;
      font-size: 12px;
    }

    .doc-sub {
      margin: 2px 0 8px;
      color: #444;
    }

    .doc-body {
      margin-top: 8px;
    }

    .doc-foot {
      display: flex;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: 6px 16px;
      margin-top: 16px;
      padding-top: 6px;
      border-top: 1px solid #999;
      font-size: 10px;
      color: #444;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DocumentFrameComponent {
  protected readonly i18n = inject(I18nService);

  readonly labNameEn = input.required<string>();
  readonly labNameAr = input.required<string>();
  /** One branch line for both sides (documents that know a single name). */
  readonly branch = input<string | null>(null);
  /** The branch name of the English side of the header. */
  readonly branchEn = input<string | null>(null);
  /** The branch name of the Arabic side of the header. */
  readonly branchAr = input<string | null>(null);

  protected readonly branchLineEn = computed(() => present(this.branchEn()) ?? present(this.branchAr()) ?? present(this.branch()));
  protected readonly branchLineAr = computed(() => present(this.branchAr()) ?? present(this.branchEn()) ?? present(this.branch()));
  readonly title = input.required<string>();
  readonly subtitle = input<string | null>(null);
  /** The document's reference number (receipt, accession…), shown beside the title. */
  readonly reference = input<string | null>(null);
  readonly printedBy = input<string | null>(null);
  /** Already formatted for the reader. */
  readonly printedAt = input<string | null>(null);
}

function present(value: string | null): string | null {
  const text = value?.trim() ?? '';
  return text === '' ? null : text;
}
