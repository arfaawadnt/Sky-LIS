import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { PatientFormSettings, RegisterPatientRequest } from '../../core/api/patients.contracts';
import { PermissionsService } from '../../core/permissions/permissions.service';
import { PatientFormComponent } from './patient-form.component';
import { ageBetween, todayIso } from './patient-demographics';

@Component({
  imports: [PatientFormComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-patient-form [settings]="settings()" (save)="saved.push($event)" />`,
})
class HostComponent {
  readonly settings = signal<PatientFormSettings | null>(null);
  readonly saved: RegisterPatientRequest[] = [];
  readonly form = viewChild.required(PatientFormComponent);
}

let granted = new Set<string>();

function type(host: HTMLElement, control: string, value: string): void {
  const element = host.querySelector<HTMLInputElement | HTMLSelectElement>(`[formcontrolname="${control}"]`);
  if (!element) {
    throw new Error(`${control} not found`);
  }

  element.value = value;
  element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }));
}

function value(host: HTMLElement, control: string): string {
  // the gender is a radio switch: its value is the checked option's
  const checked = host.querySelector<HTMLInputElement>(`input[type="radio"][formcontrolname="${control}"]:checked`);
  return checked?.value ?? host.querySelector<HTMLInputElement | HTMLSelectElement>(`[formcontrolname="${control}"]`)?.value ?? '';
}

describe('PatientFormComponent (CP-06 / CP-11 patient data)', () => {
  beforeEach(async () => {
    granted = new Set<string>();
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideZonelessChangeDetection(), { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> }],
    }).compileComponents();
  });

  async function render(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<HostComponent>>; host: HTMLElement }> {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    return { fixture, host: fixture.nativeElement as HTMLElement };
  }

  it('lets the title propose the gender', async () => {
    const { fixture, host } = await render();

    type(host, 'title', 'Mrs');
    await fixture.whenStable();
    expect(value(host, 'gender')).toBe('Female');

    type(host, 'title', 'Dr');
    await fixture.whenStable();
    expect(value(host, 'gender')).toBe('Female');   // no default for Dr: the previous choice stays

    type(host, 'title', 'Master');
    await fixture.whenStable();
    expect(value(host, 'gender')).toBe('Male');
  });

  it('syncs the age from the date of birth and saves the date', async () => {
    const { fixture, host } = await render();

    type(host, 'fullName', '  Mona Hassan ');
    type(host, 'dateOfBirth', '1990-05-01');
    type(host, 'mobile', '0100 123 4567');
    await fixture.whenStable();
    const age = ageBetween('1990-05-01', todayIso());
    expect(value(host, 'ageYears')).toBe(String(age.years));
    expect(value(host, 'ageMonths')).toBe(String(age.months));

    expect(fixture.componentInstance.form().submit()).toBe(true);
    const saved = fixture.componentInstance.saved[0];
    expect(saved?.fullName).toBe('Mona Hassan');
    expect(saved?.dateOfBirth).toBe('1990-05-01');
    expect(saved?.age).toBeNull();
    expect(saved?.mobile).toBe('01001234567');
    expect(saved?.isVip).toBeNull();   // the VIP field is not shown without patients.viewConfidential
  });

  it('derives the date of birth from a typed age and saves the age', async () => {
    const { fixture, host } = await render();

    type(host, 'fullName', 'Omar Said');
    type(host, 'ageYears', '40');
    type(host, 'ageMonths', '6');
    await fixture.whenStable();
    expect(value(host, 'dateOfBirth')).not.toBe('');
    expect(ageBetween(value(host, 'dateOfBirth'), todayIso())).toEqual({ years: 40, months: 6, days: 0 });

    fixture.componentInstance.form().submit();
    expect(fixture.componentInstance.saved[0]?.dateOfBirth).toBeNull();
    expect(fixture.componentInstance.saved[0]?.age).toEqual({ years: 40, months: 6, days: 0 });
  });

  it('refuses to save without a name, without a birth date or age, or with a malformed mobile', async () => {
    const { fixture, host } = await render();

    expect(fixture.componentInstance.form().submit()).toBe(false);
    await fixture.whenStable();
    expect(host.querySelector('.field-error')?.textContent).toContain('patients.validation.birthOrAge');

    type(host, 'fullName', 'Ali');
    type(host, 'ageYears', '30');
    type(host, 'mobile', '12ab');
    await fixture.whenStable();
    expect(fixture.componentInstance.form().submit()).toBe(false);
    expect(fixture.componentInstance.saved).toEqual([]);
  });

  it('makes the mobile mandatory when the tenant parameter says so and shows VIP to the confidential right', async () => {
    granted = new Set(['patients.viewConfidential']);
    const { fixture, host } = await render();
    fixture.componentInstance.settings.set({ mandatoryMobile: true, mandatoryNationalId: false, similarNameWarning: true });
    await fixture.whenStable();

    type(host, 'fullName', 'Ali');
    type(host, 'ageYears', '30');
    await fixture.whenStable();
    expect(fixture.componentInstance.form().submit()).toBe(false);

    type(host, 'mobile', '01001234567');
    const vip = host.querySelector<HTMLInputElement>('[formcontrolname="isVip"]');
    expect(vip).not.toBeNull();
    vip?.click();
    await fixture.whenStable();
    expect(fixture.componentInstance.form().submit()).toBe(true);
    expect(fixture.componentInstance.saved[0]?.isVip).toBe(true);
  });

  it('folds the optional details and opens them when a field in them refuses the save', async () => {
    const { fixture, host } = await render();
    const more = host.querySelector<HTMLDetailsElement>('details.more');
    expect(more?.open).toBe(false);

    type(host, 'fullName', 'Ali');
    type(host, 'ageYears', '30');
    type(host, 'email', 'not-an-address');
    await fixture.whenStable();
    expect(fixture.componentInstance.form().submit()).toBe(false);
    await fixture.whenStable();
    expect(more?.open).toBe(true);
  });

  it('picks the gender with the switch', async () => {
    const { fixture, host } = await render();
    type(host, 'fullName', 'Mona');
    type(host, 'ageYears', '30');
    host.querySelector<HTMLInputElement>('input[type="radio"][value="Female"]')?.click();
    await fixture.whenStable();
    expect(fixture.componentInstance.form().submit()).toBe(true);
    expect(fixture.componentInstance.saved[0]?.gender).toBe('Female');
  });
});
