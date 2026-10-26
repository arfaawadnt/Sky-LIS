import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { SampleConditionView } from '../../../core/api/catalogue-a.contracts';
import type { CreateSampleCompatibilityGroupRequest, SampleCompatibilityGroupView } from '../../../core/api/sample-compatibility.contracts';
import type { CatalogueOption } from '../state-a/catalogue-a-options';
import { CompatibilityGroupDrawerComponent } from './compatibility-group-drawer.component';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const types: readonly CatalogueOption[] = [
  { value: 'ser', code: 'SER', nameEn: 'Serum', nameAr: 'مصل', isActive: true },
  { value: 'pl', code: 'PL', nameEn: 'Plasma', nameAr: 'بلازما', isActive: true },
  { value: 'old', code: 'OLD', nameEn: 'Old', nameAr: 'قديم', isActive: false },
];

const conditions = new Map<string, readonly SampleConditionView[]>([
  [
    'ser',
    [
      { id: 'fast', code: 'FAST', nameEn: 'Fasting', nameAr: 'صائم', isActive: true },
      { id: 'rand', code: 'RAND', nameEn: 'Random', nameAr: 'عشوائي', isActive: true },
    ],
  ],
]);

const sugar: SampleCompatibilityGroupView = {
  id: 'g1',
  code: 'SUGAR',
  nameEn: 'Sugar',
  nameAr: 'السكر',
  isActive: true,
  members: [
    { sampleTypeId: 'ser', sampleTypeCode: 'SER', sampleTypeNameEn: 'Serum', sampleTypeNameAr: 'مصل', sampleConditionId: 'fast', sampleConditionCode: 'FAST', sampleConditionNameEn: 'Fasting', sampleConditionNameAr: 'صائم', priority: 1, available: true },
    { sampleTypeId: 'ser', sampleTypeCode: 'SER', sampleTypeNameEn: 'Serum', sampleTypeNameAr: 'مصل', sampleConditionId: 'rand', sampleConditionCode: 'RAND', sampleConditionNameEn: 'Random', sampleConditionNameAr: 'عشوائي', priority: 2, available: true },
  ],
};

describe('CompatibilityGroupDrawerComponent (LIS-CR-015 M4)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [CompatibilityGroupDrawerComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
  });

  async function render(group: SampleCompatibilityGroupView | null): Promise<{ root: HTMLElement; saved: CreateSampleCompatibilityGroupRequest[]; asked: string[] }> {
    const fixture = TestBed.createComponent(CompatibilityGroupDrawerComponent);
    const saved: CreateSampleCompatibilityGroupRequest[] = [];
    const asked: string[] = [];
    fixture.componentInstance.save.subscribe((request) => saved.push(request));
    fixture.componentInstance.needConditions.subscribe((typeId) => asked.push(typeId));
    fixture.componentRef.setInput('sampleTypes', types);
    fixture.componentRef.setInput('conditions', conditions);
    fixture.componentRef.setInput('group', group);
    fixture.componentRef.setInput('open', true);
    TestBed.tick();
    await settle();
    TestBed.tick();
    return { root: fixture.nativeElement as HTMLElement, saved, asked };
  }

  function element<T extends Element>(root: HTMLElement, selector: string): T {
    const found = root.querySelector<T>(selector);
    if (!found) {
      throw new Error(`${selector} missing`);
    }

    return found;
  }

  function change(root: HTMLElement, selector: string, value: string, event = 'change'): void {
    const target = element<HTMLInputElement | HTMLSelectElement>(root, selector);
    target.value = value;
    target.dispatchEvent(new Event(event));
    TestBed.tick();
  }

  it('shows an existing group in order with its conditions, the code locked, and asks for the conditions of its types', async () => {
    const { root, asked } = await render(sugar);
    expect(element<HTMLInputElement>(root, 'input[name="code"]').disabled).toBe(true);
    expect(element<HTMLSelectElement>(root, 'tr[data-member="1"] select.condition').value).toBe('fast');
    expect(element<HTMLSelectElement>(root, 'tr[data-member="2"] select.condition').value).toBe('rand');
    expect(asked).toEqual(['ser']);
    // inactive sample types are not offered
    expect(Array.from(element<HTMLSelectElement>(root, 'tr[data-member="1"] select.type').options).map((option) => option.value)).toEqual(['', 'ser', 'pl']);
  });

  it('reorders the members and emits them in the new order', async () => {
    const { root, saved } = await render(sugar);
    element<HTMLButtonElement>(root, 'tr[data-member="2"] .row-btn.up').click();
    TestBed.tick();
    element<HTMLButtonElement>(root, '.btn.primary').click();

    expect(saved).toEqual([
      {
        code: 'SUGAR',
        nameEn: 'Sugar',
        nameAr: 'السكر',
        members: [
          { sampleTypeId: 'ser', sampleConditionId: 'rand' },
          { sampleTypeId: 'ser', sampleConditionId: 'fast' },
        ],
      },
    ]);
  });

  it('builds a new group of mixed sample types with "No condition"', async () => {
    const { root, saved, asked } = await render(null);
    change(root, 'input[name="code"]', 'sugar', 'input');
    change(root, 'input[name="nameEn"]', 'Sugar', 'input');
    change(root, 'input[name="nameAr"]', 'السكر', 'input');
    change(root, 'tr[data-member="1"] select.type', 'ser');
    change(root, 'tr[data-member="1"] select.condition', 'fast');
    change(root, 'tr[data-member="2"] select.type', 'pl');
    element<HTMLButtonElement>(root, '.btn.primary').click();

    expect(asked).toEqual(['ser', 'pl']);
    expect(saved).toEqual([
      {
        code: 'SUGAR',
        nameEn: 'Sugar',
        nameAr: 'السكر',
        members: [
          { sampleTypeId: 'ser', sampleConditionId: 'fast' },
          { sampleTypeId: 'pl', sampleConditionId: null },
        ],
      },
    ]);
  });

  it('marks broken rules and does not emit', async () => {
    const { root, saved } = await render(sugar);
    element<HTMLButtonElement>(root, 'tr[data-member="2"] .row-btn.remove').click();
    TestBed.tick();
    element<HTMLButtonElement>(root, '.btn.primary').click();
    TestBed.tick();

    expect(saved).toEqual([]);
    expect(root.textContent).toContain('catalogue-a.compatible.fixRules');
    expect(root.textContent).toContain('catalogue-a.compatible.rule.CAT-280');

    element<HTMLButtonElement>(root, '.btn.add').click();
    TestBed.tick();
    change(root, 'tr[data-member="2"] select.type', 'ser');
    change(root, 'tr[data-member="2"] select.condition', 'fast');
    element<HTMLButtonElement>(root, '.btn.primary').click();
    TestBed.tick();
    expect(saved).toEqual([]);
    expect(root.textContent).toContain('catalogue-a.compatible.rule.CAT-283');
  });
});
