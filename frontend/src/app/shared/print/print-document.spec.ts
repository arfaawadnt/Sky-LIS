import { composePrintDocument, pageRule } from './print-document';

describe('print document composition (shared/print)', () => {
  it('writes the @page rule for every supported size', () => {
    expect(pageRule('A4')).toBe('@page { size: A4; margin: 10mm; }');
    expect(pageRule('A4', true)).toBe('@page { size: A4 landscape; margin: 10mm; }');
    expect(pageRule('A5')).toBe('@page { size: A5; margin: 8mm; }');
    expect(pageRule('label-50x25')).toBe('@page { size: 50mm 25mm; margin: 0; }');
    expect(pageRule('label-38x25', true)).toBe('@page { size: 38mm 25mm; margin: 0; }');
  });

  it('builds a complete RTL-aware document with the cloned styles, the base href and the size class on the body', () => {
    const html = composePrintDocument({
      title: 'Receipt <RCT-2026-00001>',
      size: 'A5',
      dir: 'rtl',
      lang: 'ar',
      baseHref: 'http://127.0.0.1:4300/',
      styles: '<link rel="stylesheet" href="styles.css">',
      body: '<article>body</article>',
    });

    expect(html.startsWith('<!doctype html>')).toBe(true);
    expect(html).toContain('<html lang="ar" dir="rtl">');
    expect(html).toContain('<title>Receipt &lt;RCT-2026-00001&gt;</title>');
    expect(html).toContain('<base href="http://127.0.0.1:4300/">');
    expect(html).toContain('<link rel="stylesheet" href="styles.css">');
    expect(html).toContain('@page { size: A5; margin: 8mm; }');
    expect(html).toContain('<body class="print-root size-A5">');
    expect(html).toContain('<article>body</article>');
  });
});
