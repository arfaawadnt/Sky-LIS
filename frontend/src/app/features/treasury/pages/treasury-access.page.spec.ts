import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideZonelessChangeDetection, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import type { FormGroup } from '@angular/forms';

import treasuryEn from '../../../../../public/i18n/treasury.en.json';
import { TREASURY_API, type TreasuryAccessRequestListItem } from '../../../core/api/treasury.contracts';
import { AuthStore, type CurrentUser } from '../../../core/auth/auth.store';
import { DeviceIdentityService } from '../../../core/device/device-identity.service';
import { problemDetailsInterceptor } from '../../../core/http/problem-details.interceptor';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { TreasuryAccessFacade } from '../state/treasury-access.facade';
import { TreasuryAccessPage } from './treasury-access.page';

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

const approved: TreasuryAccessRequestListItem = {
  id: 'r1',
  number: 'TRQ-2026-00001',
  treasuryId: 't1',
  treasuryCode: 'NORTH-CASH',
  treasuryNameEn: 'North cash',
  treasuryNameAr: 'خزينة الشمال',
  branchId: 'b1',
  requestedByUserId: 'u-cashier',
  requestedByName: 'Cashier One',
  deviceId: 'device-1',
  deviceLabel: 'Front desk',
  status: 'Approved',
  requestedAtUtc: '2026-10-11T08:00:00Z',
  decidedAtUtc: '2026-10-11T08:05:00Z',
  decidedByName: 'Shift Manager',
  decisionReason: null,
  approvalComment: 'checked at the desk',
};
const rejected: TreasuryAccessRequestListItem = { ...approved, id: 'r2', number: 'TRQ-2026-00002', status: 'Rejected', approvalComment: null, decisionReason: 'use your own desk' };
const pending: TreasuryAccessRequestListItem = { ...approved, id: 'r3', number: 'TRQ-2026-00003', status: 'Pending', decidedAtUtc: null, decidedByName: null, approvalComment: null };

interface AccessPageInternals {
  readonly form: FormGroup;
  note(row: TreasuryAccessRequestListItem): string;
  requestProblem(): unknown;
  approve(row: TreasuryAccessRequestListItem): Promise<void>;
  submitRequest(): Promise<void>;
  exportCsv(): Promise<void>;
}

describe('TreasuryAccessPage (CP-29)', () => {
  let backend: HttpTestingController;

  beforeEach(async () => {
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input: string | URL | Request) =>
      new Response(JSON.stringify(String(input) === '/i18n/treasury.en.json' ? treasuryEn : {}), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    );
    await TestBed.configureTestingModule({
      imports: [TreasuryAccessPage],
      providers: [
        provideZonelessChangeDetection(),
        provideRouter([]),   // the C11 links (RouterLink) need a router
        provideHttpClient(withInterceptors([problemDetailsInterceptor])),
        provideHttpClientTesting(),
        TreasuryAccessFacade,
        { provide: PermissionsService, useValue: { can: () => true } satisfies Pick<PermissionsService, 'can'> },
        {
          provide: AuthStore,
          useValue: {
            currentUser: signal<CurrentUser | null>({ id: 'u-approver', displayName: 'Shift Manager', language: 'en', scope: 'tenant' }),
            branchIds: signal<readonly string[]>([]),
          } satisfies Pick<AuthStore, 'currentUser' | 'branchIds'>,
        },
        { provide: DeviceIdentityService, useValue: { deviceId: () => 'device-test' } satisfies Pick<DeviceIdentityService, 'deviceId'> },
      ],
    }).compileComponents();
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  async function open(rows: readonly TreasuryAccessRequestListItem[]): Promise<{ page: AccessPageInternals; root: HTMLElement; detect: () => void }> {
    const fixture = TestBed.createComponent(TreasuryAccessPage);
    TestBed.tick();
    await settle();
    backend.match((request) => request.url === TREASURY_API.requests).forEach((request) => request.flush({ items: rows, page: 1, pageSize: 50, totalCount: rows.length, totalPages: 1 }));
    backend.match((request) => request.url === TREASURY_API.treasuries).forEach((request) => request.flush({ items: [], page: 1, pageSize: 200, totalCount: 0, totalPages: 0 }));
    await settle();
    fixture.detectChanges();
    return { page: fixture.componentInstance as unknown as AccessPageInternals, root: fixture.nativeElement as HTMLElement, detect: () => fixture.detectChanges() };
  }

  it('shows the approval comment and the decision reason on the rows (validation v0.5.0 D-05)', async () => {
    const { page, root } = await open([approved, rejected]);

    expect(page.note(approved)).toBe('Comment: checked at the desk');
    expect(page.note(rejected)).toBe('Reason: use your own desk');
    expect(page.note({ ...approved, status: 'Revoked', decisionReason: 'desk closed' })).toBe('Comment: checked at the desk · Reason: desk closed');
    expect(page.note(pending)).toBe('');
    expect(root.textContent).toContain('Comment: checked at the desk');
    expect(root.textContent).toContain('Reason: use your own desk');
  });

  it('approves with the optional comment of the dialog, and without one when it is left empty (D-05)', async () => {
    const { page } = await open([pending]);
    const prompt = vi.spyOn(TestBed.inject(ConfirmService), 'promptReason').mockResolvedValueOnce('desk verified').mockResolvedValueOnce('');

    const first = page.approve(pending);
    await settle();
    expect(prompt.mock.calls[0]?.[0]).toMatchObject({ required: false, label: 'Comment (optional)' });
    const withComment = backend.expectOne(`${TREASURY_API.requests}/r3/approve`);
    expect(withComment.request.body).toEqual({ comment: 'desk verified' });
    withComment.flush(null, { status: 204, statusText: 'No Content' });
    await first;
    await settle();
    backend.match((request) => request.url === TREASURY_API.requests).forEach((request) => request.flush({ items: [pending], page: 1, pageSize: 50, totalCount: 1, totalPages: 1 }));

    const second = page.approve(pending);
    await settle();
    const without = backend.expectOne(`${TREASURY_API.requests}/r3/approve`);
    expect(without.request.body).toEqual({ comment: null });
    without.flush(null, { status: 204, statusText: 'No Content' });
    await second;
  });

  it('shows TRS-015 under the treasury field, not in the banner (validation v0.5.0 O-4)', async () => {
    const { page, root, detect } = await open([]);
    page.form.setValue({ treasuryId: 't-east', deviceLabel: '' });

    const submitting = page.submitRequest();
    await settle();
    backend.expectOne(TREASURY_API.requests).flush(
      { type: 'about:blank', title: 'Refused', status: 422, code: 'TRS-015', traceId: null, correlationId: null, errors: null },
      { status: 422, statusText: 'Unprocessable Entity' },
    );
    await submitting;
    detect();

    const control = page.form.get('treasuryId');
    expect(control?.errors).toEqual({ server: 'This treasury belongs to a branch you are not assigned to.' });
    expect(control?.touched).toBe(true);
    expect(page.requestProblem()).toBeNull();
    expect(root.querySelector('.request-card .problem')).toBeNull();
    expect(root.textContent).toContain('This treasury belongs to a branch you are not assigned to.');
  });

  it('exports the requests of the filter with the approval comment and the decision reason (D-05)', async () => {
    const { page } = await open([approved, rejected]);
    const blobs: Blob[] = [];
    const original = URL.createObjectURL;
    URL.createObjectURL = (blob: Blob | MediaSource): string => {
      blobs.push(blob as Blob);
      return 'blob:spec';
    };
    try {
      const exporting = page.exportCsv();
      await settle();
      const request = backend.expectOne((candidate) => candidate.url === TREASURY_API.requests);
      expect(request.request.params.get('mine')).toBe('false');
      request.flush({ items: [approved, rejected], page: 1, pageSize: 200, totalCount: 2, totalPages: 1 });
      await exporting;
    } finally {
      URL.createObjectURL = original;
    }

    expect(blobs.length).toBe(1);
    const text = await blobs[0]!.text();
    const [header, first, second] = text.replace(/^\uFEFF/, '').split('\r\n');
    expect(header).toContain('Approval comment,Decision reason');
    expect(first).toContain('TRQ-2026-00001');
    expect(first).toMatch(/,checked at the desk,$/);
    expect(second).toMatch(/,,use your own desk$/);
  });

  it('shows Approve (never Reject) on the primary administrator\'s own pending request — the SOD-TRS-001 exception (2026-10-14)', async () => {
    const own = { ...pending, requestedByUserId: 'u-approver', requestedByName: 'Shift Manager' };
    const labels = (root: HTMLElement): string[] => [...root.querySelectorAll<HTMLButtonElement>('app-data-table button.btn.sm')].map((button) => button.textContent?.trim() ?? '');

    // an ordinary approver: nothing to decide on their own row, only Cancel
    const first = await open([own]);
    expect(labels(first.root)).toEqual(['Cancel request']);

    // the primary administrator: Approve appears, Reject does not, Cancel stays
    const user = TestBed.inject(AuthStore).currentUser as unknown as { set(value: CurrentUser | null): void };
    user.set({ id: 'u-approver', displayName: 'Shift Manager', language: 'en', scope: 'tenant', isPrimaryAdministrator: true });
    first.detect();
    await settle();
    first.detect();
    expect(labels(first.root)).toEqual(['Approve', 'Cancel request']);
  });
});
