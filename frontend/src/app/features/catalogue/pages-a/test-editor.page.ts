import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, Injector, afterNextRender, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { type AbstractControl, FormArray, FormControl, FormGroup, ReactiveFormsModule, type ValidationErrors, type ValidatorFn, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { map } from 'rxjs';

import {
  CATALOGUE_A_PERMISSIONS,
  FREQUENCY_ACTIONS,
  GENDER_RESTRICTIONS,
  LAB_TEST_TYPES,
  RANGE_AGE_UNITS,
  TEST_CONFIGURATION,
  type FrequencyCheckAction,
  type GenderRestriction,
  type LabTestDetail,
  type LabTestGeneral,
  type LabTestTiming,
  type LabTestType,
  type ListValueItem,
  type ParagraphItem,
  type RangeAgeUnit,
  type RangeBandItem,
  type SampleConditionView,
  type TestLookupItem,
} from '../../../core/api/catalogue-a.contracts';
import { type FieldRule, type ProblemDetails, ProblemCodes } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import type { Mutation } from '../../../core/state/mutation';
import { localizedPick, localizedTextGroup } from '../../../shared/forms/localized-text';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SERVER_ERROR_KEY, SERVER_RULES_KEY, problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { UppercaseDirective } from '../../../shared/forms/uppercase.directive';
import { codeValidator, requiredText } from '../../../shared/forms/validators';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { LocalizedTextInputComponent } from '../../../shared/ui/localized-text-input/localized-text-input.component';
import { type Breadcrumb, PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { type TabItem, TabsComponent } from '../../../shared/ui/tabs/tabs.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { daysToMask, maskToDays, numberOrNull, testGeneralOf, textOrNull } from '../data-a/catalogue-a.mapping';
import { CatalogueAOptions, type CatalogueOption, optionLabel } from '../state-a/catalogue-a-options';
import { TestEditorFacade } from '../state-a/test-editor.facade';

type Section = 'general' | 'ranges' | 'values' | 'formula' | 'paragraphs' | 'timing';

type RowKind = 'bands' | 'values' | 'paragraphs';

/** One write of a save: the section it belongs to and the call (true = stored). */
type Step = readonly [Section, () => Promise<boolean>];

type BandForm = FormGroup<{
  sex: FormControl<GenderRestriction>;
  ageFrom: FormControl<number | null>;
  ageTo: FormControl<number | null>;
  ageUnit: FormControl<RangeAgeUnit>;
  patientConditionId: FormControl<string | null>;
  low: FormControl<number | null>;
  high: FormControl<number | null>;
  panicLow: FormControl<number | null>;
  panicHigh: FormControl<number | null>;
  reportableLow: FormControl<number | null>;
  reportableHigh: FormControl<number | null>;
  textRange: FormControl<string>;
}>;

type ValueForm = FormGroup<{
  value: FormControl<string>;
  isNormal: FormControl<boolean>;
  isCritical: FormControl<boolean>;
  isDefault: FormControl<boolean>;
  isPlaceholder: FormControl<boolean>;
  colour: FormControl<string>;
}>;

type ParagraphForm = FormGroup<{ title: FormControl<string>; text: FormControl<string> }>;

const COLOUR = /^#[0-9A-Fa-f]{6}$/;

/**
 * The test editor (SRS CP-41): general data, then one tab per configuration the type allows (range bands, list
 * values, formula with "check formula", paragraphs) and the timing; one save (Ctrl+S) writes only what changed.
 * Clearing a configuration is saved before a type change, so changing a test's type is a single save (CAT-071).
 */
@Component({
  selector: 'app-test-editor-page',
  imports: [
    ReactiveFormsModule,
    PageHeaderComponent,
    SectionCardComponent,
    TabsComponent,
    FormFieldComponent,
    LocalizedTextInputComponent,
    SearchBoxComponent,
    BadgeComponent,
    IconComponent,
    CanDirective,
    SaveShortcutDirective,
    UppercaseDirective,
  ],
  templateUrl: './test-editor.page.html',
  styleUrl: './catalogue-editor.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TestEditorPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(TestEditorFacade);
  protected readonly options = inject(CatalogueAOptions);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly access = inject(PermissionsService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly injector = inject(Injector);

  protected readonly permissions = CATALOGUE_A_PERMISSIONS;
  protected readonly types = LAB_TEST_TYPES;
  protected readonly genders = GENDER_RESTRICTIONS;
  protected readonly ageUnits = RANGE_AGE_UNITS;
  protected readonly frequencyActions = FREQUENCY_ACTIONS;
  protected readonly weekdays = [0, 1, 2, 3, 4, 5, 6];

  /** The id in the address bar, followed reactively: the router reuses this page when only `:id` changes (UAT B-05). */
  private readonly routeId = toSignal(this.route.paramMap.pipe(map((params) => params.get('id'))), { initialValue: this.route.snapshot.paramMap.get('id') });

  /** A test created here whose configuration was partly refused: the page continues as its editor and keeps the input (UAT B-04). */
  private readonly createdId = signal<string | null>(null);

  protected readonly testId = computed(() => this.createdId() ?? this.routeId());
  protected readonly isNew = computed(() => this.testId() === null);

  /** Viewers (no create / edit right) see the test read-only (UAT B-12); the server decides regardless. */
  protected readonly canEdit = computed(() => this.access.can(this.isNew() ? CATALOGUE_A_PERMISSIONS.create : CATALOGUE_A_PERMISSIONS.edit));

  /** The record the forms were last filled from: a re-read of the same record keeps the sections still being edited (UAT B-04). */
  private filledId: string | null = null;

  protected readonly general = new FormGroup({
    code: new FormControl('', { nonNullable: true, validators: [requiredText, codeValidator, Validators.maxLength(16)] }),
    name: localizedTextGroup({}, { required: true, maxLength: 200 }),
    reportName: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
    loinc: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(20)] }),
    cpt: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(20)] }),
    clinicalGroupId: new FormControl<string | null>(null, { validators: [Validators.required] }),
    type: new FormControl<LabTestType>('Numeric', { nonNullable: true }),
    sampleTypeId: new FormControl<string | null>(null),
    sampleConditionId: new FormControl<string | null>(null),
    unitId: new FormControl<string | null>(null),
    gender: new FormControl<GenderRestriction>('Any', { nonNullable: true }),
    methodology: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(200)] }),
    decimals: new FormControl<number | null>(2, { validators: [Validators.required, Validators.min(0), Validators.max(6)] }),
    isHiddenHelper: new FormControl(false, { nonNullable: true }),
    printReference: new FormControl(true, { nonNullable: true }),
    labelCount: new FormControl<number | null>(1, { validators: [Validators.required, Validators.min(0), Validators.max(20)] }),
    prefix: new FormControl('', { nonNullable: true, validators: [Validators.maxLength(10)] }),
  });

  protected readonly timing = new FormGroup({
    durationHours: new FormControl<number | null>(24, { validators: [Validators.required, Validators.min(0), Validators.max(8760)] }),
    minDurationHours: new FormControl<number | null>(0, { validators: [Validators.required, Validators.min(0), Validators.max(8760)] }),
    frequencyCheckDays: new FormControl<number | null>(null, { validators: [Validators.min(1), Validators.max(3650)] }),
    frequencyAction: new FormControl<FrequencyCheckAction>('Alert', { nonNullable: true }),
    deltaPercent: new FormControl<number | null>(null, { validators: [Validators.min(0.001), Validators.max(1000)] }),
    deltaWindowDays: new FormControl<number | null>(null, { validators: [Validators.min(1), Validators.max(3650)] }),
    testDays: new FormArray(maskToDays(127).map((on) => new FormControl(on, { nonNullable: true }))),
  });

  protected readonly bands = new FormArray<BandForm>([]);
  protected readonly values = new FormArray<ValueForm>([]);
  protected readonly paragraphs = new FormArray<ParagraphForm>([]);
  protected readonly formula = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(1000)] });

  /** The forms as a new test starts: restored when the page goes back to "new" without being rebuilt (UAT R1-07). */
  private readonly blankGeneral = this.general.getRawValue();
  private readonly blankTiming = this.timing.getRawValue();

  protected readonly type = toSignal(this.general.controls.type.valueChanges, { initialValue: this.general.controls.type.value });
  private readonly sampleTypeId = toSignal(this.general.controls.sampleTypeId.valueChanges, { initialValue: this.general.controls.sampleTypeId.value });

  protected readonly allows = computed(() => ({
    ranges: TEST_CONFIGURATION.rangeBands(this.type()),
    values: TEST_CONFIGURATION.listValues(this.type()),
    formula: TEST_CONFIGURATION.formula(this.type()),
    paragraphs: TEST_CONFIGURATION.paragraphs(this.type()),
  }));

  protected readonly textOnlyBands = computed(() => this.type() === 'NonNumeric');

  protected readonly activeTab = signal<string | null>('general');
  protected readonly saving = signal(false);
  /** A save the forms refused (validation v0.5.0 round 4): the page says so, like the drawer editors, until the forms are valid. */
  protected readonly blocked = signal(false);
  protected readonly problems = signal<Partial<Record<Section, ProblemDetails>>>({});
  protected readonly conditions = signal<readonly SampleConditionView[]>([]);
  private readonly rowsVersion = signal(0);

  protected readonly tabs = computed<readonly TabItem[]>(() => {
    this.rowsVersion();
    const allows = this.allows();
    const problems = this.problems();
    // a refused section is marked "!" on its tab (UAT B-04)
    const badge = (section: Section, count: number | null): string | number | null => (problems[section] ? '!' : count);
    const tabs: TabItem[] = [{ id: 'general', label: this.i18n.t('catalogue-a.tests.tab.general'), badge: badge('general', null) }];
    if (allows.ranges) {
      tabs.push({ id: 'ranges', label: this.i18n.t('catalogue-a.tests.tab.ranges'), badge: badge('ranges', this.bands.length) });
    }

    if (allows.values) {
      tabs.push({ id: 'values', label: this.i18n.t('catalogue-a.tests.tab.values'), badge: badge('values', this.values.length) });
    }

    if (allows.formula) {
      tabs.push({ id: 'formula', label: this.i18n.t('catalogue-a.tests.tab.formula'), badge: badge('formula', null) });
    }

    if (allows.paragraphs) {
      tabs.push({ id: 'paragraphs', label: this.i18n.t('catalogue-a.tests.tab.paragraphs'), badge: badge('paragraphs', this.paragraphs.length) });
    }

    tabs.push({ id: 'timing', label: this.i18n.t('catalogue-a.tests.tab.timing'), badge: badge('timing', null) });
    return tabs;
  });

  /** The loaded test, only while it is the one this page edits: a previous record is never shown or acted on (UAT B-05). */
  protected readonly detail = computed<LabTestDetail | null>(() => {
    const detail = this.facade.detail.hasValue() ? this.facade.detail.value() : null;
    return detail !== null && detail.id === this.testId() ? detail : null;
  });

  protected readonly title = computed(() => {
    const detail = this.detail();
    return detail === null ? this.i18n.t('catalogue-a.tests.new') : `${detail.code} · ${localizedPick({ en: detail.nameEn, ar: detail.nameAr }, this.i18n.lang())}`;
  });

  protected readonly crumbs = computed<readonly Breadcrumb[]>(() => [
    { label: this.i18n.t('catalogue-a.tests.title'), path: this.router.createUrlTree(['..'], { relativeTo: this.route }).toString() },
    { label: this.detail()?.code ?? this.i18n.t('catalogue-a.tests.new'), path: null },
  ]);

  protected readonly groupOptions = computed(() => selectable(this.options.groups.hasValue() ? this.options.groups.value() : [], this.detail()?.clinicalGroupId ?? null));
  protected readonly sampleTypeOptions = computed(() => selectable(this.options.sampleTypes.hasValue() ? this.options.sampleTypes.value() : [], this.detail()?.sampleTypeId ?? null));
  protected readonly unitOptions = computed(() => selectable(this.options.units.hasValue() ? this.options.units.value() : [], this.detail()?.unitId ?? null));
  protected readonly conditionOptions = computed(() => {
    const used = new Set((this.detail()?.rangeBands ?? []).map((band) => band.patientConditionId));
    return (this.options.patientConditions.hasValue() ? this.options.patientConditions.value() : []).filter((option) => option.isActive || used.has(option.value));
  });

  /** The active conditions of the chosen sample type, plus the one the test already holds (BR-24, UAT B-08). */
  protected readonly conditionChoices = computed(() => {
    const current = this.detail()?.sampleConditionId ?? null;
    return this.conditions().filter((condition) => condition.isActive || condition.id === current);
  });

  protected readonly verdict = signal<{ readonly valid: boolean; readonly text: string } | null>(null);
  protected readonly referenceResults = computed<readonly TestLookupItem[]>(() => (this.facade.references.hasValue() ? this.facade.references.value() : []));

  constructor() {
    void this.i18n.load('catalogue-a');
    this.options.refresh();   // the options outlive the page: re-read on open (UAT D-14)

    // every id in the address bar opens its own record, also when the router reuses the page (UAT B-05)
    effect(() => {
      const id = this.routeId();
      untracked(() => this.open(id));
    });

    // UAT R1-07: after a partly refused create the address shows the created test (replaceState) while the router still
    // holds the "new" route, so a history step back to /tests/new is not a navigation for the router. The page itself
    // follows the address: "new" starts empty again.
    const stopFollowing = this.location.onUrlChange((url) => {
      if (this.createdId() !== null && isNewTestUrl(url)) {
        this.startNew();
      }
    });
    inject(DestroyRef).onDestroy(stopFollowing);

    // read-only without the right to save (UAT B-12)
    effect(() => {
      this.canEdit();
      untracked(() => this.applyAccess());
    });

    effect(() => {
      const detail = this.detail();
      if (detail !== null) {
        untracked(() => this.fill(detail));
      }
    });

    effect(() => {
      const sampleTypeId = this.sampleTypeId();
      untracked(() => void this.loadConditions(sampleTypeId));
    });

    // a tab the new type no longer has falls back to the general data
    effect(() => {
      const ids = this.tabs().map((tab) => tab.id);
      untracked(() => {
        if (!ids.includes(this.activeTab() ?? 'general')) {
          this.activeTab.set('general');
        }
      });
    });
  }

  // ---- labels

  protected optionText(option: CatalogueOption): string {
    return optionLabel(option, this.i18n.lang()) + (option.isActive ? '' : ` (${this.i18n.t('catalogue-a.inactive')})`);
  }

  protected conditionText(condition: SampleConditionView): string {
    return `${condition.code} · ${localizedPick({ en: condition.nameEn, ar: condition.nameAr }, this.i18n.lang())}`;
  }

  protected typeLabel(type: string): string {
    return this.i18n.t(`catalogue-a.testType.${type}`);
  }

  protected name(item: { nameEn: string; nameAr: string }): string {
    return localizedPick({ en: item.nameEn, ar: item.nameAr }, this.i18n.lang());
  }

  protected problemText(problem: ProblemDetails | null | undefined): string {
    return problem ? problemMessage(this.i18n, 'catalogue-a', problem) : this.i18n.t('shared.error.generic');
  }

  protected problemOf(section: Section): ProblemDetails | null {
    return this.problems()[section] ?? null;
  }

  // ---- rows

  protected addBand(): void {
    const last = this.bands.at(this.bands.length - 1) as BandForm | undefined;
    this.bands.push(bandForm(null, last?.getRawValue().ageTo ?? 0, last?.getRawValue().ageUnit ?? 'Years'));
    this.touchRows(this.bands);
  }

  protected addValue(): void {
    this.values.push(valueForm(null));
    this.touchRows(this.values);
  }

  protected addParagraph(): void {
    this.paragraphs.push(paragraphForm(null));
    this.touchRows(this.paragraphs);
  }

  protected removeAt(kind: RowKind, index: number): void {
    if (kind === 'bands') {
      this.bands.removeAt(index);
      this.touchRows(this.bands);
    } else if (kind === 'values') {
      this.values.removeAt(index);
      this.touchRows(this.values);
    } else {
      this.paragraphs.removeAt(index);
      this.touchRows(this.paragraphs);
    }
  }

  protected move(kind: RowKind, index: number, offset: -1 | 1): void {
    const target = index + offset;
    if (kind === 'bands') {
      const row = this.bands.at(index);
      if (target >= 0 && target < this.bands.length) {
        this.bands.removeAt(index);
        this.bands.insert(target, row);
        this.touchRows(this.bands);
      }
    } else if (kind === 'values') {
      const row = this.values.at(index);
      if (target >= 0 && target < this.values.length) {
        this.values.removeAt(index);
        this.values.insert(target, row);
        this.touchRows(this.values);
      }
    } else {
      const row = this.paragraphs.at(index);
      if (target >= 0 && target < this.paragraphs.length) {
        this.paragraphs.removeAt(index);
        this.paragraphs.insert(target, row);
        this.touchRows(this.paragraphs);
      }
    }
  }

  /** BR-19 affordance: ticking "default" also ticks "placeholder" and unticks every other default. */
  protected onDefault(index: number): void {
    const row = this.values.at(index);
    if (row.controls.isDefault.value) {
      row.controls.isPlaceholder.setValue(true);
      row.controls.isCritical.setValue(false);
      this.values.controls.forEach((other, position) => {
        if (position !== index && other.controls.isDefault.value) {
          other.controls.isDefault.setValue(false);
        }
      });
    }
  }

  // ---- formula

  protected async checkFormula(): Promise<void> {
    const expression = this.formula.value.trim();
    if (expression === '') {
      this.verdict.set(null);
      return;
    }

    const result = await this.facade.check.execute({ expression, testId: this.testId() });
    if (result === null) {
      this.verdict.set({ valid: false, text: this.problemText(this.facade.check.problem()) });
      return;
    }

    if (result.valid) {
      this.verdict.set({ valid: true, text: this.i18n.tf('catalogue-a.tests.formula.valid', { normalized: result.normalized ?? '', references: result.references.join(', ') || '—' }) });
      if (result.normalized !== null && result.normalized !== expression) {
        this.formula.setValue(result.normalized);
        this.formula.markAsDirty();
      }
    } else {
      const position = result.position === null ? null : this.i18n.tf('catalogue-a.tests.formula.position', { position: result.position });
      this.verdict.set({ valid: false, text: formulaVerdict(this.problemText({ code: result.code ?? 'CAT-063' } as ProblemDetails), position, result.message) });
    }
  }

  protected searchReferences(search: string): void {
    this.facade.searchReferences(search);
  }

  protected insertReference(item: TestLookupItem): void {
    if (!this.canEdit()) {
      return;
    }

    const current = this.formula.value.trimEnd();
    this.formula.setValue(current === '' ? `[${item.code}]` : `${current} [${item.code}]`);
    this.formula.markAsDirty();
    this.verdict.set(null);
  }

  // ---- save

  async save(): Promise<void> {
    const id = this.testId();
    if (this.saving() || !this.canEdit() || (id !== null && this.detail() === null)) {
      return;   // nothing to save for a viewer, nor before the record in the address bar is loaded
    }

    if (!markAllTouched(this.general)) {
      this.block('general');
      return;
    }

    const allows = this.allows();
    const invalid = (
      [
        ['timing', this.timing],
        ['ranges', allows.ranges ? this.bands : null],
        ['values', allows.values ? this.values : null],
        ['paragraphs', allows.paragraphs ? this.paragraphs : null],
      ] as const
    ).find(([, control]) => control !== null && !markAllTouched(control));
    if (invalid) {
      this.block(invalid[0]);
      return;
    }

    this.blocked.set(false);
    this.saving.set(true);
    this.problems.set({});
    try {
      if (id === null) {
        await this.createAll();
      } else {
        await this.updateAll(id);
      }
    } finally {
      this.saving.set(false);
    }
  }

  /** True while a section the save checks is still invalid: the blocked-save notice follows the forms. */
  protected stillInvalid(): boolean {
    const allows = this.allows();
    return this.general.invalid || this.timing.invalid || (allows.ranges && this.bands.invalid) || (allows.values && this.values.invalid) || (allows.paragraphs && this.paragraphs.invalid);
  }

  /** The text under a list value's flags: CAT-061 from the form itself, or the server's field error on them. */
  protected flagsError(row: ValueForm): string | null {
    if (row.hasError('normalAndCritical')) {
      return this.i18n.t('catalogue-a.error.CAT-061');
    }

    const server: unknown = row.controls.isCritical.errors?.[SERVER_ERROR_KEY] ?? row.controls.isNormal.errors?.[SERVER_ERROR_KEY];
    if (typeof server !== 'string') {
      return null;
    }

    const rules = (row.controls.isCritical.errors?.[SERVER_RULES_KEY] ?? []) as readonly FieldRule[];
    return rules.some((rule) => rule.rule === 'CAT-061') ? this.i18n.t('catalogue-a.error.CAT-061') : server;
  }

  /** A save the forms refused: the section opens, the notice shows and the first marked field takes the focus once rendered. */
  private block(section: Section): void {
    this.activeTab.set(section);
    this.blocked.set(true);
    afterNextRender({ read: () => this.focusFirstInvalid() }, { injector: this.injector });
  }

  private focusFirstInvalid(): void {
    const panel = '.editor [role="tabpanel"]:not([hidden])';
    const selector = ['input', 'select', 'textarea'].flatMap((tag) => [`${panel} ${tag}.ng-invalid:not([disabled])`, `${panel} ${tag}[aria-invalid="true"]:not([disabled])`]).join(', ');
    this.host.querySelector<HTMLElement>(selector)?.focus();
  }

  protected async toggleActive(): Promise<void> {
    const detail = this.detail();
    if (detail === null) {
      return;
    }

    const key = detail.isActive ? 'confirmDeactivate' : 'confirmActivate';
    const confirmed = await this.confirm.confirm({ title: this.i18n.t(`catalogue-a.${key}.title`), message: this.i18n.tf(`catalogue-a.${key}.message`, { code: detail.code }), danger: detail.isActive });
    if (!confirmed) {
      return;
    }

    if ((await this.facade.setActive.execute({ id: detail.id, active: !detail.isActive })) !== null) {
      this.toast.success(this.i18n.t(detail.isActive ? 'catalogue-a.deactivated' : 'catalogue-a.activated'));
    } else {
      this.toast.error(this.problemText(this.facade.setActive.problem()));
    }
  }

  protected back(): void {
    void this.router.navigate(['..'], { relativeTo: this.route });
  }

  private async createAll(): Promise<void> {
    const created = await this.facade.create.execute({ code: this.general.getRawValue().code.trim(), ...this.generalValue() });
    if (created === null) {
      this.fail('general', this.facade.create.problem(), this.general);
      return;
    }

    this.general.markAsPristine();
    const allows = this.allows();
    const steps: Step[] = [];
    if (this.timing.dirty) {
      steps.push(['timing', () => this.run('timing', this.facade.timing, { id: created.id, timing: this.timingValue() }, this.timing)]);
    }

    if (allows.ranges && this.bands.length > 0) {
      steps.push(['ranges', () => this.run('ranges', this.facade.bands, { id: created.id, bands: this.bandValues() }, this.bands)]);
    }

    if (allows.values && this.values.length > 0) {
      steps.push(['values', () => this.run('values', this.facade.values, { id: created.id, values: this.valueItems() }, this.values)]);
    }

    if (allows.formula && this.formula.value.trim() !== '') {
      steps.push(['formula', () => this.run('formula', this.facade.formula, { id: created.id, expression: this.formula.value.trim() }, this.formula)]);
    }

    if (allows.paragraphs && this.paragraphs.length > 0) {
      steps.push(['paragraphs', () => this.run('paragraphs', this.facade.paragraphs, { id: created.id, paragraphs: this.paragraphItems() }, this.paragraphs)]);
    }

    // the sections are independent: each one is tried, so every refusal shows on its own tab
    const refused = await this.runEach(steps);
    if (refused.length === 0) {
      this.toast.success(this.i18n.t('catalogue-a.tests.created'));
      await this.router.navigate(['..', created.id], { relativeTo: this.route, replaceUrl: true });
      return;
    }

    // The head exists: the page continues as the editor of the created test, on its address, keeping everything the
    // user typed; the refused sections stay dirty and are sent again by the next save (UAT B-04 / C-05).
    this.filledId = created.id;
    this.createdId.set(created.id);
    this.location.replaceState(this.router.serializeUrl(this.router.createUrlTree(['..', created.id], { relativeTo: this.route })));
    this.facade.load(created.id);
    this.applyAccess();
    this.activeTab.set(refused[0] ?? 'general');
    this.toast.error(this.i18n.t('catalogue-a.tests.createdPartly'));
  }

  private async updateAll(id: string): Promise<void> {
    const allows = this.allows();
    const detail = this.detail();

    // 1. clearing a configuration first, so a type change that needs it passes (CAT-071); 2. the general data.
    // These run in order and stop at the first refusal: the type change depends on them.
    const ordered: Step[] = [];
    if (this.bands.dirty && this.bands.length === 0 && (detail?.rangeBands.length ?? 0) > 0) {
      ordered.push(['ranges', () => this.run('ranges', this.facade.bands, { id, bands: [] }, this.bands)]);
    }

    if (this.values.dirty && this.values.length === 0 && (detail?.listValues.length ?? 0) > 0) {
      ordered.push(['values', () => this.run('values', this.facade.values, { id, values: [] }, this.values)]);
    }

    if (this.formula.dirty && this.formula.value.trim() === '' && detail?.formulaExpression) {
      ordered.push(['formula', () => this.run('formula', this.facade.formula, { id, expression: null }, this.formula)]);
    }

    if (this.paragraphs.dirty && this.paragraphs.length === 0 && (detail?.paragraphs.length ?? 0) > 0) {
      ordered.push(['paragraphs', () => this.run('paragraphs', this.facade.paragraphs, { id, paragraphs: [] }, this.paragraphs)]);
    }

    if (this.general.dirty) {
      ordered.push(['general', () => this.run('general', this.facade.update, { id, request: this.generalValue() }, this.general)]);
    }

    // 3. the configurations the (new) type allows: independent, each one is tried
    const independent: Step[] = [];
    if (this.timing.dirty) {
      independent.push(['timing', () => this.run('timing', this.facade.timing, { id, timing: this.timingValue() }, this.timing)]);
    }

    if (allows.ranges && this.bands.dirty && this.bands.length > 0) {
      independent.push(['ranges', () => this.run('ranges', this.facade.bands, { id, bands: this.bandValues() }, this.bands)]);
    }

    if (allows.values && this.values.dirty && this.values.length > 0) {
      independent.push(['values', () => this.run('values', this.facade.values, { id, values: this.valueItems() }, this.values)]);
    }

    if (allows.formula && this.formula.dirty && this.formula.value.trim() !== '') {
      independent.push(['formula', () => this.run('formula', this.facade.formula, { id, expression: this.formula.value.trim() }, this.formula)]);
    }

    if (allows.paragraphs && this.paragraphs.dirty && this.paragraphs.length > 0) {
      independent.push(['paragraphs', () => this.run('paragraphs', this.facade.paragraphs, { id, paragraphs: this.paragraphItems() }, this.paragraphs)]);
    }

    let saved = 0;
    const refused: Section[] = [];
    for (const [section, step] of ordered) {
      if (!(await step())) {
        refused.push(section);
        break;
      }

      saved++;
    }

    if (refused.length === 0) {
      const failed = await this.runEach(independent);
      refused.push(...failed);
      saved += independent.length - failed.length;
    }

    // the re-read refreshes the saved sections; the refused ones are still dirty and keep the user's input (fill)
    if (saved > 0) {
      this.facade.detail.reload();
    }

    if (refused.length > 0) {
      this.activeTab.set(refused[0] ?? 'general');
      this.toast.error(this.i18n.t(saved > 0 ? 'catalogue-a.tests.savedPartly' : 'catalogue-a.tests.saveRefused'));
      return;
    }

    this.toast.success(this.i18n.t(saved > 0 ? 'catalogue-a.saved' : 'catalogue-a.nothingToSave'));
  }

  /** Runs every step, refused or not; answers the refused sections in order. */
  private async runEach(steps: readonly Step[]): Promise<Section[]> {
    const refused: Section[] = [];
    for (const [section, step] of steps) {
      if (!(await step())) {
        refused.push(section);
      }
    }

    return refused;
  }

  /** Runs one write; on success the section is pristine, on failure its problem shows on its tab and its input stays. */
  private async run<TIn>(section: Section, write: Mutation<TIn, unknown>, input: TIn, control: AbstractControl): Promise<boolean> {
    const done = await write.execute(input);
    if (done === null) {
      this.fail(section, write.problem(), control);
      return false;
    }

    control.markAsPristine();
    return true;
  }

  private fail(section: Section, problem: ProblemDetails | null, control: AbstractControl): void {
    if (problem === null) {
      return;
    }

    if (problem.code === ProblemCodes.validationFailed && control instanceof FormGroup) {
      problemToFormErrors(problem, control);
    }

    if (problem.code === ProblemCodes.validationFailed && control === this.values) {
      this.valueRowErrors(problem);
    }

    this.problems.update((current) => ({ ...current, [section]: problem }));
    this.activeTab.set(section);
  }

  /** `values[1].isCritical` → the server error on that row's control, shown under the row like the form's own errors (O-2). */
  private valueRowErrors(problem: ProblemDetails): void {
    for (const [key, messages] of Object.entries(problem.errors ?? {})) {
      const match = /^values\[(\d+)\]\.(\w+)$/i.exec(key);
      const row = match ? (this.values.at(Number(match[1])) as ValueForm | undefined) : undefined;
      const name = match?.[2] ?? '';
      const target = row?.get(name.charAt(0).toLowerCase() + name.slice(1));
      if (!target || messages.length === 0) {
        continue;
      }

      const rules = problem.rules?.[key];
      target.setErrors({ ...(target.errors ?? {}), [SERVER_ERROR_KEY]: messages.join(' '), ...(rules && rules.length > 0 ? { [SERVER_RULES_KEY]: rules } : {}) });
      target.markAsTouched();
    }
  }

  /** Back to an empty "new test": every section as a fresh page shows it. */
  private startNew(): void {
    this.general.reset(this.blankGeneral);
    this.timing.reset(this.blankTiming);
    this.bands.clear();
    this.values.clear();
    this.paragraphs.clear();
    this.formula.reset('');
    this.open(null);
  }

  /** Opens the record of the address bar: a new id resets the page and refills every section from the server. */
  private open(id: string | null): void {
    this.createdId.set(null);
    this.filledId = null;
    this.problems.set({});
    this.verdict.set(null);
    this.activeTab.set('general');
    this.facade.load(id);
    this.applyAccess();
  }

  /** Every input follows the right to save; the code never changes once created. */
  private applyAccess(): void {
    const sections: AbstractControl[] = [this.general, this.timing, this.bands, this.values, this.paragraphs, this.formula];
    if (!this.canEdit()) {
      sections.forEach((control) => control.disable({ emitEvent: false }));
      return;
    }

    sections.forEach((control) => control.enable({ emitEvent: false }));
    if (!this.isNew()) {
      this.general.controls.code.disable({ emitEvent: false });
    }
  }

  private generalValue(): LabTestGeneral {
    const raw = this.general.getRawValue();
    return {
      nameEn: raw.name.en.trim(),
      nameAr: raw.name.ar.trim(),
      reportName: textOrNull(raw.reportName),
      loinc: textOrNull(raw.loinc),
      cpt: textOrNull(raw.cpt),
      clinicalGroupId: raw.clinicalGroupId ?? '',
      type: raw.type,
      sampleTypeId: raw.sampleTypeId,
      sampleConditionId: raw.sampleTypeId === null ? null : raw.sampleConditionId,
      unitId: raw.unitId,
      gender: raw.gender,
      methodology: textOrNull(raw.methodology),
      decimals: numberOrNull(raw.decimals) ?? 0,
      isHiddenHelper: raw.isHiddenHelper,
      printReference: raw.printReference,
      labelCount: numberOrNull(raw.labelCount) ?? 0,
      prefix: textOrNull(raw.prefix),
    };
  }

  private timingValue(): LabTestTiming {
    const raw = this.timing.getRawValue();
    return {
      durationHours: numberOrNull(raw.durationHours) ?? 0,
      minDurationHours: numberOrNull(raw.minDurationHours) ?? 0,
      frequencyCheckDays: numberOrNull(raw.frequencyCheckDays),
      frequencyAction: raw.frequencyAction,
      deltaPercent: numberOrNull(raw.deltaPercent),
      deltaWindowDays: numberOrNull(raw.deltaWindowDays),
      testDays: daysToMask(raw.testDays),
    };
  }

  private bandValues(): readonly RangeBandItem[] {
    const textOnly = this.textOnlyBands();
    return this.bands.getRawValue().map((band) => ({
      sex: band.sex,
      ageFrom: numberOrNull(band.ageFrom) ?? 0,
      ageTo: numberOrNull(band.ageTo) ?? 0,
      ageUnit: band.ageUnit,
      patientConditionId: band.patientConditionId,
      low: textOnly ? null : numberOrNull(band.low),
      high: textOnly ? null : numberOrNull(band.high),
      panicLow: textOnly ? null : numberOrNull(band.panicLow),
      panicHigh: textOnly ? null : numberOrNull(band.panicHigh),
      reportableLow: textOnly ? null : numberOrNull(band.reportableLow),
      reportableHigh: textOnly ? null : numberOrNull(band.reportableHigh),
      textRange: textOrNull(band.textRange),
    }));
  }

  private valueItems(): readonly ListValueItem[] {
    return this.values.getRawValue().map((value) => ({
      value: value.value.trim(),
      isNormal: value.isNormal,
      isCritical: value.isCritical,
      isDefault: value.isDefault,
      isPlaceholder: value.isPlaceholder,
      colour: textOrNull(value.colour),
    }));
  }

  private paragraphItems(): readonly ParagraphItem[] {
    return this.paragraphs.getRawValue().map((paragraph) => ({ title: paragraph.title.trim(), text: textOrNull(paragraph.text) }));
  }

  /**
   * Fills the forms from the server. A re-read of the record already on screen (after a save) refreshes only the
   * sections the user is not editing: a refused section is still dirty and keeps its input (UAT B-04).
   */
  private fill(detail: LabTestDetail): void {
    const keep = this.filledId === detail.id;
    this.filledId = detail.id;
    const refill = (control: AbstractControl): boolean => !keep || !control.dirty;
    const general = testGeneralOf(detail);
    if (refill(this.general)) {
      this.fillGeneral(detail, general);
    }

    if (refill(this.timing)) {
      this.timing.reset({
        durationHours: detail.timing.durationHours,
        minDurationHours: detail.timing.minDurationHours,
        frequencyCheckDays: detail.timing.frequencyCheckDays,
        frequencyAction: detail.timing.frequencyAction,
        deltaPercent: detail.timing.deltaPercent,
        deltaWindowDays: detail.timing.deltaWindowDays,
        testDays: maskToDays(detail.timing.testDays),
      });
    }

    if (refill(this.bands)) {
      this.bands.clear();
      detail.rangeBands.forEach((band) => this.bands.push(bandForm(band, 0, 'Years')));
      this.bands.markAsPristine();
    }

    if (refill(this.values)) {
      this.values.clear();
      detail.listValues.forEach((value) => this.values.push(valueForm(value)));
      this.values.markAsPristine();
    }

    if (refill(this.paragraphs)) {
      this.paragraphs.clear();
      detail.paragraphs.forEach((paragraph) => this.paragraphs.push(paragraphForm(paragraph)));
      this.paragraphs.markAsPristine();
    }

    if (refill(this.formula)) {
      this.formula.reset(detail.formulaExpression ?? '');
      this.verdict.set(null);
    }

    this.applyAccess();
    this.rowsVersion.update((value) => value + 1);
  }

  private fillGeneral(detail: LabTestDetail, general: LabTestGeneral): void {
    this.general.reset({
      code: detail.code,
      name: { en: general.nameEn, ar: general.nameAr },
      reportName: general.reportName ?? '',
      loinc: general.loinc ?? '',
      cpt: general.cpt ?? '',
      clinicalGroupId: general.clinicalGroupId,
      type: general.type,
      sampleTypeId: general.sampleTypeId,
      sampleConditionId: general.sampleConditionId,
      unitId: general.unitId,
      gender: general.gender,
      methodology: general.methodology ?? '',
      decimals: general.decimals,
      isHiddenHelper: general.isHiddenHelper,
      printReference: general.printReference,
      labelCount: general.labelCount,
      prefix: general.prefix ?? '',
    });
  }

  private touchRows(array: AbstractControl): void {
    array.markAsDirty();
    this.rowsVersion.update((value) => value + 1);
  }

  private async loadConditions(sampleTypeId: string | null): Promise<void> {
    const conditions = sampleTypeId === null ? [] : await this.options.conditionsOf(sampleTypeId);
    this.conditions.set(conditions);
    const current = this.general.controls.sampleConditionId.value;
    if (current !== null && !conditions.some((condition) => condition.id === current)) {
      this.general.controls.sampleConditionId.setValue(null);
    }
  }
}

/**
 * The verdict of a refused formula check, said once (UAT B-13): the translated code text, the position, then the
 * server's detail without the position it already carries ("… (position 12)").
 */
export function formulaVerdict(codeText: string, position: string | null, detail: string | null): string {
  const reason = detail?.replace(/\s*\(position \d+\)\s*$/i, '').trim() ?? '';
  return [codeText, position].filter((part): part is string => part !== null && part !== '').join(' ') + (reason === '' ? '' : ` — ${reason}`);
}

/** Active options plus the one currently referenced (it may have been deactivated since, BR-24). */
function selectable(options: readonly CatalogueOption[], current: string | null): readonly CatalogueOption[] {
  return options.filter((option) => option.isActive || option.value === current);
}

function bandForm(band: RangeBandItem | null, nextFrom: number, unit: RangeAgeUnit): BandForm {
  return new FormGroup({
    sex: new FormControl<GenderRestriction>(band?.sex ?? 'Any', { nonNullable: true }),
    ageFrom: new FormControl<number | null>(band?.ageFrom ?? nextFrom, { validators: [Validators.required, Validators.min(0)] }),
    ageTo: new FormControl<number | null>(band?.ageTo ?? null, { validators: [Validators.required, Validators.min(1)] }),
    ageUnit: new FormControl<RangeAgeUnit>(band?.ageUnit ?? unit, { nonNullable: true }),
    patientConditionId: new FormControl<string | null>(band?.patientConditionId ?? null),
    low: new FormControl<number | null>(band?.low ?? null),
    high: new FormControl<number | null>(band?.high ?? null),
    panicLow: new FormControl<number | null>(band?.panicLow ?? null),
    panicHigh: new FormControl<number | null>(band?.panicHigh ?? null),
    reportableLow: new FormControl<number | null>(band?.reportableLow ?? null),
    reportableHigh: new FormControl<number | null>(band?.reportableHigh ?? null),
    textRange: new FormControl(band?.textRange ?? '', { nonNullable: true, validators: [Validators.maxLength(200)] }),
  });
}

/** Validation v0.5.0 O-2 (CAT-061): a list value reads as Normal or as Critical, never both. */
export const normalOrCritical: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const flags = control.value as { isNormal?: boolean; isCritical?: boolean } | null;
  return flags?.isNormal === true && flags.isCritical === true ? { normalAndCritical: true } : null;
};

function valueForm(value: ListValueItem | null): ValueForm {
  return new FormGroup(
    {
      value: new FormControl(value?.value ?? '', { nonNullable: true, validators: [requiredText, Validators.maxLength(200)] }),
      isNormal: new FormControl(value?.isNormal ?? true, { nonNullable: true }),
      isCritical: new FormControl(value?.isCritical ?? false, { nonNullable: true }),
      isDefault: new FormControl(value?.isDefault ?? false, { nonNullable: true }),
      isPlaceholder: new FormControl(value?.isPlaceholder ?? false, { nonNullable: true }),
      colour: new FormControl(value?.colour ?? '', { nonNullable: true, validators: [Validators.pattern(COLOUR)] }),
    },
    { validators: [normalOrCritical] },
  );
}

function paragraphForm(paragraph: ParagraphItem | null): ParagraphForm {
  return new FormGroup({
    title: new FormControl(paragraph?.title ?? '', { nonNullable: true, validators: [requiredText, Validators.maxLength(200)] }),
    text: new FormControl(paragraph?.text ?? '', { nonNullable: true, validators: [Validators.maxLength(4000)] }),
  });
}

/** True for the address of the "new test" page (`…/tests/new`, any query or fragment). */
export function isNewTestUrl(url: string): boolean {
  return /\/tests\/new$/.test(url.split(/[?#]/)[0] ?? '');
}
