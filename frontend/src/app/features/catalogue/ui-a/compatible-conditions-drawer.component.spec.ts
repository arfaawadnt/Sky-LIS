import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { CompatibleSampleTypeView, SampleConditionPairItem } from '../../../core/api/compatible-conditions.contracts';
import { CompatibleConditionsDrawerComponent } from './compatible-conditions-drawer.component';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const serum: CompatibleSampleTypeView = {
  id: 'serum',
  code: 'SERUM',
  shortName: 'SER',
  nameEn: 'Serum',
  nameAr: 'مصل',
  isActive: true,
  conditions: [
    { id: 'fast', code: 'FAST', nameEn: 'Fasting', nameAr: 'صائم', isActive: true, mergesIntoConditionId: null },
    { id: 'rand', code: 'RAND', nameEn: 'Random', nameAr: 'عشوائي', isActive: true, mergesIntoConditionId: null },
    { id: 'pp', code: 'PP', nameEn: 'Post-prandial', nameAr: 'بعد الأكل', isActive: true, mergesIntoConditionId: 'rand' },
  ],
};

describe('CompatibleConditionsDrawerComponent (LIS-CR-015 M4)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CompatibleConditionsDrawerComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
  });

  async function render(type: CompatibleSampleTypeView = serum): Promise<{ root: HTMLElement; emitted: (readonly SampleConditionPairItem[])[] }> {
    const fixture = TestBed.createComponent(CompatibleConditionsDrawerComponent);
    const emitted: (readonly SampleConditionPairItem[])[] = [];
    fixture.componentInstance.save.subscribe((pairs) => emitted.push(pairs));
    fixture.componentRef.setInput('sampleType', type);
    fixture.componentRef.setInput('open', true);
    TestBed.tick();
    await settle();
    TestBed.tick();
    return { root: fixture.nativeElement as HTMLElement, emitted };
  }

  function select(root: HTMLElement, code: string): HTMLSelectElement {
    const found = root.querySelector<HTMLSelectElement>(`tr[data-condition="${code}"] select`);
    if (!found) {
      throw new Error(`select of ${code} missing`);
    }

    return found;
  }

  function choose(root: HTMLElement, code: string, value: string): void {
    const element = select(root, code);
    element.value = value;
    element.dispatchEvent(new Event('change'));
    TestBed.tick();
  }

  function saveButton(root: HTMLElement): HTMLButtonElement {
    const button = root.querySelector<HTMLButtonElement>('.btn.primary');
    if (!button) {
      throw new Error('save button missing');
    }

    return button;
  }

  it('shows one select per condition with "Its own sample" and the stored choice', async () => {
    const { root } = await render();
    expect(root.querySelectorAll('tr[data-condition]')).toHaveLength(3);
    expect(select(root, 'FAST').options[0]?.textContent).toContain('catalogue-a.compatible.ownSample');
    expect(select(root, 'PP').value).toBe('rand');
    // RAND is a target (PP merges into it): it is offered nothing else
    expect(select(root, 'RAND').options).toHaveLength(1);
  });

  it('emits the full map of the type', async () => {
    const { root, emitted } = await render();
    choose(root, 'PP', 'fast');
    choose(root, 'RAND', 'fast');
    saveButton(root).click();

    expect(emitted).toEqual([
      [
        { conditionId: 'fast', mergesIntoConditionId: null },
        { conditionId: 'rand', mergesIntoConditionId: 'fast' },
        { conditionId: 'pp', mergesIntoConditionId: 'fast' },
      ],
    ]);
  });

  it('marks a broken rule and does not emit', async () => {
    const chained: CompatibleSampleTypeView = { ...serum, conditions: serum.conditions.map((condition) => (condition.id === 'rand' ? { ...condition, mergesIntoConditionId: 'fast' } : condition)) };
    const { root, emitted } = await render(chained);
    saveButton(root).click();
    TestBed.tick();

    expect(emitted).toEqual([]);
    expect(root.textContent).toContain('catalogue-a.compatible.fixRules');
    expect(root.textContent).toContain('catalogue-a.compatible.rule.CAT-283');
    expect(root.textContent).toContain('catalogue-a.compatible.rule.CAT-284');
  });
});
