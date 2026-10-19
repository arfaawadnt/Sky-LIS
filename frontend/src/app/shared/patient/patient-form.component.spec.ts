import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { type CountryOption, type GovernorateOption, PATIENTS_API, type PatientDetail, type PatientFormSettings, type RegisterPatientRequest } from '../../core/api/patients.contracts';
import { PermissionsService } from '../../core/permissions/permissions.service';
import { PatientFormComponent, type TypedIdentity } from './patient-form.component';
import { ageBetween, todayIso } from './patient-demographics';

@Component({
  imports: [PatientFormComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-patient-form [patient]="patient()" [settings]="settings()" [countries]="countries()" [governorates]="governorates()" [identityLocked]="locked()" [lockHint]="hint()" (save)="saved.push($event)" />`,
})
class HostComponent {
  readonly patient = signal<PatientDetail | null>(null);
  readonly settings = signal<PatientFormSettings | null>(null);
  readonly countries = signal<readonly CountryOption[]>([]);
  readonly governorates = signal<readonly GovernorateOption[]>([]);
  readonly locked = signal(false);
  readonly hint = signal<string | null>(null);
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

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 0));

/** Lets a flushed answer land, runs the effects (the lookups of the location hierarchy start) and lets the request reach the backend. */
async function tick(): Promise<void> {
  await settle();
  TestBed.tick();
  await settle();
}

/** Answers every pending public location lookup with an empty list, so `whenStable` has nothing to wait for. */
async function flushLookups(): Promise<void> {
  await tick();
  const backend = TestBed.inject(HttpTestingController);
  backend.match((r) => r.url === PATIENTS_API.cities || r.url === PATIENTS_API.districts || r.url === PATIENTS_API.areas).forEach((request) => request.flush([]));
  await settle();
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
      providers: [
        provideZonelessChangeDetection(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PermissionsService, useValue: { can: (key: string) => granted.has(key) } satisfies Pick<PermissionsService, 'can'> },
      ],
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

  it('freezes the identity fields under a demographics lock and still saves them unchanged with the edited contact (PAT-032)', async () => {
    const { fixture, host } = await render();
    fixture.componentInstance.patient.set({
      id: 'p-1', number: 'PAT-2026-00001', title: 'Mrs', fullName: 'Mona Hassan', nameAr: 'منى حسن', gender: 'Female', dateOfBirth: '1990-05-01', dateOfBirthIsEstimated: false,
      age: ageBetween('1990-05-01', todayIso()), statedAge: null, ageRecordedOn: null, registeredOn: '2026-10-01', patientConditionId: null, mobile: '01001234567', nationalId: '29005010101234',
      nationality: 'EG', email: null, governorateId: null, city: 'Cairo', address: null, comments: null, isVip: null, status: 'Active', merge: null, mergedFiles: [],
      createdAtUtc: '2026-10-01T08:00:00Z', createdByName: null, modifiedAtUtc: null, modifiedByName: null, rowVersion: 3,
    });
    fixture.componentInstance.locked.set(true);
    fixture.componentInstance.hint.set('locked at Verified');
    await fixture.whenStable();

    expect(host.querySelector('.lock-hint')?.textContent).toContain('locked at Verified');
    for (const field of ['title', 'fullName', 'nameAr', 'dateOfBirth', 'ageYears', 'ageMonths', 'ageDays', 'nationalId', 'nationality']) {
      expect(host.querySelector<HTMLInputElement>(`[formcontrolname="${field}"]`)?.disabled).toBe(true);
    }

    expect(host.querySelectorAll<HTMLInputElement>('input[type="radio"][formcontrolname="gender"]').length).toBeGreaterThan(0);
    host.querySelectorAll<HTMLInputElement>('input[type="radio"][formcontrolname="gender"]').forEach((radio) => expect(radio.disabled).toBe(true));
    for (const field of ['mobile', 'email', 'city', 'address', 'comments']) {
      expect(host.querySelector<HTMLInputElement>(`[formcontrolname="${field}"]`)?.disabled).toBe(false);
    }

    type(host, 'mobile', '01112223333');
    await fixture.whenStable();
    expect(fixture.componentInstance.form().submit()).toBe(true);
    const saved = fixture.componentInstance.saved[0]!;
    expect(saved).toEqual(expect.objectContaining({ title: 'Mrs', fullName: 'Mona Hassan', nameAr: 'منى حسن', gender: 'Female', dateOfBirth: '1990-05-01', nationalId: '29005010101234', nationality: 'EG', mobile: '01112223333' }));

    fixture.componentInstance.locked.set(false);
    await fixture.whenStable();
    expect(host.querySelector('.lock-hint')).toBeNull();
    expect(host.querySelector<HTMLInputElement>('[formcontrolname="fullName"]')?.disabled).toBe(false);
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
      await flushLookups();
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
      await flushLookups();
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
      await flushLookups();
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

  describe('location hierarchy and second phone (LIS-CR-003 §3, A2 / C3)', () => {
    const governorates: GovernorateOption[] = [
      { id: 'g-cairo', nameEn: 'Cairo', nameAr: 'القاهرة', countryId: 'c-eg' },
      { id: 'g-aswan', nameEn: 'Aswan', nameAr: 'أسوان', countryId: 'c-eg' },
    ];

    function openMore(host: HTMLElement): void {
      const details = host.querySelector<HTMLDetailsElement>('details.more');
      if (details) {
        details.open = true;
        details.dispatchEvent(new Event('toggle'));
      }
    }

    async function renderWithGovernorates(): Promise<Awaited<ReturnType<typeof render>> & { backend: HttpTestingController }> {
      const rendered = await render();
      rendered.fixture.componentInstance.governorates.set(governorates);
      await rendered.fixture.whenStable();
      openMore(rendered.host);
      await rendered.fixture.whenStable();
      return { ...rendered, backend: TestBed.inject(HttpTestingController) };
    }

    function lookup(backend: HttpTestingController, url: string, parent: [string, string]): ReturnType<HttpTestingController['expectOne']> {
      const request = backend.expectOne((r) => r.url === url);
      expect(request.request.params.get(parent[0])).toBe(parent[1]);
      return request;
    }

    it('replaces the free-text city with linked selects when the governorate has cities, and saves the chain (A2)', async () => {
      const { fixture, host, backend } = await renderWithGovernorates();
      expect(host.querySelector('input[formcontrolname="city"]')).not.toBeNull();

      type(host, 'governorateId', 'g-cairo');
      await tick();
      lookup(backend, PATIENTS_API.cities, ['governorateId', 'g-cairo']).flush([
        { id: 'c-nasr', governorateId: 'g-cairo', nameEn: 'Nasr City', nameAr: 'مدينة نصر' },
        { id: 'c-maadi', governorateId: 'g-cairo', nameEn: 'Maadi', nameAr: 'المعادي' },
      ]);
      await tick();
      expect(host.querySelector('input[formcontrolname="city"]')).toBeNull();
      expect(host.querySelectorAll('select[formcontrolname="cityId"] option').length).toBe(3);
      expect(host.querySelector('select[formcontrolname="districtId"]')).toBeNull();   // nothing picked yet

      type(host, 'cityId', 'c-nasr');
      await tick();
      lookup(backend, PATIENTS_API.districts, ['cityId', 'c-nasr']).flush([{ id: 'd-7', cityId: 'c-nasr', nameEn: '7th district', nameAr: 'الحي السابع' }]);
      await tick();
      type(host, 'districtId', 'd-7');
      await tick();
      lookup(backend, PATIENTS_API.areas, ['districtId', 'd-7']).flush([{ id: 'a-1', districtId: 'd-7', nameEn: 'Area 1', nameAr: 'منطقة 1' }]);
      await tick();
      type(host, 'areaId', 'a-1');

      type(host, 'fullName', 'Mona');
      type(host, 'ageYears', '30');
      await tick();
      expect(fixture.componentInstance.form().submit()).toBe(true);
      // the server names the city from the picked id: no free text travels beside it
      expect(fixture.componentInstance.saved[0]).toEqual(expect.objectContaining({ governorateId: 'g-cairo', cityId: 'c-nasr', districtId: 'd-7', areaId: 'a-1', city: null }));
    });

    it('clears the levels below a changed one, so nothing outside the chain is sent (PAT-014…016)', async () => {
      const { fixture, host, backend } = await renderWithGovernorates();
      type(host, 'governorateId', 'g-cairo');
      await tick();
      lookup(backend, PATIENTS_API.cities, ['governorateId', 'g-cairo']).flush([{ id: 'c-nasr', governorateId: 'g-cairo', nameEn: 'Nasr City', nameAr: 'مدينة نصر' }]);
      await tick();
      type(host, 'cityId', 'c-nasr');
      await tick();
      lookup(backend, PATIENTS_API.districts, ['cityId', 'c-nasr']).flush([{ id: 'd-7', cityId: 'c-nasr', nameEn: '7th district', nameAr: 'الحي السابع' }]);
      await tick();
      type(host, 'districtId', 'd-7');
      await tick();
      lookup(backend, PATIENTS_API.areas, ['districtId', 'd-7']).flush([]);
      await tick();

      type(host, 'governorateId', 'g-aswan');
      await tick();
      lookup(backend, PATIENTS_API.cities, ['governorateId', 'g-aswan']).flush([]);   // Aswan lists no cities: free text again
      await tick();
      expect(host.querySelector('input[formcontrolname="city"]')).not.toBeNull();
      expect(host.querySelector('select[formcontrolname="cityId"]')).toBeNull();

      type(host, 'city', 'Kom Ombo');
      type(host, 'fullName', 'Mona');
      type(host, 'ageYears', '30');
      await tick();
      expect(fixture.componentInstance.form().submit()).toBe(true);
      expect(fixture.componentInstance.saved[0]).toEqual(expect.objectContaining({ governorateId: 'g-aswan', cityId: null, districtId: null, areaId: null, city: 'Kom Ombo' }));
    });

    it('fills the selects from the file being edited', async () => {
      const { fixture, host, backend } = await renderWithGovernorates();
      fixture.componentInstance.patient.set({
        id: 'p-1', number: 'PAT-2026-00001', title: 'Mrs', fullName: 'Mona Hassan', nameAr: null, gender: 'Female', dateOfBirth: '1990-05-01', dateOfBirthIsEstimated: false,
        age: ageBetween('1990-05-01', todayIso()), statedAge: null, ageRecordedOn: null, registeredOn: '2026-10-01', patientConditionId: null, mobile: '01001234567', nationalId: null,
        nationality: null, email: null, governorateId: 'g-cairo', city: 'Maadi', address: null, comments: null, isVip: null, status: 'Active', merge: null, mergedFiles: [],
        createdAtUtc: '2026-10-01T08:00:00Z', createdByName: null, modifiedAtUtc: null, modifiedByName: null, rowVersion: 3, secondPhone: '01112223333',
        location: { cityId: 'c-maadi', cityNameEn: 'Maadi', cityNameAr: 'المعادي', districtId: null, districtNameEn: null, districtNameAr: null, areaId: null, areaNameEn: null, areaNameAr: null },
      });
      await tick();
      lookup(backend, PATIENTS_API.cities, ['governorateId', 'g-cairo']).flush([{ id: 'c-maadi', governorateId: 'g-cairo', nameEn: 'Maadi', nameAr: 'المعادي' }]);
      await tick();
      lookup(backend, PATIENTS_API.districts, ['cityId', 'c-maadi']).flush([]);
      await tick();

      expect(value(host, 'cityId')).toBe('c-maadi');
      expect(value(host, 'secondPhone')).toBe('01112223333');
      expect(fixture.componentInstance.form().submit()).toBe(true);
      expect(fixture.componentInstance.saved[0]).toEqual(expect.objectContaining({ cityId: 'c-maadi', districtId: null, areaId: null, city: null, secondPhone: '01112223333' }));
    });

    it('saves the second phone cleaned like the mobile and types it into the similar-patient lookup (C3)', async () => {
      const { fixture, host } = await render();
      fixture.componentInstance.countries.set([{ id: 'c-eg', code: 'EG', nameEn: 'Egypt', nameAr: 'مصر', mobileLength: 10 }]);
      await tick();
      const country = host.querySelector<HTMLSelectElement>('.mobile select')!;
      country.value = 'EG';
      country.dispatchEvent(new Event('change'));

      const typed: TypedIdentity[] = [];
      fixture.componentInstance.form().typed.subscribe((identity) => typed.push(identity));
      type(host, 'fullName', 'Mona');
      type(host, 'ageYears', '30');
      type(host, 'secondPhone', '1 011 222 333');
      await new Promise((resolve) => setTimeout(resolve, 400));
      expect(typed.at(-1)).toEqual({ name: 'Mona', mobile: '', secondPhone: '01011222333' });

      expect(fixture.componentInstance.form().submit()).toBe(true);
      expect(fixture.componentInstance.saved[0]).toEqual(expect.objectContaining({ mobile: null, secondPhone: '01011222333' }));
    });

    it('refuses a second phone that is not a number and marks the field (PAT-017 from the server too)', async () => {
      const { fixture, host } = await render();
      type(host, 'fullName', 'Mona');
      type(host, 'ageYears', '30');
      type(host, 'secondPhone', 'abc');
      await tick();
      expect(fixture.componentInstance.form().submit()).toBe(false);
      expect(host.querySelector('[formcontrolname="secondPhone"].ng-invalid')).not.toBeNull();

      type(host, 'secondPhone', '');
      await tick();
      expect(fixture.componentInstance.form().submit()).toBe(true);
    });
  });
});
