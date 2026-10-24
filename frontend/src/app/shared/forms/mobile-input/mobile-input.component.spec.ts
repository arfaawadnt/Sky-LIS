import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';

import { MobileInputComponent } from './mobile-input.component';
import type { MobileDialCountry } from './mobile-number';

const EGYPT: MobileDialCountry = { dialCode: '+20', mobileLength: 10, isoCode: 'EG', nameEn: 'Egypt', nameAr: 'مصر' };
const SAUDI: MobileDialCountry = { dialCode: '+966', mobileLength: 9, isoCode: 'SA', nameEn: 'Saudi Arabia', nameAr: 'السعودية' };

@Component({
  imports: [ReactiveFormsModule, MobileInputComponent],
  template: `<app-mobile-input [formControl]="control" [country]="country()" [countries]="countries()" [defaultCountry]="defaultCountry()" (countryChange)="shown.push($event)" (edited)="edits.push($event)" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
class HostComponent {
  readonly control = new FormControl('', { nonNullable: true });
  readonly country = signal<MobileDialCountry | null>(null);
  readonly countries = signal<readonly MobileDialCountry[]>([]);
  readonly defaultCountry = signal<string | null>(null);
  readonly shown: (MobileDialCountry | null)[] = [];
  readonly edits: string[] = [];
}

function paste(input: HTMLInputElement, text: string): void {
  const event = new Event('paste', { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'clipboardData', { value: { getData: () => text } });
  input.dispatchEvent(event);
}

function type(input: HTMLInputElement, text: string): void {
  input.value = text;
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

describe('MobileInputComponent (LIS-CR-007 C-05)', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  let root: HTMLElement;

  async function render(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
  }

  const number = (): HTMLInputElement => root.querySelector<HTMLInputElement>('input.number')!;

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideZonelessChangeDetection()] });
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    root = fixture.nativeElement as HTMLElement;
    await render();
  });

  it('shows the fixed dial code read-only and sends the E.164 value without the trunk 0', async () => {
    host.country.set(EGYPT);
    await render();
    expect(root.querySelector('.dial.fixed')?.textContent).toContain('+20');
    expect(root.querySelector('select')).toBeNull();
    expect(number().getAttribute('placeholder')).toBe('shared.mobileInput.lengthHint');

    type(number(), '01004439855');
    expect(host.control.value).toBe('+201004439855');
    expect(host.control.dirty).toBe(true);
    expect(host.edits).toEqual(['+201004439855']);

    number().dispatchEvent(new Event('blur'));
    await render();
    expect(number().value).toBe('1004439855');
    expect(host.control.touched).toBe(true);
  });

  it('recognises a pasted international number and moves the dial code to its country', async () => {
    host.countries.set([EGYPT, SAUDI]);
    host.defaultCountry.set('EG');
    await render();
    const select = root.querySelector<HTMLSelectElement>('select.dial')!;
    expect(select.value).toBe('EG');

    paste(number(), '+966 50 123 4567');
    await render();
    expect(host.control.value).toBe('+966501234567');
    expect(number().value).toBe('501234567');
    expect(select.value).toBe('SA');
    expect(host.shown.at(-1)).toEqual(SAUDI);

    type(number(), '');
    expect(host.control.value).toBe('');
    paste(number(), '0020 100 443 9855');
    await render();
    expect(host.control.value).toBe('+201004439855');
    expect(select.value).toBe('EG');
  });

  it('changes the value when another dial code is chosen under a typed number', async () => {
    host.countries.set([EGYPT, SAUDI]);
    await render();
    type(number(), '501234567');
    expect(host.control.value).toBe('+20501234567');

    const select = root.querySelector<HTMLSelectElement>('select.dial')!;
    select.value = 'SA';
    select.dispatchEvent(new Event('change'));
    expect(host.control.value).toBe('+966501234567');
  });

  it('reads a written value into the two parts without marking the form dirty', async () => {
    host.countries.set([EGYPT, SAUDI]);
    await render();
    host.control.setValue('+966501234567');
    await render();
    expect(number().value).toBe('501234567');
    expect(root.querySelector<HTMLSelectElement>('select.dial')!.value).toBe('SA');
    expect(host.control.dirty).toBe(false);
    expect(host.control.value).toBe('+966501234567');

    host.control.setValue('01001234567');
    await render();
    expect(number().value).toBe('1001234567');
    expect(host.control.value).toBe('01001234567');

    host.control.reset('');
    await render();
    expect(number().value).toBe('');
  });

  it('re-reads a written international value when the list arrives after it', async () => {
    host.control.setValue('+201004439855');
    await render();
    expect(number().value).toBe('+201004439855');

    host.countries.set([SAUDI, EGYPT]);
    await render();
    expect(number().value).toBe('1004439855');
    expect(root.querySelector<HTMLSelectElement>('select.dial')!.value).toBe('EG');
    expect(host.control.dirty).toBe(false);
  });

  it('takes a typed dial code without a list and follows the form country picked after the number', async () => {
    const free = root.querySelector<HTMLInputElement>('input.dial.free')!;
    type(free, '966');
    type(number(), '0501234567');
    expect(host.control.value).toBe('+966501234567');

    host.country.set(EGYPT);
    await render();
    expect(host.control.value).toBe('+20501234567');
  });

  it('answers value and disabled on its host like a native input, and takes a value set there with an input event', async () => {
    host.country.set(EGYPT);
    await render();
    const element = root.querySelector('app-mobile-input') as HTMLElement & { value: string; disabled: boolean };
    element.value = '01112223333';
    element.dispatchEvent(new Event('input', { bubbles: true }));
    expect(host.control.value).toBe('+201112223333');
    expect(element.value).toBe('+201112223333');
    expect(host.edits).toEqual(['+201112223333']);
    expect(element.disabled).toBe(false);
  });

  it('passes text that is not a number on, so the form refuses it, and disables both parts', async () => {
    host.country.set(EGYPT);
    await render();
    type(number(), '12ab');
    expect(host.control.value).toBe('12ab');

    host.control.disable();
    await render();
    expect(number().disabled).toBe(true);
  });
});
