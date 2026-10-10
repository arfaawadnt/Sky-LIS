import { FormControl, FormGroup } from '@angular/forms';

import { textValidators } from './validators';

/** A bilingual name as the domain's `LocalizedText` (`name_en` / `name_ar`; DTOs carry `nameEn` / `nameAr`). */
export interface LocalizedText {
  readonly en: string;
  readonly ar: string;
}

export type LocalizedTextGroup = FormGroup<{ en: FormControl<string>; ar: FormControl<string> }>;

export interface LocalizedTextOptions {
  /** Both languages required (the default for names of master data). */
  readonly required?: boolean;
  readonly maxLength?: number;
}

/** The form group an `<app-localized-text-input>` binds to: `{ en, ar }`, non-nullable, with the usual validators. */
export function localizedTextGroup(initial: Partial<LocalizedText> = {}, options: LocalizedTextOptions = {}): LocalizedTextGroup {
  const validators = textValidators({ required: options.required ?? true, maxLength: options.maxLength ?? 200 });
  return new FormGroup({
    en: new FormControl(initial.en ?? '', { nonNullable: true, validators }),
    ar: new FormControl(initial.ar ?? '', { nonNullable: true, validators }),
  });
}

/** `{ nameEn, nameAr }` of a DTO → `{ en, ar }` for the form. */
export function localizedFromDto(dto: Readonly<Record<string, unknown>>, name: string): LocalizedText {
  const nested = dto[name];
  if (nested && typeof nested === 'object') {
    const value = nested as Partial<LocalizedText>;
    return { en: value.en ?? '', ar: value.ar ?? '' };
  }

  const en = dto[`${name}En`];
  const ar = dto[`${name}Ar`];
  return { en: typeof en === 'string' ? en : '', ar: typeof ar === 'string' ? ar : '' };
}

/** `{ en, ar }` of the form → `{ nameEn, nameAr }` for the DTO (trimmed). */
export function localizedToDto(name: string, value: LocalizedText): Record<string, string> {
  return { [`${name}En`]: value.en.trim(), [`${name}Ar`]: value.ar.trim() };
}

/** Picks the text of the current language, falling back to the other one when it is empty. */
export function localizedPick(value: Readonly<Partial<LocalizedText>>, lang: string): string {
  const preferred = lang === 'ar' ? value.ar : value.en;
  const other = lang === 'ar' ? value.en : value.ar;
  return preferred && preferred.length > 0 ? preferred : (other ?? '');
}
