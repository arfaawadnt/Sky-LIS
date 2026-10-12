import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { type NotificationTemplateListItem, PLATFORM_PERMISSIONS } from '../../../core/api/platform.contracts';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { TemplatesFacade } from '../state/reference.facades';
import { NotificationTemplatesPage } from './notification-templates.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const otp: NotificationTemplateListItem = {
  id: 't1', code: 'otp', channel: 'Sms', subjectEn: null, subjectAr: null,
  textEn: 'Your code is {code}.', textAr: 'رمزك {code}.', isActive: true,
};

interface TemplatesPageApi {
  openEdit(template: NotificationTemplateListItem | null): void;
}

/**
 * Validation R4-02: a user who may only view notification templates (notifications.view without notifications.manage) opens
 * a read-only template — titled as such, every field disabled, no token chips to insert, no Save — while a manager edits.
 */
describe('Notification templates dialog follows the user rights (UAT R4-02)', () => {
  async function open(canManage: boolean): Promise<HTMLElement> {
    await TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        TemplatesFacade,
        {
          provide: PermissionsService,
          useValue: { can: (key: string) => key !== PLATFORM_PERMISSIONS.notificationsManage || canManage } satisfies Pick<PermissionsService, 'can'>,
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(NotificationTemplatesPage);
    TestBed.tick();
    await settle();
    TestBed.inject(HttpTestingController).match(() => true).forEach((request) => request.flush(request.request.url.endsWith('placeholders') ? { placeholders: ['code', 'minutes'], aliases: {} } : { items: [otp], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 }));
    await settle();

    (fixture.componentInstance as unknown as TemplatesPageApi).openEdit(otp);
    TestBed.tick();
    await fixture.whenStable();
    return document.body;
  }

  afterEach(() => TestBed.resetTestingModule());

  it('a viewer sees "Template" with disabled fields, disabled token chips and no Save', async () => {
    const root = await open(false);
    expect(root.textContent).toContain('platform.templates.view');
    expect(root.textContent).not.toContain('platform.templates.edit');
    const fields = Array.from(root.querySelectorAll<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>('#platform-template-form textarea, #platform-template-form select, #platform-template-form input'));
    expect(fields.length).toBeGreaterThan(0);
    expect(fields.every((field) => field.disabled)).toBe(true);
    const chips = Array.from(root.querySelectorAll<HTMLButtonElement>('#platform-template-form .chips button'));
    expect(chips.every((chip) => chip.disabled)).toBe(true);
    expect(root.querySelector('button[type="submit"][form="platform-template-form"]')).toBeNull();
  });

  it('a manager sees "Edit template" with editable text and a Save button', async () => {
    const root = await open(true);
    expect(root.textContent).toContain('platform.templates.edit');
    const text = root.querySelector<HTMLTextAreaElement>('#platform-template-form textarea');
    expect(text?.disabled).toBe(false);
    expect(root.querySelector('button[type="submit"][form="platform-template-form"]')).not.toBeNull();
  });

  it('C-10: the row action reads "View template" for a viewer and "Edit template" for a manager', async () => {
    const viewer = await open(false);
    const viewLabels = Array.from(viewer.querySelectorAll<HTMLButtonElement>('button.row-btn')).map((button) => button.getAttribute('aria-label'));
    expect(viewLabels).toContain('platform.templates.viewAction');
    expect(viewLabels).not.toContain('platform.templates.edit');
    TestBed.resetTestingModule();
    document.body.innerHTML = '';

    const manager = await open(true);
    const editLabels = Array.from(manager.querySelectorAll<HTMLButtonElement>('button.row-btn')).map((button) => button.getAttribute('aria-label'));
    expect(editLabels).toContain('platform.templates.edit');
    expect(editLabels).not.toContain('platform.templates.viewAction');
  });
});
