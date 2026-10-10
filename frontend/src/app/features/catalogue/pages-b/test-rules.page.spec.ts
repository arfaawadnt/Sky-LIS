import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { CATALOGUE_B_API, CATALOGUE_B_LOOKUP_API, type Paged, type TestRuleListItem } from '../../../core/api/catalogue-b.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { TestRulesFacade } from '../state-b/catalogue-b-list.facade';
import { TestRulesPage } from './test-rules.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const rule: TestRuleListItem = {
  id: 'r1',
  nameEn: 'Glucose adds HbA1c',
  nameAr: 'الجلوكوز يضيف السكر التراكمي',
  clinicalGroupId: null,
  clinicalGroupNameEn: null,
  clinicalGroupNameAr: null,
  triggers: [{ id: 'glu', code: 'GLU', nameEn: 'Glucose', nameAr: 'جلوكوز', isActive: true }],
  added: [{ id: 'a1c', code: 'HBA1C', nameEn: 'HbA1c', nameAr: 'السكر التراكمي', isActive: true }],
  isActive: true,
};

function page(items: TestRuleListItem[]): Paged<TestRuleListItem> {
  return { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
}

describe('TestRulesPage (CP-52)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestRulesPage],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        TestRulesFacade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  async function render(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<TestRulesPage>>; host: HTMLElement }> {
    const fixture = TestBed.createComponent(TestRulesPage);
    TestBed.tick();
    await settle();
    backend.expectOne((request) => request.url === CATALOGUE_B_LOOKUP_API.clinicalGroups).flush({ items: [{ id: 'g1', code: 'CHEM', nameEn: 'Chemistry', nameAr: 'الكيمياء' }] });
    backend.expectOne((request) => request.method === 'GET' && request.url === CATALOGUE_B_API.testRules).flush(page([rule]));
    await settle();
    await fixture.whenStable();
    return { fixture, host: fixture.nativeElement as HTMLElement };
  }

  it('shows each rule as triggers >> added services', async () => {
    const { host } = await render();

    const cell = host.querySelector('tbody tr[data-row="r1"] .rule');
    expect(cell?.textContent).toContain('GLU');
    expect(cell?.textContent).toContain('>>');
    expect(cell?.textContent).toContain('HBA1C');
    expect(host.querySelector('app-toolbar select option[value="g1"]')?.textContent).toContain('Chemistry');
  });

  it('checks the edited rule on the server and lists its issues without saving', async () => {
    const { fixture, host } = await render();

    host.querySelector<HTMLTableRowElement>('tbody tr[data-row="r1"]')?.click();
    await fixture.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    expect(dialog?.querySelectorAll('app-catalogue-b-picker .chip').length).toBe(2);

    const check = Array.from(dialog?.querySelectorAll<HTMLButtonElement>('button') ?? []).find((button) => button.textContent?.includes('catalogue-b.testRules.check'));
    check?.click();
    await settle();

    const request = backend.expectOne(CATALOGUE_B_API.testRuleValidation);
    expect(request.request.body).toEqual({ ruleId: 'r1', triggerServiceIds: ['glu'], addedServiceIds: ['a1c'] });
    request.flush({ isValid: true, issues: [{ code: 'CAT-243', severity: 'Warning', message: 'Same triggers', serviceId: null, otherRuleId: 'r9' }] });
    await settle();
    await fixture.whenStable();

    expect(dialog?.querySelector('.issues')?.textContent).toContain('Same triggers');
    backend.expectNone((candidate) => candidate.method === 'PUT' || (candidate.method === 'POST' && candidate.url === CATALOGUE_B_API.testRules));
  });
});
