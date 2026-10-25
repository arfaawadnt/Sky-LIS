import { ChangeDetectionStrategy, Component, computed, effect, inject, input, resource, signal, untracked } from '@angular/core';
import { type FormControl, ReactiveFormsModule } from '@angular/forms';

import type { AreaOption, CityOption, DistrictOption } from '../../../core/api/patients.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PatientLookupApi } from '../../patient/patient-lookup.api';
import { FormFieldComponent } from '../../ui/form-field/form-field.component';

/** The three labels and the empty choice, already translated by the host (`<module>.….city` …). */
export interface LocalityLabels {
  readonly city: string;
  readonly district: string;
  readonly area: string;
  /** The text of the empty option ("— none —"). */
  readonly none: string;
  /** Shown under the city select (e.g. "Optional"). */
  readonly hint?: string | null;
}

/** A group's three controls: ids of the platform hierarchy, `''` = none. */
export interface LocalityControls {
  readonly cityId: FormControl<string>;
  readonly districtId: FormControl<string>;
  readonly areaId: FormControl<string>;
}

/** The ids of a locality form value as a request sends them: blank → null, a lower level never without its parent. */
export function localityRequest(value: { readonly cityId: string; readonly districtId: string; readonly areaId: string }): {
  cityId: string | null;
  districtId: string | null;
  areaId: string | null;
} {
  const cityId = value.cityId.trim() === '' ? null : value.cityId;
  const districtId = cityId === null || value.districtId.trim() === '' ? null : value.districtId;
  const areaId = districtId === null || value.areaId.trim() === '' ? null : value.areaId;
  return { cityId, districtId, areaId };
}

/**
 * `<app-locality-fields [governorateId]="…" [controls]="…" [labels]="…" />` (LIS-CR-008 C-04): the cascading City (markaz) →
 * District → Area (village) selects under a chosen governorate, read from the anonymous public lookups
 * (`/api/v1/public/cities|districts|areas`, through `PatientLookupApi`, cached per session) with one dedicated key signal per
 * level (the shared/patient pattern: a broad read would reload on every keystroke). Optional at every level: nothing shows
 * while the governorate lists no city. Changing the governorate (after the first value) clears the three controls; changing a
 * level clears the levels below. Laid out with `display: contents`, so the fields join the host's `.form-grid`. The server
 * checks the chain (GEO-020…022); the host maps those codes to the controls.
 */
@Component({
  selector: 'app-locality-fields',
  imports: [ReactiveFormsModule, FormFieldComponent],
  template: `
    @if (showCity()) {
      <app-form-field [label]="labels().city" [hint]="labels().hint ?? null">
        <select class="inp" [formControl]="controls().cityId" (change)="onCityChange()" data-level="city">
          <option value="">{{ labels().none }}</option>
          @for (city of cityOptions(); track city.id) {
            <option [value]="city.id">{{ name(city) }}</option>
          }
        </select>
      </app-form-field>
      @if (districtOptions().length > 0 || controls().districtId.value !== '') {
        <app-form-field [label]="labels().district">
          <select class="inp" [formControl]="controls().districtId" (change)="onDistrictChange()" data-level="district">
            <option value="">{{ labels().none }}</option>
            @for (district of districtOptions(); track district.id) {
              <option [value]="district.id">{{ name(district) }}</option>
            }
          </select>
        </app-form-field>
      }
      @if (areaOptions().length > 0 || controls().areaId.value !== '') {
        <app-form-field [label]="labels().area">
          <select class="inp" [formControl]="controls().areaId" data-level="area">
            <option value="">{{ labels().none }}</option>
            @for (area of areaOptions(); track area.id) {
              <option [value]="area.id">{{ name(area) }}</option>
            }
          </select>
        </app-form-field>
      }
    }
  `,
  host: { style: 'display: contents' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocalityFieldsComponent {
  private readonly i18n = inject(I18nService);
  private readonly lookup = inject(PatientLookupApi);

  /** The governorate whose cities are offered; null or `''` shows nothing. */
  readonly governorateId = input<string | null>(null);
  readonly controls = input.required<LocalityControls>();
  readonly labels = input.required<LocalityLabels>();

  protected readonly cityKey = signal('');
  protected readonly districtKey = signal('');
  private previousGovernorate: string | null | undefined = undefined;
  private previousControls: LocalityControls | null = null;

  private readonly governorateKey = computed(() => this.governorateId() || undefined);

  private readonly cities = resource({
    params: () => this.governorateKey(),
    loader: ({ params }) => this.lookup.cities(params),
  });

  private readonly districts = resource({
    params: () => this.cityKey() || undefined,
    loader: ({ params }) => this.lookup.districts(params),
  });

  private readonly areas = resource({
    params: () => this.districtKey() || undefined,
    loader: ({ params }) => this.lookup.areas(params),
  });

  protected readonly cityOptions = computed<readonly CityOption[]>(() => (this.cities.hasValue() ? this.cities.value() : []));
  protected readonly districtOptions = computed<readonly DistrictOption[]>(() => (this.districts.hasValue() ? this.districts.value() : []));
  protected readonly areaOptions = computed<readonly AreaOption[]>(() => (this.areas.hasValue() ? this.areas.value() : []));

  /** The governorate lists cities, or the record already holds one (a city deactivated since stays visible as chosen). */
  protected readonly showCity = computed(() => this.governorateKey() !== undefined && (this.cityOptions().length > 0 || this.cityKey() !== ''));

  constructor() {
    // the controls' current values seed the keys (an edit drawer opens on a stored locality)
    effect(() => {
      const controls = this.controls();
      untracked(() => {
        this.cityKey.set(controls.cityId.value);
        this.districtKey.set(controls.districtId.value);
      });
    });

    // a new governorate (after the first one seen for the same controls) forgets the locality; new controls (another record) start over
    effect(() => {
      const governorate = this.governorateId() ?? null;
      const controls = this.controls();
      untracked(() => {
        if (controls === this.previousControls && this.previousGovernorate !== undefined && this.previousGovernorate !== governorate) {
          controls.cityId.setValue('');
          controls.districtId.setValue('');
          controls.areaId.setValue('');
          this.cityKey.set('');
          this.districtKey.set('');
        }

        this.previousGovernorate = governorate;
        this.previousControls = controls;
      });
    });
  }

  protected name(option: { readonly nameEn: string; readonly nameAr: string }): string {
    return this.i18n.lang() === 'ar' ? option.nameAr : option.nameEn;
  }

  protected onCityChange(): void {
    const controls = this.controls();
    this.cityKey.set(controls.cityId.value);
    controls.districtId.setValue('');
    controls.areaId.setValue('');
    this.districtKey.set('');
  }

  protected onDistrictChange(): void {
    const controls = this.controls();
    this.districtKey.set(controls.districtId.value);
    controls.areaId.setValue('');
  }
}
