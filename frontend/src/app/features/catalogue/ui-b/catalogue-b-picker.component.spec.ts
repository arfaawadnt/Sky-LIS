import { ChangeDetectionStrategy, Component, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';

import type { CatalogueBOption } from '../data-b/catalogue-b-lookups';
import { CatalogueBPickerComponent } from './catalogue-b-picker.component';

const SERVICES: readonly CatalogueBOption[] = [
  { id: 's1', code: 'CBC', nameEn: 'Blood count', nameAr: 'صورة دم' },
  { id: 's2', code: 'GLU', nameEn: 'Glucose', nameAr: 'جلوكوز' },
  { id: 's3', code: 'TSH', nameEn: 'Thyroid', nameAr: 'الغدة الدرقية' },
];

@Component({
  imports: [ReactiveFormsModule, CatalogueBPickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<app-catalogue-b-picker [formControl]="control" [search]="search" [known]="known" label="Services" emptyText="All services" [ordered]="true" />`,
})
class HostComponent {
  readonly control = new FormControl<readonly string[]>(['s3'], { nonNullable: true });
  readonly known: readonly CatalogueBOption[] = [SERVICES[2] as CatalogueBOption];
  readonly terms: string[] = [];
  readonly search = (term: string): Promise<readonly CatalogueBOption[]> => {
    this.terms.push(term);
    return Promise.resolve(SERVICES.filter((service) => `${service.code} ${service.nameEn}`.toLowerCase().includes(term.toLowerCase())));
  };
}

const wait = (ms: number): Promise<void> => new Promise((resolve) => setTimeout(resolve, ms));

describe('CatalogueBPickerComponent (ui-b)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent], providers: [provideZonelessChangeDetection()] }).compileComponents();
  });

  async function render(): Promise<{ fixture: ReturnType<typeof TestBed.createComponent<HostComponent>>; host: HTMLElement; input: HTMLInputElement }> {
    const fixture = TestBed.createComponent(HostComponent);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const input = host.querySelector<HTMLInputElement>('input[role="combobox"]');
    if (!input) {
      throw new Error('combobox missing');
    }

    return { fixture, host, input };
  }

  it('shows the known selection as numbered chips with their labels', async () => {
    const { host } = await render();

    const chip = host.querySelector('.chip');
    expect(chip?.textContent).toContain('TSH');
    expect(chip?.textContent).toContain('Thyroid');
    expect(chip?.querySelector('.order')?.textContent?.trim()).toBe('1');
  });

  it('searches as the user types, adds the highlighted match with Enter and removes the last chip with Backspace', async () => {
    const { fixture, host, input } = await render();

    input.focus();
    input.value = 'glu';
    input.dispatchEvent(new Event('input'));
    await wait(300);
    await fixture.whenStable();
    expect(fixture.componentInstance.terms).toContain('glu');
    expect(host.querySelectorAll('[role="option"]').length).toBe(1);

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await fixture.whenStable();
    expect(fixture.componentInstance.control.value).toEqual(['s3', 's2']);
    expect(host.querySelectorAll('.chip').length).toBe(2);

    input.value = '';
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Backspace', bubbles: true }));
    await fixture.whenStable();
    expect(fixture.componentInstance.control.value).toEqual(['s3']);
  });

  it('closes its list on Escape without letting the key reach the drawer', async () => {
    const { fixture, host, input } = await render();
    let reachedDocument = false;
    const listener = (): void => {
      reachedDocument = true;
    };
    document.addEventListener('keydown', listener);

    input.focus();
    input.dispatchEvent(new Event('focus'));
    await wait(10);
    await fixture.whenStable();
    expect(host.querySelector('[role="listbox"]')).not.toBeNull();

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await fixture.whenStable();
    document.removeEventListener('keydown', listener);

    expect(host.querySelector('[role="listbox"]')).toBeNull();
    expect(reachedDocument).toBe(false);
  });

  it('shows the empty text and follows the disabled state of the control', async () => {
    const { fixture, host, input } = await render();

    fixture.componentInstance.control.setValue([]);
    fixture.componentInstance.control.disable();
    await fixture.whenStable();

    expect(host.querySelector('.empty')?.textContent?.trim()).toBe('All services');
    expect(input.disabled).toBe(true);
  });
});
