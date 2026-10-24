import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { BillingReasonOption, PaymentMethodOption } from '../../../core/api/billing.contracts';
import { methodOptions } from '../testing/billing.fixtures';
import { MovementFormComponent, type MovementInput, type MovementMode } from './movement-form.component';

const reasons: BillingReasonOption[] = [{ id: 'r-1', kind: 'Refund', nameEn: 'Overpayment', nameAr: 'مبلغ زائد', isActive: true }];

@Component({
  selector: 'app-movement-form-host',
  imports: [MovementFormComponent],
  template: `<app-billing-movement-form [mode]="mode()" [methods]="methods()" [dueAmount]="due()" [reasons]="reasons" submitLabel="Pay" (submitted)="submitted.push($event)" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class HostComponent {
  readonly mode = signal<MovementMode>('payment');
  readonly due = signal(150);
  readonly methods = signal<readonly PaymentMethodOption[]>([]);
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

  it('puts the cursor in the amount with the proposed value selected (LIS-CR-007 C-07)', async () => {
    const { host, root } = await render();
    const amount = field<HTMLInputElement>(root, 'input[formcontrolname=amount]');
    document.body.appendChild(root);
    try {
      host.form().focusAmount();
      expect(document.activeElement).toBe(amount);
      expect(amount.value).toBe('150');
    } finally {
      root.remove();
    }
  });

  it('proposes what is due, refuses more than that and a non-cash payment without reference', async () => {
    const { host, root, submit } = await render();
    expect(root.querySelector('select[formcontrolname=treasuryId]')).toBeNull();   // LIS-CR-004: no treasury to choose
    expect(field<HTMLInputElement>(root, 'input[type=number]').value).toBe('150');

    set(root, 'input[type=number]', '151');
    await submit();
    expect(host.submitted).toEqual([]);

    set(root, 'input[type=number]', '100');
    set(root, 'select[formcontrolname=methodKey]', 'Card');
    await submit();
    expect(host.submitted).toEqual([]);

    set(root, 'input[formcontrolname=reference]', ' SLIP-1 ');
    await submit();
    expect(host.submitted).toEqual([{ amount: 100, method: 'Card', methodCode: null, reference: 'SLIP-1', reasonId: null, note: null }]);
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
    expect(host.submitted).toEqual([{ amount: 40, method: 'Cash', methodCode: null, reference: null, reasonId: 'r-1', note: 'contract transfer' }]);
  });

  it('starts the next movement from a fresh form after a save: no previous note, reason, method or reference (UAT A-06)', async () => {
    const { host, root, submit } = await render();
    host.mode.set('refund');
    host.due.set(400);
    TestBed.tick();
    set(root, 'select[formcontrolname=methodKey]', 'Card');
    TestBed.tick();   // the reference field appears once the method requires it
    set(root, 'input[formcontrolname=reference]', 'SLIP-7');
    set(root, 'select[formcontrolname=reasonId]', 'r-1');
    set(root, 'textarea', 'first refund note');
    set(root, 'input[type=number]', '200');
    await submit();
    expect(host.submitted.length).toBe(1);

    host.form().reset();   // what the page does once the save succeeded
    TestBed.tick();
    expect(field<HTMLTextAreaElement>(root, 'textarea').value).toBe('');
    expect(root.querySelector('input[formcontrolname=reference]')).toBeNull();   // cash asks for no reference, so the field is not shown
    expect(field<HTMLSelectElement>(root, 'select[formcontrolname=methodKey]').value).toBe('Cash');
    expect(field<HTMLInputElement>(root, 'input[type=number]').value).toBe('400');

    await submit();
    expect(host.submitted.length).toBe(1);   // the reason must be chosen again

    set(root, 'select[formcontrolname=reasonId]', 'r-1');
    await submit();
    expect(host.submitted[1]).toEqual({ amount: 400, method: 'Cash', methodCode: null, reference: null, reasonId: 'r-1', note: null });
  });

  it("offers the laboratory's payment methods by name, asks for the reference only when the chosen one requires it and sends kind and code (CP-30b)", async () => {
    const { host, root, submit } = await render();
    host.methods.set([...methodOptions, { id: 'pm-wallet', code: 'WALLET', nameEn: 'Wallet', nameAr: 'محفظة', kind: 'Card', requiresReference: false }]);
    TestBed.tick();
    await submit();

    // the options replace the kinds; the first one (CASH) is the default and needs no reference
    const select = field<HTMLSelectElement>(root, 'select[formcontrolname=methodKey]');
    expect(Array.from(select.options).map((option) => option.value)).toEqual(['CASH', 'CARD', 'POS', 'TRANSFER', 'WALLET']);
    expect(select.options[1]?.textContent?.trim()).toBe('Card');
    expect(host.submitted).toEqual([{ amount: 150, method: 'Cash', methodCode: 'CASH', reference: null, reasonId: null, note: null }]);

    // a Card-kind definition without the rule: no reference field, the kind travels with the code
    set(root, 'select[formcontrolname=methodKey]', 'WALLET');
    TestBed.tick();
    expect(root.querySelector('input[formcontrolname=reference]')).toBeNull();
    await submit();
    expect(host.submitted[1]).toEqual({ amount: 150, method: 'Card', methodCode: 'WALLET', reference: null, reasonId: null, note: null });

    // CARD requires the reference (BIL-003): blocked until typed
    set(root, 'select[formcontrolname=methodKey]', 'CARD');
    TestBed.tick();
    await submit();
    expect(host.submitted.length).toBe(2);
    set(root, 'input[formcontrolname=reference]', 'SLIP-9');
    await submit();
    expect(host.submitted[2]).toEqual({ amount: 150, method: 'Card', methodCode: 'CARD', reference: 'SLIP-9', reasonId: null, note: null });

    // the definitions vanish (a refused re-read): the form falls back to the kinds and the legacy rule
    host.methods.set([]);
    TestBed.tick();
    expect(field<HTMLSelectElement>(root, 'select[formcontrolname=methodKey]').value).toBe('Cash');
    expect(root.querySelector('input[formcontrolname=reference]')).toBeNull();
  });
});
