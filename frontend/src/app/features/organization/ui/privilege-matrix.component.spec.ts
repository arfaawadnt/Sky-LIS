import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { PermissionModule } from '../../../core/api/organization.contracts';
import { PrivilegeMatrixComponent, sortKeys } from './privilege-matrix.component';
import { applyResultFlag } from './result-rights-grid.component';
import { discountModeOf } from './special-rights-form.component';

const modules: PermissionModule[] = [
  { key: 'registration', displayName: 'Visits, orders and bookings', actions: ['view', 'create', 'cancel'], platformOnly: false },
  { key: 'billing', displayName: 'Payments', actions: ['view', 'approve'], platformOnly: false },
];

describe('PrivilegeMatrixComponent (CP-37)', () => {
  let emitted: (readonly string[])[];

  async function render(selected: string[], baseline: string[] | null = null): Promise<HTMLElement> {
    await TestBed.configureTestingModule({ imports: [PrivilegeMatrixComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
    const fixture = TestBed.createComponent(PrivilegeMatrixComponent);
    fixture.componentRef.setInput('modules', modules);
    fixture.componentRef.setInput('actions', ['view', 'create', 'approve', 'cancel']);
    fixture.componentRef.setInput('selected', new Set(selected));
    fixture.componentRef.setInput('baseline', baseline === null ? null : new Set(baseline));
    emitted = [];
    fixture.componentInstance.selectedChange.subscribe((keys) => emitted.push(keys));
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows a checkbox only where the module exposes the action and toggles one key', async () => {
    const host = await render(['registration.view']);

    const rows = host.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(rows[1]?.querySelectorAll('input[type="checkbox"]').length).toBe(3);   // all + view + approve
    const create = host.querySelector<HTMLInputElement>('input[aria-label="Visits, orders and bookings: create"]');
    create?.click();

    expect(emitted.at(-1)).toEqual(['registration.create', 'registration.view']);
  });

  it('grants a whole module from its "all" box and revokes everything shown', async () => {
    const host = await render(['billing.view']);

    host.querySelectorAll<HTMLInputElement>('tbody tr td.all-col input')[1]?.click();
    expect(emitted.at(-1)).toEqual(['billing.approve', 'billing.view']);

    const revoke = [...host.querySelectorAll<HTMLButtonElement>('button')].find((button) => button.textContent?.includes('organization.matrix.revokeAll'));
    revoke?.click();
    expect(emitted.at(-1)).toEqual([]);
  });

  it('marks the cells that differ from the baseline', async () => {
    const host = await render(['registration.view', 'billing.approve'], ['registration.view', 'registration.create']);

    expect(host.querySelectorAll('td.modified').length).toBe(2);   // registration.create removed, billing.approve added
  });

  it('sorts keys ordinally like the server', () => {
    expect(sortKeys(['b.view', 'a.viewConfidential', 'a.view'])).toEqual(['a.view', 'a.viewConfidential', 'b.view']);
  });
});

describe('Result and special rights helpers', () => {
  const none = { clinicalGroupId: 'g', branchId: null, canSee: false, canReview: false, canVerify: false, canEdit: false, canPrint: false };

  it('implies see from any other result right and clears everything with see', () => {
    const verify = applyResultFlag(none, 'canVerify', true);
    expect(verify.canSee).toBe(true);
    expect(applyResultFlag(verify, 'canSee', false)).toEqual(none);
  });

  it('reads the discount mode from the cap', () => {
    expect(discountModeOf(null)).toBe('default');
    expect(discountModeOf(0)).toBe('none');
    expect(discountModeOf(15)).toBe('custom');
  });
});
