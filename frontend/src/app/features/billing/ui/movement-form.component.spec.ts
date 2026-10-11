import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { BillingReasonOption, BillingTreasuryItem } from '../../../core/api/billing.contracts';
import { MovementFormComponent, type MovementInput, type MovementMode } from './movement-form.component';

const treasuries: BillingTreasuryItem[] = [{ id: 't-1', code: 'MAIN', nameEn: 'Main drawer', nameAr: 'الخزينة', branchId: 'b-1', isActive: true }];
const reasons: BillingReasonOption[] = [{ id: 'r-1', kind: 'Refund', nameEn: 'Overpayment', nameAr: 'مبلغ زائد', isActive: true }];

@Component({
  selector: 'app-movement-form-host',
  imports: [MovementFormComponent],
  template: `<app-billing-movement-form [mode]="mode()" [treasuries]="treasuries" [(treasuryId)]="treasuryId" [dueAmount]="due()" [reasons]="reasons" submitLabel="Pay" (submitted)="submitted.push($event)" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class HostComponent {
  readonly mode = signal<MovementMode>('payment');
  readonly due = signal(150);
  readonly treasuryId = signal('t-1');
  readonly treasuries = treasuries;
  readonly reasons = reasons;
  readonly submitted: MovementInput[] = [];
  readonly form = viewChild.required(MovementFormComponent);
}

function field<T extends HTMLElement>(root: HTMLElement, selector: string): T {
  const element = root.querySelector<T>(selector);
  if (element === null) {
    throw new Error(`missing ${selector}`);
  }

  return element;
}

describe('MovementFormComponent (pay and refund form)', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] }));

  async function render(): Promise<{ host: HostComponent; root: HTMLElement; submit: () => Promise<void> }> {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    return {
      host: fixture.componentInstance,
      root,
      submit: async () => {
        field<HTMLFormElement>(root, 'form').dispatchEvent(new Event('submit'));
        await fixture.whenStable();
      },
    };
  }

  function set(root: HTMLElement, selector: string, value: string): void {
    const element = field<HTMLInputElement | HTMLSelectElement>(root, selector);
    element.value = value;
    element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? 'change' : 'input'));
  }

  it('proposes what is due, refuses more than that and a non-cash payment without reference', async () => {
    const { host, root, submit } = await render();
    expect(field<HTMLInputElement>(root, 'input[type=number]').value).toBe('150');

    set(root, 'input[type=number]', '151');
    await submit();
    expect(host.submitted).toEqual([]);

    set(root, 'input[type=number]', '100');
    set(root, 'select[formcontrolname=method]', 'Card');
    await submit();
    expect(host.submitted).toEqual([]);

    set(root, 'input[formcontrolname=reference]', ' SLIP-1 ');
    await submit();
    expect(host.submitted).toEqual([{ treasuryId: 't-1', amount: 100, method: 'Card', reference: 'SLIP-1', reasonId: null, note: null }]);
  });

  it('requires a reason in refund mode and passes the note', async () => {
    const { host, root, submit } = await render();
    host.mode.set('refund');
    host.due.set(40);
    TestBed.tick();

    await submit();
    expect(host.submitted).toEqual([]);

    set(root, 'select[formcontrolname=reasonId]', 'r-1');
    set(root, 'textarea', ' contract transfer ');
    await submit();
    expect(host.submitted).toEqual([{ treasuryId: 't-1', amount: 40, method: 'Cash', reference: null, reasonId: 'r-1', note: 'contract transfer' }]);
  });

  it('starts the next movement from a fresh form after a save: no previous note, reason, method or reference (UAT A-06)', async () => {
    const { host, root, submit } = await render();
    host.mode.set('refund');
    host.due.set(400);
    TestBed.tick();
    set(root, 'select[formcontrolname=method]', 'Card');
    set(root, 'input[formcontrolname=reference]', 'SLIP-7');
    set(root, 'select[formcontrolname=reasonId]', 'r-1');
    set(root, 'textarea', 'first refund note');
    set(root, 'input[type=number]', '200');
    await submit();
    expect(host.submitted.length).toBe(1);

    host.form().reset();   // what the page does once the save succeeded
    TestBed.tick();
    expect(field<HTMLTextAreaElement>(root, 'textarea').value).toBe('');
    expect(field<HTMLInputElement>(root, 'input[formcontrolname=reference]').value).toBe('');
    expect(field<HTMLSelectElement>(root, 'select[formcontrolname=method]').value).toBe('Cash');
    expect(field<HTMLInputElement>(root, 'input[type=number]').value).toBe('400');

    await submit();
    expect(host.submitted.length).toBe(1);   // the reason must be chosen again

    set(root, 'select[formcontrolname=reasonId]', 'r-1');
    await submit();
    expect(host.submitted[1]).toEqual({ treasuryId: 't-1', amount: 400, method: 'Cash', reference: null, reasonId: 'r-1', note: null });
  });
});
