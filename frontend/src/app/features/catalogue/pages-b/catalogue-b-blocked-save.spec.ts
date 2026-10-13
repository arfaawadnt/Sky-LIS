import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type Type, provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';

import { PermissionsService } from '../../../core/permissions/permissions.service';
import { DiagnosesFacade, PreparationsFacade, ShortcutSetsFacade, TestRulesFacade } from '../state-b/catalogue-b-list.facade';
import { DiagnosesPage } from './diagnoses.page';
import { PreparationsPage } from './preparations.page';
import { ShortcutSetsPage } from './shortcut-sets.page';
import { TestRulesPage } from './test-rules.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

interface EditorAccess {
  openCreate(): void;
  save(): Promise<void>;
}

/**
 * Validation v0.5.0 F-03: the Catalogue B drawers (Preparations, Diagnoses, Test rules, Shortcut sets) refused an empty
 * save silently. They now say "The record was not saved: correct the marked fields first." and focus the first marked
 * field, like the kit's master-data drawers; nothing is sent.
 */
describe('Catalogue B editors: a blocked save says so (F-03)', () => {
  const pages: readonly (readonly [string, Type<unknown>, Type<unknown>])[] = [
    ['Preparations', PreparationsPage, PreparationsFacade],
    ['Diagnoses', DiagnosesPage, DiagnosesFacade],
    ['Test rules', TestRulesPage, TestRulesFacade],
    ['Shortcut sets', ShortcutSetsPage, ShortcutSetsFacade],
  ];

  let backend: HttpTestingController;
  let fixture: ComponentFixture<unknown> | null = null;

  afterEach(() => {
    (fixture?.nativeElement as HTMLElement | undefined)?.remove();
    fixture = null;
  });

  async function render(page: Type<unknown>, facade: Type<unknown>): Promise<{ view: EditorAccess; host: HTMLElement }> {
    await TestBed.configureTestingModule({
      imports: [page],
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        facade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(page);
    const host = fixture.nativeElement as HTMLElement;
    document.body.appendChild(host);   // focus needs a connected document
    TestBed.tick();
    await settle();
    for (const request of backend.match((candidate) => candidate.method === 'GET')) {
      request.flush({ items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 1 });
    }

    await settle();
    await fixture.whenStable();
    return { view: fixture.componentInstance as EditorAccess, host };
  }

  it.each(pages)('%s: the notice shows, the first marked field takes the focus and nothing is sent', async (_, page, facade) => {
    const { view, host } = await render(page, facade);

    view.openCreate();
    await fixture?.whenStable();
    const dialog = host.querySelector<HTMLElement>('[role="dialog"]');
    expect(dialog?.querySelector('form > p.problem[role="alert"]')).toBeNull();

    await view.save();
    await fixture?.whenStable();

    expect(dialog?.querySelector('form > p.problem[role="alert"]')?.textContent).toContain('shared.masterData.invalid');
    const first = dialog?.querySelector<HTMLElement>('input.ng-invalid, select.ng-invalid, textarea.ng-invalid');
    expect(first).toBeTruthy();
    expect(document.activeElement).toBe(first);
    backend.expectNone((candidate) => candidate.method === 'POST' || candidate.method === 'PUT');
  });

  it('the notice is gone when the drawer is opened again', async () => {
    const { view, host } = await render(PreparationsPage, PreparationsFacade);

    view.openCreate();
    await fixture?.whenStable();
    await view.save();
    await fixture?.whenStable();
    expect(host.querySelector('[role="dialog"] form > p.problem[role="alert"]')).not.toBeNull();

    view.openCreate();
    await fixture?.whenStable();
    expect(host.querySelector('[role="dialog"] form > p.problem[role="alert"]')).toBeNull();
  });
});
