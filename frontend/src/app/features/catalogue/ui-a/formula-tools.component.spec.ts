import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { FORMULA_TOOLS, FORMULA_TOOL_GROUPS, type FormulaTool, FormulaToolsComponent, applyFormulaTool } from './formula-tools.component';

const toolOf = (id: string): FormulaTool => {
  const found = FORMULA_TOOLS.find((item) => item.id === id);
  if (found === undefined) {
    throw new Error(`no tool ${id}`);
  }

  return found;
};

/** LIS-CR-015 M3a: the formula tools insert at the caret; functions wrap the selection and put the caret inside. */
describe('Formula tools', () => {
  it('inserts an operator at the caret and replaces a selection', () => {
    expect(applyFormulaTool('[A][B]', 3, 3, toolOf('plus'))).toEqual({ value: '[A] + [B]', caret: 6 });
    expect(applyFormulaTool('[A] x [B]', 4, 5, toolOf('times'))).toEqual({ value: '[A]  *  [B]', caret: 7 });
    expect(applyFormulaTool('', 0, 0, toolOf('age'))).toEqual({ value: '@AGE', caret: 4 });
  });

  it('puts the caret inside the parentheses of a function, around the selection when there is one', () => {
    expect(applyFormulaTool('', 0, 0, toolOf('round'))).toEqual({ value: 'ROUND()', caret: 6 });
    expect(applyFormulaTool('[A] / 2', 0, 7, toolOf('abs'))).toEqual({ value: 'ABS([A] / 2)', caret: 11 });
    expect(applyFormulaTool('1 + ', 4, 4, toolOf('if'))).toEqual({ value: '1 + IF(, , )', caret: 7 });
  });

  it('keeps the text when the tool would pass the maximum length, and clamps a caret out of range', () => {
    expect(applyFormulaTool('x'.repeat(999), 999, 999, toolOf('plus'))).toEqual({ value: 'x'.repeat(999), caret: 999 });
    expect(applyFormulaTool('[A]', 50, 50, toolOf('minus'))).toEqual({ value: '[A] - ', caret: 6 });
  });

  it('offers operators, comparisons, functions and the patient variables in four groups', () => {
    expect(FORMULA_TOOL_GROUPS).toEqual(['operators', 'compare', 'functions', 'patient']);
    expect(FORMULA_TOOLS.filter((item) => item.group === 'operators').map((item) => item.before.trim())).toEqual(['+', '-', '*', '/', '^', '(', ')', ',']);
    expect(FORMULA_TOOLS.filter((item) => item.group === 'compare').map((item) => item.label)).toEqual(['<', '<=', '>', '>=', '=', '<>']);
    expect(FORMULA_TOOLS.filter((item) => item.group === 'functions').map((item) => item.before)).toEqual(['ROUND(', 'IF(', 'MIN(', 'MAX(', 'ABS(']);
    expect(FORMULA_TOOLS.filter((item) => item.group === 'patient').map((item) => item.before)).toEqual(['@AGE', '@FEMALE']);
  });

  it('renders a titled button per tool and emits the tool and Clear', async () => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    const fixture = TestBed.createComponent(FormulaToolsComponent);
    const applied: FormulaTool[] = [];
    let cleared = 0;
    fixture.componentInstance.apply.subscribe((item) => applied.push(item));
    fixture.componentInstance.clear.subscribe(() => cleared++);
    await fixture.whenStable();

    const root = fixture.nativeElement as HTMLElement;
    const buttons = Array.from(root.querySelectorAll<HTMLButtonElement>('button[data-tool]'));
    expect(buttons).toHaveLength(FORMULA_TOOLS.length);
    expect(buttons.every((button) => button.title !== '' && button.type === 'button')).toBe(true);
    expect(root.querySelectorAll('[role="group"]')).toHaveLength(4);

    root.querySelector<HTMLButtonElement>('button[data-tool="max"]')?.click();
    root.querySelector<HTMLButtonElement>('button.clear')?.click();
    expect(applied.map((item) => item.id)).toEqual(['max']);
    expect(cleared).toBe(1);

    fixture.componentRef.setInput('disabled', true);
    await fixture.whenStable();
    expect(buttons.every((button) => button.disabled)).toBe(true);
  });
});
