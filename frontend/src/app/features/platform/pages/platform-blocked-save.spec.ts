import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type Type, provideZonelessChangeDetection } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ClientsFacade } from '../state/clients.facade';
import { PcrKitsFacade, TemplatesFacade } from '../state/reference.facades';
import { WalletsFacade } from '../state/wallets.facade';
import { ClientsPage } from './clients.page';
import { NotificationTemplatesPage } from './notification-templates.page';
import { PcrKitsPage } from './pcr-kits.page';
import { SubscriptionsPage } from './subscriptions.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

/** How the test opens the add drawer and saves it. */
interface Editor {
  readonly name: string;
  readonly page: Type<unknown>;
  readonly facade: Type<unknown>;
  readonly form: string;
  open(view: Record<string, (argument?: null) => unknown>): unknown;
  save(view: Record<string, () => Promise<void>>): Promise<void>;
}

const editors: readonly Editor[] = [
  { name: 'Add client (SA-04)', page: ClientsPage, facade: ClientsFacade, form: '#platform-client-form', open: (view) => view['openCreate']?.(), save: (view) => view['save']?.() ?? Promise.resolve() },
  { name: 'Add template (SA-20)', page: NotificationTemplatesPage, facade: TemplatesFacade, form: '#platform-template-form', open: (view) => view['openEdit']?.(null), save: (view) => view['save']?.() ?? Promise.resolve() },
  { name: 'Add plan', page: SubscriptionsPage, facade: WalletsFacade, form: '#platform-plan-form', open: (view) => view['openPlan']?.(null), save: (view) => view['savePlan']?.() ?? Promise.resolve() },
  { name: 'Add PCR kit', page: PcrKitsPage, facade: PcrKitsFacade, form: '#platform-pcr-kit-form', open: (view) => view['openEdit']?.(null), save: (view) => view['save']?.() ?? Promise.resolve() },
];

/**
 * Validation v0.5.0 F-03: Add client, Add template and Add plan refused an empty save without the notice. They now say
 * "The record was not saved: correct the marked fields first." above the form and focus the first marked field; nothing
 * is sent.
 */
describe('Super Admin drawers: a blocked save says so (F-03)', () => {
  let backend: HttpTestingController;
  let fixture: ComponentFixture<unknown> | null = null;

  afterEach(() => {
    (fixture?.nativeElement as HTMLElement | undefined)?.remove();
    fixture = null;
    TestBed.resetTestingModule();
  });

  async function render(editor: Editor): Promise<{ view: Record<string, never>; host: HTMLElement }> {
    await TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        editor.facade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(editor.page);
    const host = fixture.nativeElement as HTMLElement;
    document.body.appendChild(host);   // focus needs a connected document
    TestBed.tick();
    await settle();
    backend.match(() => true).forEach((request) => request.flush(request.request.url.endsWith('placeholders') ? { placeholders: ['code'], aliases: {} } : { items: [], page: 1, pageSize: 50, totalCount: 0, totalPages: 0 }));
    await settle();
    await fixture.whenStable();
    return { view: fixture.componentInstance as Record<string, never>, host };
  }

  it.each(editors)('$name: the notice shows, the first marked field takes the focus and nothing is sent', async (editor) => {
    const { view, host } = await render(editor);

    editor.open(view);
    await fixture?.whenStable();
    const form = host.querySelector<HTMLElement>(editor.form);
    expect(form).not.toBeNull();
    expect(form?.querySelector(':scope > p.problem[role="alert"]')).toBeNull();

    await editor.save(view);
    await fixture?.whenStable();

    expect(form?.querySelector(':scope > p.problem[role="alert"]')?.textContent).toContain('shared.masterData.invalid');
    const first = form?.querySelector<HTMLElement>('input.ng-invalid, select.ng-invalid, textarea.ng-invalid');
    expect(first).toBeTruthy();
    expect(document.activeElement).toBe(first);
    backend.expectNone((candidate) => candidate.method === 'POST' || candidate.method === 'PUT');
  });

  it('the notice is cleared when the drawer opens again', async () => {
    const editor = editors[0];
    if (editor === undefined) {
      throw new Error('no editor');
    }

    const { view, host } = await render(editor);
    editor.open(view);
    await fixture?.whenStable();
    await editor.save(view);
    await fixture?.whenStable();
    expect(host.querySelector(`${editor.form} > p.problem[role="alert"]`)).not.toBeNull();

    editor.open(view);
    await fixture?.whenStable();
    expect(host.querySelector(`${editor.form} > p.problem[role="alert"]`)).toBeNull();
  });
});
