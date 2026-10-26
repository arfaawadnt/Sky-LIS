import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { FormulaOperandChoice, FormulaOperandServiceOption } from '../../../core/api/catalogue-a.contracts';
import { type FormulaOperandRow, FormulaOperandsComponent, formulaOperandItems, formulaOperandRows } from './formula-operands.component';

const service = (id: string, type: FormulaOperandServiceOption['type'] = 'Test'): FormulaOperandServiceOption => ({ id, code: id.toUpperCase(), nameEn: id, nameAr: id, type });

const choices: readonly FormulaOperandChoice[] = [
  { testCode: 'CHOL', testId: 't-chol', testNameEn: 'Cholesterol', testNameAr: 'كوليسترول', services: [service('s-chol'), service('s-lipid', 'Profile')], defaultServiceId: 's-chol' },
  { testCode: 'HDL', testId: 't-hdl', testNameEn: 'HDL', testNameAr: 'HDL', services: [service('s-hdl'), service('s-lipid', 'Profile')], defaultServiceId: 's-hdl' },
  { testCode: 'TG', testId: 't-tg', testNameEn: 'Triglycerides', testNameAr: 'دهون ثلاثية', services: [], defaultServiceId: null },
];

/** LIS-CR-015 M3b / M3c: the operand table under the formula. */
describe('Formula operands', () => {
  it('takes the choice made on the page, else the stored operand, else the default off the receipt', () => {
    const current: FormulaOperandRow[] = [{ ...formulaOperandRows(choices, [], [])[0]!, serviceId: null, visibleOnReceipt: false }];
    const rows = formulaOperandRows(choices, current, [
      { testCode: 'CHOL', serviceId: 's-lipid', visibleOnReceipt: true },
      { testCode: 'HDL', serviceId: 's-lipid', visibleOnReceipt: true },
      { testCode: 'OLD', serviceId: 's-old', visibleOnReceipt: true },
    ]);

    expect(rows.map((row) => [row.testCode, row.serviceId, row.visibleOnReceipt])).toEqual([
      ['CHOL', null, false],
      ['HDL', 's-lipid', true],
      ['TG', null, false],
    ]);
  });

  it('falls back to the default when a remembered service no longer carries the test, and never shows a line not ordered on the receipt', () => {
    const rows = formulaOperandRows(choices, [], [{ testCode: 'HDL', serviceId: 's-gone', visibleOnReceipt: true }]);
    expect(rows[1]).toEqual(expect.objectContaining({ serviceId: 's-hdl', visibleOnReceipt: true }));

    expect(formulaOperandItems([{ ...rows[0]!, serviceId: null, visibleOnReceipt: true }])).toEqual([{ testCode: 'CHOL', serviceId: null, visibleOnReceipt: false }]);
  });

  it('shows a row per test with the services, the default preselected, a warning for none, and reports changes', async () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    const fixture = TestBed.createComponent(FormulaOperandsComponent);
    const changes: (readonly FormulaOperandRow[])[] = [];
    fixture.componentRef.setInput('rows', formulaOperandRows(choices, [], []));
    fixture.componentInstance.rows.subscribe((rows) => changes.push(rows));
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    const selects = Array.from(root.querySelectorAll('select'));
    expect(selects.map((select) => select.value)).toEqual(['s-chol', 's-hdl', '']);
    expect(Array.from(selects[0]!.options).map((option) => option.value)).toEqual(['', 's-chol', 's-lipid']);
    expect(root.querySelector('tr[data-operand="TG"] .warn-strip')?.textContent).toContain('noService');
    expect(root.querySelector('tr[data-operand="CHOL"] .warn-strip')).toBeNull();

    const visible = root.querySelector<HTMLInputElement>('tr[data-operand="HDL"] input[type="checkbox"]')!;
    expect(visible.checked).toBe(false);
    visible.click();
    selects[0]!.value = '';
    selects[0]!.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    const last = changes.at(-1) ?? [];
    expect(last.map((row) => [row.testCode, row.serviceId, row.visibleOnReceipt])).toEqual([
      ['CHOL', null, false],
      ['HDL', 's-hdl', true],
      ['TG', null, false],
    ]);
    expect(root.querySelector('tr[data-operand="CHOL"] .warn-strip')?.textContent).toContain('notOrdered');
    expect(root.querySelector<HTMLInputElement>('tr[data-operand="CHOL"] input[type="checkbox"]')?.disabled).toBe(true);
  });
});
