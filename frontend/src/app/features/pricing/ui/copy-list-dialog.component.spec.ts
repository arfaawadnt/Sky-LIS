import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { ProblemDetails } from '../../../core/api/problem-details';
import type { CopyBasePriceListRequest } from '../../../core/api/pricing.contracts';
import { CopyListDialogComponent, copyCodeStem, freeCode } from './copy-list-dialog.component';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const SOURCE = { code: 'V11-PL26', nameEn: 'V11 cash 2026', nameAr: 'نقدية 2026', year: 2026 };

const TAKEN: ProblemDetails = {
  type: 'about:blank',
  title: 'Conflict',
  status: 409,
  code: 'PRC-101',
  traceId: null,
  correlationId: null,
  errors: null,
};

describe('Copy list dialog (CP-33)', () => {
  it('proposes next year\'s code when it is free, otherwise the first free -n variant within 16 characters (UAT B-06)', () => {
    expect(freeCode('V11-PL27', new Set())).toBe('V11-PL27');
    expect(freeCode('V11-PL27', new Set(['V11-PL27']))).toBe('V11-PL27-2');
    expect(freeCode('v11-pl27', new Set(['v11-pl27', 'V11-PL27-2']))).toBe('V11-PL27-3');
    expect(freeCode('ABCDEFGHIJKLMNOP', new Set(['ABCDEFGHIJKLMNOP']))).toBe('ABCDEFGHIJKLMN-2');
    expect(copyCodeStem(SOURCE)).toBe('V11-PL27');
    expect(copyCodeStem({ ...SOURCE, code: 'LONGCODEPL-2026', year: 2026 })).toBe('LONGCODEPL-2');
  });

  async function render(taken: ReadonlySet<string>) {
    await TestBed.configureTestingModule({ imports: [CopyListDialogComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
    const fixture = TestBed.createComponent(CopyListDialogComponent);
    fixture.componentRef.setInput('source', SOURCE);
    fixture.componentRef.setInput('takenCodes', taken);
    fixture.componentInstance.open.set(true);
    TestBed.tick();
    await settle();
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const code = host.querySelector<HTMLInputElement>('input[formcontrolname="code"]');
    if (!code) {
      throw new Error('code input missing');
    }

    return { fixture, host, code };
  }

  it('never proposes the code of an existing list, even an inactive one hidden by the status filter (UAT B-06)', async () => {
    const { code } = await render(new Set(['V11-PL27']));
    expect(code.value).toBe('V11-PL27-2');
  });

  it('shows a refused duplicate code under the code field, not as a banner, until the code is changed (UAT B-06)', async () => {
    const { fixture, host, code } = await render(new Set());
    let sent: CopyBasePriceListRequest | null = null;
    fixture.componentInstance.confirmCopy.subscribe((request) => (sent = request));

    host.querySelector<HTMLButtonElement>('button.btn.primary')?.click();
    await settle();
    expect(sent).not.toBeNull();
    expect((sent as unknown as CopyBasePriceListRequest).code).toBe('V11-PL27');

    fixture.componentRef.setInput('problem', TAKEN);
    TestBed.tick();
    fixture.detectChanges();
    const field = code.closest('app-form-field');
    expect(field?.querySelector('.error')?.textContent?.trim()).toBeTruthy();
    expect(host.querySelector('form > p.problem')).toBeNull();
    expect(code.getAttribute('aria-invalid')).toBe('true');

    code.value = 'V11-PL27B';
    code.dispatchEvent(new Event('input'));
    TestBed.tick();
    fixture.detectChanges();
    expect(field?.querySelector('.error')).toBeNull();
  });

  it('keeps other refusals in the banner', async () => {
    const { fixture, host } = await render(new Set());
    fixture.componentRef.setInput('problem', { ...TAKEN, code: 'PRC-012' });
    TestBed.tick();
    fixture.detectChanges();
    expect(host.querySelector('form > p.problem')).not.toBeNull();
    expect(host.querySelector('app-form-field .error')).toBeNull();
  });
});
