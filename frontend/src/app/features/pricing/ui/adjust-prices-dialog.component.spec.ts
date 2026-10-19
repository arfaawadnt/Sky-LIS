import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { AdjustBasePriceListRequest, AdjustBasePriceListResult } from '../../../core/api/pricing.contracts';
import { AdjustPricesDialogComponent, toAdjustRequest } from './adjust-prices-dialog.component';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const PREVIEW: AdjustBasePriceListResult = {
  rows: [
    { serviceId: 's-cbc', code: 'CBC', nameEn: 'Complete blood count', nameAr: 'صورة دم', oldPrice: 180, newPrice: 198, clamped: false },
    { serviceId: 's-lip', code: 'LIP', nameEn: 'Lipid profile', nameAr: 'دهون', oldPrice: 5, newPrice: 0, clamped: true },
  ],
  changed: 2,
  clamped: 1,
  applied: false,
};

describe('Bulk adjust dialog (C21)', () => {
  it('maps the form to the request: the filtered ids as the scope or null, the percentage bounded, two decimals', () => {
    expect(toAdjustRequest({ mode: 'Percent', value: 10.004, rounding: 'Whole', scope: 'all' }, ['s-cbc'], true)).toEqual({ mode: 'Percent', value: 10, rounding: 'Whole', serviceIds: null, preview: true });
    expect(toAdjustRequest({ mode: 'Amount', value: -2.5, rounding: 'None', scope: 'filtered' }, ['s-cbc', 's-lip'], false)).toEqual({ mode: 'Amount', value: -2.5, rounding: 'None', serviceIds: ['s-cbc', 's-lip'], preview: false });
    expect(toAdjustRequest({ mode: 'Percent', value: 5000, rounding: 'None', scope: 'all' }, [], true).value).toBe(1000);
    expect(toAdjustRequest({ mode: 'Percent', value: -150, rounding: 'None', scope: 'all' }, [], true).value).toBe(-100);
  });

  async function render(filterActive: boolean) {
    await TestBed.configureTestingModule({ imports: [AdjustPricesDialogComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
    const fixture = TestBed.createComponent(AdjustPricesDialogComponent);
    fixture.componentRef.setInput('lineCount', 3);
    fixture.componentRef.setInput('filteredIds', ['s-cbc']);
    fixture.componentRef.setInput('filterActive', filterActive);
    fixture.componentInstance.open.set(true);
    TestBed.tick();
    await settle();
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const buttons = [...host.querySelectorAll<HTMLButtonElement>('button')];
    const preview = buttons.find((button) => button.textContent?.includes('pricing.adjust.preview'));
    const apply = buttons.find((button) => button.textContent?.includes('pricing.adjust.apply'));
    if (!preview || !apply) {
      throw new Error('footer buttons missing');
    }

    return { fixture, host, preview, apply };
  }

  it('previews with the typed mode, value, rounding and scope; Apply stays disabled until a preview shows a change', async () => {
    const { fixture, host, preview, apply } = await render(false);
    const sent: AdjustBasePriceListRequest[] = [];
    fixture.componentInstance.preview.subscribe((request) => sent.push(request));
    expect(host.querySelector<HTMLOptionElement>('select[formcontrolname="scope"] option[value="filtered"]')?.disabled).toBe(true);
    expect(apply.disabled).toBe(true);

    const value = host.querySelector<HTMLInputElement>('input[formcontrolname="value"]');
    const rounding = host.querySelector<HTMLSelectElement>('select[formcontrolname="rounding"]');
    if (!value || !rounding) {
      throw new Error('form controls missing');
    }

    value.value = '12.5';
    value.dispatchEvent(new Event('input'));
    rounding.value = 'Nearest5';
    rounding.dispatchEvent(new Event('change'));
    await settle();
    fixture.detectChanges();
    expect(host.querySelector('.example')?.textContent).toContain('pricing.adjust.example');

    preview.click();
    await settle();
    expect(sent).toEqual([{ mode: 'Percent', value: 12.5, rounding: 'Nearest5', serviceIds: null, preview: true }]);

    fixture.componentRef.setInput('result', PREVIEW);
    TestBed.tick();
    fixture.detectChanges();
    const rows = [...host.querySelectorAll('tbody tr')];
    expect(rows.length).toBe(2);
    expect(rows[0]?.textContent).toContain('CBC');
    expect(rows[1]?.classList.contains('clamped')).toBe(true);
    expect(rows[1]?.textContent).toContain('pricing.adjust.heldAtZero');
    expect(host.textContent).toContain('pricing.adjust.clamped');
    expect(apply.disabled).toBe(false);

    const applied: AdjustBasePriceListRequest[] = [];
    fixture.componentInstance.apply.subscribe((request) => applied.push(request));
    apply.click();
    await settle();
    expect(applied).toEqual([{ mode: 'Percent', value: 12.5, rounding: 'Nearest5', serviceIds: null, preview: false }]);

    // an applied answer disables Apply again
    fixture.componentRef.setInput('result', { ...PREVIEW, applied: true });
    TestBed.tick();
    fixture.detectChanges();
    expect(apply.disabled).toBe(true);
  });

  it('starts on the filtered rows when the grid is filtered and sends their ids as the scope', async () => {
    const { fixture, host, preview } = await render(true);
    const sent: AdjustBasePriceListRequest[] = [];
    fixture.componentInstance.preview.subscribe((request) => sent.push(request));

    expect(host.querySelector<HTMLSelectElement>('select[formcontrolname="scope"]')?.value).toBe('filtered');
    preview.click();
    await settle();
    expect(sent[0]?.serviceIds).toEqual(['s-cbc']);
  });

  it('refuses to preview an empty value and never emits while pending', async () => {
    const { fixture, host, preview } = await render(false);
    const sent: AdjustBasePriceListRequest[] = [];
    fixture.componentInstance.preview.subscribe((request) => sent.push(request));

    const value = host.querySelector<HTMLInputElement>('input[formcontrolname="value"]');
    if (!value) {
      throw new Error('value input missing');
    }

    value.value = '';
    value.dispatchEvent(new Event('input'));
    await settle();
    preview.click();
    await settle();
    expect(sent).toEqual([]);

    value.value = '5';
    value.dispatchEvent(new Event('input'));
    fixture.componentRef.setInput('pending', true);
    TestBed.tick();
    fixture.detectChanges();
    preview.click();
    await settle();
    expect(sent).toEqual([]);
  });
});
