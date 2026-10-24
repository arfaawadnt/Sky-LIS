import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, forwardRef, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { type ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

import { I18nService } from '../../../core/i18n/i18n.service';
import { type MobileDialCountry, type MobileParts, cleanMobileText, dialDigits, mobileCountryKey, mobileValueOf, splitMobile } from './mobile-number';

type DialMode = 'fixed' | 'select' | 'free';

/**
 * `<app-form-field [label]="…"><app-mobile-input formControlName="mobile" [country]="…" /></app-form-field>` (change request
 * LIS-CR-007, reviewer point C-05): one bordered field in two parts — the dial code and the national number.
 *
 * - The dial code is read-only when `country` is given (the form's own country picker decides it), else a small select over
 *   `countries` (the public countries list; `defaultCountry` = ISO or dial code preselected, else the first), else free text
 *   starting with `+`.
 * - A trunk 0 typed in the national part is dropped from the value at once and from the field on blur; pasting `+20 100…`,
 *   `0020100…` or `0100…` is recognised (the dial code follows the pasted one when the list knows it).
 * - The value is the E.164 string (`+201004439855`) or `''`; text that is not a number is passed on as typed so the form's
 *   validator refuses it, and without any dial code the digits are passed on as typed. A value written by the form keeps its
 *   spelling until the user edits the field (no spontaneous `dirty`).
 * - The national input comes first in the DOM (the form field's label, `aria-invalid` and the first-invalid focus land on
 *   it); the dial code is drawn before it with `order`.
 * - The host element answers `value` and `disabled` like a native input, and an `input` event on it takes the `value`
 *   set before, so a script or a spec that addresses the field by its `formcontrolname` keeps working.
 */
@Component({
  selector: 'app-mobile-input',
  templateUrl: './mobile-input.component.html',
  styleUrl: './mobile-input.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => MobileInputComponent), multi: true }],
  host: { '(input)': 'onHostInput($event)' },
})
export class MobileInputComponent implements ControlValueAccessor {
  protected readonly i18n = inject(I18nService);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly numberInput = viewChild<ElementRef<HTMLInputElement>>('number');

  /** The country of the number, fixed by the form (read-only dial code). */
  readonly country = input<MobileDialCountry | null>(null);
  /** The choice of dial codes when `country` is not given. */
  readonly countries = input<readonly MobileDialCountry[]>([]);
  /** The option preselected in the select: an ISO code (`EG`) or a dial code (`+20`). */
  readonly defaultCountry = input<string | null>(null);
  readonly autocomplete = input('off');

  /** The country of the dial code shown (`null` in free text), for the page's hint or storage rule. */
  readonly countryChange = output<MobileDialCountry | null>();
  /** Every edit by the user (typing, pasting, another dial code), with the new value. */
  readonly edited = output<string>();

  protected readonly disabled = signal(false);
  protected readonly national = signal('');
  private readonly chosenKey = signal<string | null>(null);
  protected readonly freeDial = signal('');

  protected readonly mode = computed<DialMode>(() => (this.country() !== null ? 'fixed' : this.countries().length > 0 ? 'select' : 'free'));

  protected readonly selected = computed<MobileDialCountry | null>(() => {
    const fixed = this.country();
    if (fixed !== null) {
      return fixed;
    }

    const list = this.countries();
    const wanted = this.chosenKey() ?? this.defaultCountry();
    return list.find((country) => matches(country, wanted)) ?? list[0] ?? null;
  });

  protected readonly selectedKey = computed(() => {
    const selected = this.selected();
    return selected === null ? '' : mobileCountryKey(selected);
  });

  protected readonly dial = computed(() => (this.mode() === 'free' ? this.freeDial() : (this.selected()?.dialCode ?? '')));

  /** "10 digits, without the leading 0" when the country's mobile length is known. */
  protected readonly placeholder = computed(() => {
    const length = this.mode() === 'free' ? null : (this.selected()?.mobileLength ?? null);
    if (length === null) {
      return null;
    }

    const shown = new Intl.NumberFormat(this.i18n.lang() === 'ar' ? 'ar-EG' : 'en-US', { useGrouping: false }).format(length);
    return this.i18n.tc('shared.mobileInput.lengthHint', length, { count: shown });
  });

  private onChange: (value: string) => void = () => undefined;
  private onTouched: () => void = () => undefined;
  /** The value the form holds (written or last emitted). */
  private lastValue = '';
  /** True once the user edited the field since the form last wrote it. */
  private userEdited = false;

  constructor() {
    Object.defineProperties(this.element, {
      value: { configurable: true, get: () => this.lastValue, set: (value: unknown) => this.showText(typeof value === 'string' ? value : '') },
      disabled: { configurable: true, get: () => this.disabled() },
    });

    effect(() => {
      const country = this.mode() === 'free' ? null : this.selected();
      untracked(() => this.countryChange.emit(country));
    });

    // The dial code can change under a number already there: the form's country picked after the number, or the list
    // arriving after the form wrote an international value. A written value is re-read; an edited one is re-sent.
    effect(() => {
      this.dial();
      this.countries();
      this.country();
      untracked(() => {
        if (this.national().startsWith('+')) {
          this.apply(splitMobile(this.national(), this.countries(), this.country(), this.dial()));
        }

        if (this.userEdited) {
          this.emit();
        }
      });
    });
  }

  writeValue(value: unknown): void {
    const text = typeof value === 'string' ? value : '';
    this.userEdited = false;
    this.lastValue = text;
    if (cleanMobileText(text) === '') {
      this.chosenKey.set(null);
      this.national.set('');
      this.syncInput();
      return;
    }

    this.apply(splitMobile(text, this.countries(), this.country(), this.dial()));
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  protected key(country: MobileDialCountry): string {
    return mobileCountryKey(country);
  }

  protected optionLabel(country: MobileDialCountry): string {
    const iso = country.isoCode?.trim();
    return iso ? `${iso} ${country.dialCode}` : country.dialCode;
  }

  protected optionTitle(country: MobileDialCountry): string | null {
    return (this.i18n.lang() === 'ar' ? country.nameAr : country.nameEn) ?? country.nameEn ?? null;
  }

  protected onInput(event: Event): void {
    this.userEdited = true;
    this.national.set((event.target as HTMLInputElement).value);
    this.emit();
    this.edited.emit(this.lastValue);
  }

  /** An `input` event dispatched on the host itself (not one bubbling from the parts): the text set through `value` was typed. */
  protected onHostInput(event: Event): void {
    if (event.target !== this.element) {
      return;
    }

    this.userEdited = true;
    this.emit();
    this.edited.emit(this.lastValue);
  }

  protected onPaste(event: ClipboardEvent): void {
    const pasted = event.clipboardData?.getData('text') ?? '';
    if (pasted === '') {
      return;
    }

    const element = event.target as HTMLInputElement;
    const start = element.selectionStart ?? element.value.length;
    const end = element.selectionEnd ?? start;
    event.preventDefault();
    this.userEdited = true;
    this.apply(splitMobile(element.value.slice(0, start) + pasted + element.value.slice(end), this.countries(), this.country(), this.dial()));
    this.emit();
    this.edited.emit(this.lastValue);
  }

  protected onBlur(): void {
    if (this.national() !== '') {
      this.apply(splitMobile(this.national(), this.countries(), this.country(), this.dial()));
    }

    if (this.userEdited) {
      this.emit();
    }

    this.onTouched();
  }

  protected onDialChange(event: Event): void {
    this.userEdited = true;
    this.chosenKey.set((event.target as HTMLSelectElement).value);
    this.emit();
    this.edited.emit(this.lastValue);
  }

  protected onFreeDial(event: Event): void {
    const digits = dialDigits((event.target as HTMLInputElement).value).slice(0, 4);
    this.userEdited = true;
    this.freeDial.set(digits === '' ? '' : `+${digits}`);
    this.emit();
    this.edited.emit(this.lastValue);
  }

  protected onDialBlur(): void {
    this.onTouched();
  }

  /** Shows the parts: the dial code follows a recognised one (select mode), the national part what is left. */
  private apply(parts: MobileParts): void {
    if (this.mode() === 'select' && parts.dial !== this.dial()) {
      const match = this.countries().find((country) => country.dialCode === parts.dial);
      if (match) {
        this.chosenKey.set(mobileCountryKey(match));
      }
    }

    this.national.set(parts.national);
    this.syncInput();
  }

  private emit(): void {
    const value = mobileValueOf(this.national(), this.countries(), this.country(), this.dial());
    if (value !== this.lastValue) {
      this.lastValue = value;
      this.onChange(value);
    }
  }

  private showText(text: string): void {
    this.national.set(text);
    this.syncInput();
  }

  /** The bound `[value]` follows on the next render; the element is updated at once so a paste or a blur shows its result. */
  private syncInput(): void {
    const element = this.numberInput()?.nativeElement;
    if (element && element.value !== this.national()) {
      element.value = this.national();
    }
  }
}

function matches(country: MobileDialCountry, wanted: string | null): boolean {
  if (wanted === null || wanted.trim() === '') {
    return false;
  }

  const key = wanted.trim();
  return mobileCountryKey(country) === key || country.isoCode === key || country.dialCode === key;
}
