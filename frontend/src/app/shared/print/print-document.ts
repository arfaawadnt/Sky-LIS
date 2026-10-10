/** Page sizes the print kit supports (print.css carries the matching `size-*` rules). */
export type PageSize = 'A4' | 'A5' | 'label-50x25' | 'label-38x25';

export interface PrintDocumentOptions {
  readonly title: string;
  readonly size: PageSize;
  readonly landscape?: boolean;
  readonly dir: 'ltr' | 'rtl';
  readonly lang: string;
  /** `<base href>` so relative stylesheet and image URLs resolve inside the frame. */
  readonly baseHref: string;
  /** The `<style>`/`<link rel="stylesheet">` markup cloned from the application document. */
  readonly styles: string;
  /** The rendered document body. */
  readonly body: string;
}

const PAGE_RULES: Readonly<Record<PageSize, { size: string; margin: string }>> = {
  A4: { size: 'A4', margin: '10mm' },
  A5: { size: 'A5', margin: '8mm' },
  'label-50x25': { size: '50mm 25mm', margin: '0' },
  'label-38x25': { size: '38mm 25mm', margin: '0' },
};

function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

/** The `@page` rule for a size (exported for the spec). */
export function pageRule(size: PageSize, landscape = false): string {
  const rule = PAGE_RULES[size];
  const orientation = landscape && (size === 'A4' || size === 'A5') ? ' landscape' : '';
  return `@page { size: ${rule.size}${orientation}; margin: ${rule.margin}; }`;
}

/** The complete HTML of the hidden print frame: a pure function so it can be checked without a browser. */
export function composePrintDocument(options: PrintDocumentOptions): string {
  return [
    '<!doctype html>',
    `<html lang="${escapeHtml(options.lang)}" dir="${options.dir}">`,
    '<head>',
    '<meta charset="utf-8">',
    `<title>${escapeHtml(options.title)}</title>`,
    `<base href="${escapeHtml(options.baseHref)}">`,
    options.styles,
    `<style>${pageRule(options.size, options.landscape ?? false)} html, body { margin: 0; padding: 0; background: #fff; }</style>`,
    '</head>',
    `<body class="print-root size-${options.size}">`,
    options.body,
    '</body>',
    '</html>',
  ].join('\n');
}
