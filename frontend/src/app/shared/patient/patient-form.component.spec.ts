import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import type { CountryOption, GovernorateOption, PatientFormSettings, RegisterPatientRequest } from '../../core/api/patients.contracts';
import { PermissionsService } from '../../core/permissions/permissions.service';
import { PatientFormComponent } from './patient-form.component';
import { ageBetween, todayIso } from './patient-demographics';

@Component({
  imports: [PatientFormComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-patient-form [settings]="settings()" [countries]="countries()" [governorates]="governorates()" (save)="saved.push($event)" />`,
})
class HostComponent {
  readonly settings = signal<PatientFormSettings | null>(null);
  readonly countries = signal<readonly CountryOption[]>([]);
  readonly governorates = signal<readonly GovernorateOption[]>([]);
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

  it('says why a save was blocked and focuses the first marked field (validation round 4)', async () => {
    const { fixture, host } = await render();
    document.body.appendChild(host);

    expect(fixture.componentInstance.form().submit()).toBe(false);
    await fixture.whenStable();
    expect(host.querySelector('.problem.blocked[role="alert"]')?.textContent).toContain('shared.masterData.invalid');
    expect(document.activeElement).toBe(host.querySelector('[formcontrolname="fullName"]'));

    type(host, 'fullName', 'Mona');
    await fixture.whenStable();
    expect(fixture.componentInstance.form().submit()).toBe(false);   // date of birth or age still missing
    await fixture.whenStable();
    expect(document.activeElement).toBe(host.querySelector('[formcontrolname="dateOfBirth"]'));

    type(host, 'ageYears', '30');
    await fixture.whenStable();
    expect(fixture.componentInstance.form().submit()).toBe(true);
    await fixture.whenStable();
    expect(host.querySelector('.problem.blocked')).toBeNull();
    host.remove();
  });

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

  describe('Egyptian national id', () => {
    const countries: readonly CountryOption[] = [
      { id: 'c-eg', code: 'EG', nameEn: 'Egypt', nameAr: 'مصر', mobileLength: 10 },
      { id: 'c-sa', code: 'SA', nameEn: 'Saudi Arabia', nameAr: 'السعودية', mobileLength: 9 },
    ];
    const governorates: readonly GovernorateOption[] = [
      { id: 'g-cairo', nameEn: 'Cairo', nameAr: 'القاهرة', countryId: 'c-eg' },
      { id: 'g-giza', nameEn: 'Giza', nameAr: 'الجيزة', countryId: 'c-eg' },
    ];

    async function renderWithLists(): Promise<Awaited<ReturnType<typeof render>>> {
      const rendered = await render();
      rendered.fixture.componentInstance.countries.set(countries);
      rendered.fixture.componentInstance.governorates.set(governorates);
      await rendered.fixture.whenStable();
      return rendered;
    }

    it('accepts an Egyptian mobile with the trunk 0 or without it and saves both as 01xxxxxxxxx (remaining point A-09)', async () => {
      for (const typed of ['01011140001', '1011140001']) {
        const { fixture, host } = await renderWithLists();
        const country = host.querySelector<HTMLSelectElement>('.mobile select')!;
        country.value = 'EG';
        country.dispatchEvent(new Event('change'));
        type(host, 'fullName', 'Walk In');
        type(host, 'ageYears', '30');
        type(host, 'mobile', typed);
        await fixture.whenStable();

        expect(fixture.componentInstance.form().submit()).toBe(true);
        expect(fixture.componentInstance.saved.at(-1)?.mobile).toBe('01011140001');
        fixture.destroy();
      }
    });

    it('F-08: opens on the laboratory country, so a 10-digit number is stored with the trunk 0 without choosing Egypt', async () => {
      const { fixture, host } = await renderWithLists();
      fixture.componentInstance.settings.set({ mandatoryMobile: false, mandatoryNationalId: false, similarNameWarning: true, countryCode: 'EG', countryMobileLength: 10 });
      await fixture.whenStable();
      const country = host.querySelector<HTMLSelectElement>('.mobile select')!;
      expect(country.value).toBe('EG');
      type(host, 'fullName', 'Mobile Ten');
      type(host, 'ageYears', '30');
      type(host, 'mobile', '1019199912');
      await fixture.whenStable();
      expect(fixture.componentInstance.form().submit()).toBe(true);
      expect(fixture.componentInstance.saved.at(-1)?.mobile).toBe('01019199912');

      // "Any", chosen on purpose, keeps the number as typed (PAT Q14)
      country.value = '';
      country.dispatchEvent(new Event('change'));
      await fixture.whenStable();
      expect(fixture.componentInstance.form().submit()).toBe(true);
      expect(fixture.componentInstance.saved.at(-1)?.mobile).toBe('1019199912');
      fixture.destroy();
    });

    it('F-08: stays on "Any" when the laboratory names no country or one without a mobile length', async () => {
      const { fixture, host } = await renderWithLists();
      fixture.componentInstance.settings.set({ mandatoryMobile: false, mandatoryNationalId: false, similarNameWarning: true, countryCode: 'KW', countryMobileLength: 8 });
      await fixture.whenStable();
      expect(host.querySelector<HTMLSelectElement>('.mobile select')!.value).toBe('');
      fixture.destroy();
    });

    it('fills the empty date of birth, gender and governorate', async () => {
      const { fixture, host } = await renderWithLists();
      type(host, 'nationalId', '29601010100022');
      await fixture.whenStable();

      expect(value(host, 'dateOfBirth')).toBe('1996-01-01');
      expect(value(host, 'gender')).toBe('Female');
      expect(value(host, 'governorateId')).toBe('g-cairo');
      expect(value(host, 'title')).toBe('Ms');   // the untouched default "Mr" follows the id
      expect(value(host, 'ageYears')).not.toBe('');
      expect(host.textContent).toContain('patients.nationalId.filled');
      expect(host.querySelector('.id-conflicts')).toBeNull();

      // a corrected id moves the values it filled
      type(host, 'nationalId', '28512252101235');
      await fixture.whenStable();
      expect(value(host, 'dateOfBirth')).toBe('1985-12-25');
      expect(value(host, 'gender')).toBe('Male');
      expect(value(host, 'governorateId')).toBe('g-giza');

      type(host, 'fullName', 'Omar');
      await fixture.whenStable();
      expect(fixture.componentInstance.form().submit()).toBe(true);
      expect(fixture.componentInstance.saved[0]).toEqual(expect.objectContaining({ dateOfBirth: '1985-12-25', gender: 'Male', governorateId: 'g-giza', nationalId: '28512252101235' }));
    });

    it('never overwrites what the clerk entered and lists the contradictions', async () => {
      const { fixture, host } = await renderWithLists();
      type(host, 'dateOfBirth', '1990-05-05');
      host.querySelector<HTMLInputElement>('input[type="radio"][value="Female"]')?.click();
      await fixture.whenStable();

      type(host, 'nationalId', '28512252101235');   // a man born 1985-12-25 in Giza
      await fixture.whenStable();
      expect(value(host, 'dateOfBirth')).toBe('1990-05-05');
      expect(value(host, 'gender')).toBe('Female');
      expect(value(host, 'governorateId')).toBe('g-giza');   // empty, so filled
      const conflicts = host.querySelector('.id-conflicts')?.textContent ?? '';
      expect(conflicts).toContain('patients.nationalId.birthDiffers');
      expect(conflicts).toContain('patients.nationalId.genderDiffers');
    });

    it('reads nothing when the nationality is not Egyptian or the id is not an Egyptian one', async () => {
      const { fixture, host } = await renderWithLists();
      type(host, 'nationalId', 'A1234567');
      await fixture.whenStable();
      expect(value(host, 'dateOfBirth')).toBe('');

      type(host, 'nationality', 'SA');
      type(host, 'nationalId', '29601010100022');
      await fixture.whenStable();
      expect(value(host, 'dateOfBirth')).toBe('');
      expect(value(host, 'governorateId')).toBe('');
    });
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
