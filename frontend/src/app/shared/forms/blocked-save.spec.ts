import { ChangeDetectionStrategy, Component, forwardRef, provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type ControlValueAccessor, FormControl, FormGroup, NG_VALUE_ACCESSOR, ReactiveFormsModule, Validators } from '@angular/forms';

import { blockedSave, focusFirstInvalidField } from './blocked-save';

/** A custom control like the Catalogue B picker: the host carries the control, a search box inside takes the focus. */
@Component({
  selector: 'app-fake-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<input class="picker-search" />',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => FakePickerComponent), multi: true }],
})
class FakePickerComponent implements ControlValueAccessor {
  writeValue(): void {
    // the test reads the form control only
  }

  registerOnChange(): void {
    // no user input in the test
  }

  registerOnTouched(): void {
    // no user input in the test
  }
}

@Component({
  selector: 'app-blocked-save-host',
  imports: [ReactiveFormsModule, FakePickerComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <input class="outside" [formControl]="outside" />
    <form id="the-form" [formGroup]="form">
      @if (blocked.blocked()) {
        <p class="problem" role="alert">blocked</p>
      }
      <input class="code" formControlName="code" />
      <div formGroupName="name">
        <input class="en" formControlName="en" />
        <input class="ar" formControlName="ar" />
      </div>
      <app-fake-picker formControlName="services" />
    </form>
  `,
})
class HostComponent {
  readonly outside = new FormControl('', { nonNullable: true, validators: [Validators.required] });
  readonly form = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    name: new FormGroup({ en: new FormControl('', { nonNullable: true }), ar: new FormControl('', { nonNullable: true, validators: [Validators.required] }) }),
    services: new FormControl<readonly string[]>([], { nonNullable: true, validators: [(control) => ((control.value as readonly string[]).length === 0 ? { required: true } : null)] }),
  });
  readonly blocked = blockedSave();
}

describe('blocked save (validation v0.5.0 F-03)', () => {
  async function render(): Promise<{ host: HTMLElement; component: HostComponent; fixture: ReturnType<typeof TestBed.createComponent<HostComponent>> }> {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    const fixture = TestBed.createComponent(HostComponent);
    const host = fixture.nativeElement as HTMLElement;
    document.body.appendChild(host);
    await fixture.whenStable();
    return { host, component: fixture.componentInstance, fixture };
  }

  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('a refused save shows the notice and focuses the first marked field of the scope, not one outside it', async () => {
    const { host, component, fixture } = await render();

    expect(component.blocked.check(component.form, '#the-form')).toBe(false);
    await fixture.whenStable();

    expect(host.querySelector('p.problem[role="alert"]')).not.toBeNull();
    expect(document.activeElement).toBe(host.querySelector('input.code'));
    expect(component.form.controls.code.touched).toBe(true);
  });

  it('skips a group to its marked half (the Arabic name) and hands a custom control\'s focus to its first focusable part', async () => {
    const { host, component, fixture } = await render();
    component.form.controls.code.setValue('GLU');
    await fixture.whenStable();

    component.blocked.check(component.form, '#the-form');
    expect(document.activeElement).toBe(host.querySelector('input.ar'));

    component.form.controls.name.controls.ar.setValue('جلوكوز');
    await fixture.whenStable();
    component.blocked.check(component.form, '#the-form');
    expect(document.activeElement).toBe(host.querySelector('input.picker-search'));
  });

  it('a valid save clears the notice, and reset() clears it when the form opens again', async () => {
    const { host, component, fixture } = await render();
    component.blocked.check(component.form, '#the-form');
    await fixture.whenStable();
    expect(component.blocked.blocked()).toBe(true);

    component.blocked.reset();
    await fixture.whenStable();
    expect(host.querySelector('p.problem')).toBeNull();

    component.form.setValue({ code: 'GLU', name: { en: 'Glucose', ar: 'جلوكوز' }, services: ['s1'] });
    expect(component.blocked.check(component.form, '#the-form')).toBe(true);
    expect(component.blocked.blocked()).toBe(false);
  });

  it('focusFirstInvalidField returns null when nothing is marked or there is no root', () => {
    const root = document.createElement('div');
    root.innerHTML = '<input class="ng-valid" /><input class="ng-invalid" disabled />';
    document.body.appendChild(root);

    expect(focusFirstInvalidField(root)).toBeNull();
    expect(focusFirstInvalidField(null)).toBeNull();
  });
});
