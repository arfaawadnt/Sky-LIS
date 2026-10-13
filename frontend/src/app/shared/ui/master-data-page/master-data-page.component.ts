import { ChangeDetectionStrategy, Component, ElementRef, Injector, computed, contentChildren, effect, inject, input, linkedSignal, resource, signal, untracked } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, type ValidationErrors, type ValidatorFn, Validators } from '@angular/forms';

import { DEFAULT_PAGE_SIZE, type PageRequest, firstPage } from '../../../core/api/paging';
import { type ProblemDetails, ProblemError, isProblem, ProblemCodes } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { type LocalizedTextGroup, localizedFromDto, localizedTextGroup, localizedToDto } from '../../forms/localized-text';
import { markAllTouched } from '../../forms/mark-all-touched';
import { problemMessage } from '../../forms/problem-text';
import { SERVER_ERROR_KEY, clearLinkedServerErrors, problemToFormErrors } from '../../forms/problem-to-form-errors';
import type { ServerSort } from '../../forms/paging';
import { SaveShortcutDirective } from '../../forms/save-shortcut.directive';
import { UppercaseDirective } from '../../forms/uppercase.directive';
import { codeValidator, requiredText } from '../../forms/validators';
import { BadgeComponent } from '../badge/badge.component';
import { CanDirective } from '../can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../data-table/cell-template.directive';
import { DataTableComponent } from '../data-table/data-table.component';
import { type TableSort, cellValue, defaultRowId, serverSortKey } from '../data-table/data-table.types';
import { ConfirmService } from '../dialog/confirm.service';
import { DrawerComponent } from '../drawer/drawer.component';
import { FormFieldComponent } from '../form-field/form-field.component';
import { IconComponent } from '../icon/icon.component';
import { LocalizedTextInputComponent } from '../localized-text-input/localized-text-input.component';
import { PageHeaderComponent } from '../page-header/page-header.component';
import { PaginatorComponent } from '../paginator/paginator.component';
import { SearchBoxComponent } from '../search-box/search-box.component';
import { SectionCardComponent } from '../section-card/section-card.component';
import { ToastService } from '../toast/toast.service';
import { ToolbarComponent } from '../toolbar/toolbar.component';
import type { FieldSchema, FormValue, MasterDataConfig, MasterDataFilter, RowAction, SelectOption } from './master-data.types';

type DynamicForm = FormGroup<Record<string, AbstractControl>>;

let nextPageId = 0;

/**
 * `<app-master-data-page [config]="config" />`: the list-plus-drawer CRUD page of catalogue and setup screens
 * (SRS "Grids & editors"). Driven by one `MasterDataConfig`: i18n prefix, columns, field schema, data adapter.
 *
 * Behaviour: search box (server or client side), active/inactive filter, paged table, "Add" opens an empty drawer,
 * a row click opens it filled, Ctrl+S saves, Esc closes, `VALIDATION-400` errors land on their fields, deactivate /
 * reactivate ask for confirmation, every success shows a toast and reloads the list. Module-specific cell
 * templates (`<ng-template appCell="…" let-row>`) and extra header actions (`[pageActions]`) can be projected.
 */
@Component({
  selector: 'app-master-data-page',
  imports: [
    ReactiveFormsModule,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    RowActionsTemplateDirective,
    PaginatorComponent,
    DrawerComponent,
    FormFieldComponent,
    LocalizedTextInputComponent,
    BadgeComponent,
    IconComponent,
    CanDirective,
    SaveShortcutDirective,
    UppercaseDirective,
  ],
  templateUrl: './master-data-page.component.html',
  styleUrl: './master-data-page.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MasterDataPageComponent<TItem, TDto> {
  protected readonly i18n = inject(I18nService);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  readonly config = input.required<MasterDataConfig<TItem, TDto>>();

  /** Cell templates projected by the module page, forwarded to the table. */
  protected readonly cellTemplates = contentChildren(CellTemplateDirective);

  protected readonly formId = `app-master-data-${++nextPageId}-form`;

  protected readonly filter = signal<MasterDataFilter>({ search: '', active: true });
  protected readonly page = linkedSignal<PageRequest>(() => firstPage(this.config().pageSize ?? DEFAULT_PAGE_SIZE));
  private readonly reloadTick = signal(0);

  /** The column order the user chose in the table header (`null` = the list's own order). */
  protected readonly sort = signal<TableSort | null>(null);

  protected readonly serverSorted = computed(() => this.config().sortMode === 'server');

  /** B-03: the server key and direction of the chosen column, per language (a bilingual name sorts by the shown language). */
  private readonly serverSort = computed<ServerSort | null>(() => {
    const sort = this.sort();
    if (sort === null || !this.serverSorted()) {
      return null;
    }

    const column = this.config().columns.find((candidate) => candidate.key === sort.key);
    return column ? { key: serverSortKey(column, this.i18n.lang()), direction: sort.direction } : null;
  });

  private readonly listFilter = computed<MasterDataFilter>(() => {
    const sort = this.serverSort();
    return sort === null ? this.filter() : { ...this.filter(), sort };
  });

  protected readonly list = resource({
    params: () => ({ filter: this.listFilter(), page: this.page(), tick: this.reloadTick() }),
    loader: ({ params, abortSignal }) => untracked(() => this.config().adapter).list(params.filter, params.page, abortSignal),
  });

  protected readonly rows = computed<readonly TItem[]>(() => {
    const items = this.list.hasValue() ? this.list.value().items : [];
    const search = this.filter().search.trim().toLowerCase();
    if (this.config().searchMode !== 'client' || search === '') {
      return items;
    }

    const columns = this.config().columns;
    return items.filter((item) => columns.some((column) => String(cellValue(column, item) ?? '').toLowerCase().includes(search)));
  });

  protected readonly totalCount = computed(() => (this.list.hasValue() ? this.list.value().totalCount : 0));

  protected readonly activation = computed(() => this.config().activation ?? this.config().adapter.setActive !== undefined);

  protected readonly rowId = computed(() => this.config().rowId ?? defaultRowId<TItem>);

  protected readonly isActive = computed(() => this.config().isActive ?? ((item: TItem) => (item as unknown as { isActive?: boolean }).isActive !== false));

  protected readonly rowClass = computed(() => {
    const active = this.isActive();
    return (item: TItem): string | null => (active(item) ? null : 'inactive');
  });

  protected readonly permissions = computed(() => this.config().permissions ?? {});

  protected readonly visibleRowActions = computed<readonly RowAction<TItem>[]>(() => this.config().rowActions ?? []);

  // drawer state
  protected readonly drawerOpen = signal(false);
  protected readonly editing = signal<TItem | null>(null);
  protected readonly form = signal<DynamicForm | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveProblem = signal<ProblemDetails | null>(null);
  /** B-01: a save was refused by the form itself; the drawer says so (the fields are marked and the first one focused). */
  protected readonly blocked = signal(false);
  /** The form value when the server errors arrived: a linked field that differs from it clears them (B-01). */
  private serverErrorSnapshot: Readonly<Record<string, unknown>> | null = null;
  protected readonly options = signal<Readonly<Record<string, readonly SelectOption[]>>>({});

  /** The user may change an existing record: the config's `update` permission, or no gate at all (the server decides regardless). */
  protected readonly canEdit = computed(() => {
    const permission = this.permissions().update;
    // resolved only for a gated config, like `*appCan` (an ungated page needs no session)
    return permission === undefined || this.injector.get(PermissionsService).can(permission);
  });

  /**
   * R-07: a row opened without the edit permission is a read-only view: every field disabled, no Save (nor F10), the
   * title says "View". The list stays clickable so a reader can see the whole record.
   */
  protected readonly readOnly = computed(() => this.editing() !== null && !this.canEdit());

  protected readonly drawerTitle = computed(() => this.text(this.editing() === null ? 'add' : this.readOnly() ? 'view' : 'edit'));

  protected readonly subtitle = computed(() => {
    const key = `${this.config().i18nPrefix}.subtitle`;
    return this.i18n.has(key) ? this.i18n.t(key) : null;
  });

  constructor() {
    // async select options are resolved once per configuration
    effect(() => {
      const fields = this.config().fields;
      untracked(() => void this.loadOptions(fields));
    });
  }

  /** `text('add')` → `<prefix>.add`, else `shared.masterData.add`. */
  protected text(key: string): string {
    const own = `${this.config().i18nPrefix}.${key}`;
    return this.i18n.has(own) ? this.i18n.t(own) : this.i18n.t(`shared.masterData.${key}`);
  }

  protected fieldLabel(field: FieldSchema): string {
    return this.i18n.t(field.label ?? `${this.config().i18nPrefix}.field.${field.name}`);
  }

  protected fieldHint(field: FieldSchema): string | null {
    return field.hint ? this.i18n.t(field.hint) : null;
  }

  protected isFull(field: FieldSchema): boolean {
    return field.full ?? (field.kind === 'textarea' || field.kind === 'localizedText');
  }

  protected optionsFor(field: FieldSchema): readonly SelectOption[] {
    return this.options()[field.name] ?? (Array.isArray(field.options) ? field.options : []);
  }

  protected optionLabel(option: SelectOption): string {
    return this.i18n.lang() === 'ar' && option.labelAr ? option.labelAr : option.label;
  }

  protected localizedGroup(form: DynamicForm, field: FieldSchema): LocalizedTextGroup {
    return form.controls[field.name] as LocalizedTextGroup;
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, this.config().i18nPrefix, problem);
  }

  protected loadErrorText(): string {
    return this.text('loadError');
  }

  protected setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search }));
    this.page.update((current) => ({ ...current, page: 1 }));
  }

  protected onActiveChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.filter.update((current) => ({ ...current, active: value === 'all' ? null : value === 'active' }));
    this.page.update((current) => ({ ...current, page: 1 }));
  }

  protected onPageChange(page: PageRequest): void {
    this.page.set(page);
  }

  /** A header click: a server-sorted list starts again at page 1 of the new order (B-03). */
  protected onSortChange(sort: TableSort | null): void {
    this.sort.set(sort);
    if (this.serverSorted()) {
      this.page.update((current) => ({ ...current, page: 1 }));
    }
  }

  /** Any edit in the drawer (bubbling `input` / `change`): stale linked server errors go, the notice follows the form. */
  protected onFormEdited(): void {
    const form = this.form();
    if (form === null) {
      return;
    }

    this.markPatternFieldsAsTyped(form);
    const groups = this.config().linkedFields ?? [];
    if (this.serverErrorSnapshot !== null && groups.length > 0 && clearLinkedServerErrors(form, groups, this.serverErrorSnapshot)) {
      this.serverErrorSnapshot = null;
    }

    if (this.blocked() && form.valid) {
      this.blocked.set(false);
    }
  }

  /** C-09 live check: a field with a `pattern` shows its verdict from the first keystroke, not only after it loses focus or on Save. */
  private markPatternFieldsAsTyped(form: DynamicForm): void {
    for (const field of this.config().fields) {
      const control = form.controls[field.name];
      if (field.pattern !== undefined && control?.dirty && !control.touched) {
        control.markAsTouched();
      }
    }
  }

  reload(): void {
    this.reloadTick.update((tick) => tick + 1);
  }

  openCreate(): void {
    this.editing.set(null);
    this.form.set(this.buildForm(null));
    this.resetDrawerState();
    this.drawerOpen.set(true);
  }

  openEdit(item: TItem): void {
    this.editing.set(item);
    const form = this.buildForm(item);
    if (this.readOnly()) {
      form.disable();
    }

    this.form.set(form);
    this.resetDrawerState();
    this.drawerOpen.set(true);
  }

  closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  protected onDrawerClosed(): void {
    this.form.set(null);
    this.editing.set(null);
    this.resetDrawerState();
  }

  private resetDrawerState(): void {
    this.saveProblem.set(null);
    this.blocked.set(false);
    this.serverErrorSnapshot = null;
  }

  async save(): Promise<void> {
    const form = this.form();
    if (form === null || this.saving() || this.readOnly()) {
      return;
    }

    // B-01: a refused save is never silent: every field shows its line, the first wrong one takes the focus and the
    // drawer says the record was not saved.
    if (!markAllTouched(form)) {
      this.blocked.set(true);
      this.focusFirstInvalid();
      return;
    }

    this.blocked.set(false);
    const item = this.editing();
    const value = this.readValue(form);
    const dto = (this.config().toDto ?? this.defaultToDto)(value, item);
    this.saving.set(true);
    this.saveProblem.set(null);
    try {
      if (item === null) {
        await this.config().adapter.create(dto);
      } else {
        await this.config().adapter.update(this.rowId()(item), dto);
      }

      this.toast.success(this.text('saved'));
      this.drawerOpen.set(false);
      this.reload();
    } catch (error: unknown) {
      this.applySaveError(error, form);
    } finally {
      this.saving.set(false);
    }
  }

  async toggleActive(item: TItem): Promise<void> {
    const adapter = this.config().adapter;
    if (!adapter.setActive) {
      return;
    }

    const active = this.isActive()(item);
    const key = active ? 'confirmDeactivate' : 'confirmActivate';
    const confirmed = await this.confirm.confirm({ title: this.text(`${key}.title`), message: this.text(`${key}.message`), danger: active });
    if (!confirmed) {
      return;
    }

    try {
      await adapter.setActive(this.rowId()(item), !active);
      this.toast.success(this.text(active ? 'deactivated' : 'activated'));
      this.reload();
    } catch (error: unknown) {
      this.toast.error(error instanceof ProblemError ? this.problemText(error.problem) : this.i18n.t('shared.error.generic'));
    }
  }

  async runAction(action: RowAction<TItem>, item: TItem): Promise<void> {
    try {
      await action.run(item);
    } catch (error: unknown) {
      this.toast.error(error instanceof ProblemError ? this.problemText(error.problem) : this.i18n.t('shared.error.generic'));
    }
  }

  protected actionVisible(action: RowAction<TItem>, item: TItem): boolean {
    return action.visible ? action.visible(item) : true;
  }

  private applySaveError(error: unknown, form: DynamicForm): void {
    if (!(error instanceof ProblemError)) {
      this.saveProblem.set({ type: 'about:blank', title: '', status: 0, code: ProblemCodes.unexpected, traceId: null, correlationId: null, errors: null });
      return;
    }

    if (isProblem(error, ProblemCodes.validationFailed)) {
      const unmatched = problemToFormErrors(error.problem, form);
      this.serverErrorSnapshot = form.getRawValue() as Readonly<Record<string, unknown>>;
      if (Object.keys(unmatched).length === 0 && error.problem.errors && Object.keys(error.problem.errors).length > 0) {
        return;
      }
    }

    this.saveProblem.set(error.problem);
  }

  /** Focuses the first invalid field of the drawer in document order. */
  private focusFirstInvalid(): void {
    const selector = ['input', 'select', 'textarea'].map((tag) => `#${this.formId} ${tag}.ng-invalid:not([disabled])`).join(', ');
    this.host.ownerDocument.querySelector<HTMLElement>(selector)?.focus();
  }

  private buildForm(item: TItem | null): DynamicForm {
    const initial: FormValue = item === null ? {} : (this.config().toForm ?? this.defaultToForm)(item);
    const controls: Record<string, AbstractControl> = {};
    for (const field of this.config().fields) {
      controls[field.name] = this.buildControl(field, initial[field.name], item !== null);
    }

    return new FormGroup(controls);
  }

  private buildControl(field: FieldSchema, initial: unknown, editing: boolean): AbstractControl {
    const disabled = editing && (field.immutable ?? field.kind === 'code');
    switch (field.kind) {
      case 'localizedText': {
        const value = initial && typeof initial === 'object' ? (initial as { en?: string; ar?: string }) : {};
        const group = localizedTextGroup({ en: value.en ?? '', ar: value.ar ?? '' }, { required: field.required ?? true, maxLength: field.maxLength ?? 200 });
        if (disabled) {
          group.disable();
        }

        return group;
      }
      case 'boolean':
        return new FormControl<boolean>({ value: typeof initial === 'boolean' ? initial : Boolean(field.defaultValue ?? false), disabled }, { nonNullable: true });
      case 'number': {
        const validators = [];
        if (field.required) {
          validators.push(Validators.required);
        }

        if (field.min !== undefined) {
          validators.push(Validators.min(field.min));
        }

        if (field.max !== undefined) {
          validators.push(Validators.max(field.max));
        }

        const value = typeof initial === 'number' ? initial : typeof field.defaultValue === 'number' ? field.defaultValue : null;
        return new FormControl<number | null>({ value, disabled }, { validators });
      }
      case 'multiSelect': {
        const value = Array.isArray(initial) ? initial.filter((entry): entry is string => typeof entry === 'string') : Array.isArray(field.defaultValue) ? field.defaultValue.filter((entry): entry is string => typeof entry === 'string') : [];
        return new FormControl<string[]>({ value, disabled }, { nonNullable: true, validators: field.required ? [Validators.required, Validators.minLength(1)] : [] });
      }
      case 'select':
      case 'date': {
        const value = typeof initial === 'string' ? initial : typeof field.defaultValue === 'string' ? field.defaultValue : null;
        return new FormControl<string | null>({ value, disabled }, { validators: field.required ? [Validators.required] : [] });
      }
      case 'code': {
        const validators = [codeValidator, Validators.maxLength(field.maxLength ?? 32), ...this.patternValidators(field)];
        if (field.required ?? true) {
          validators.unshift(requiredText);
        }

        // B-01: a new record starts at the field's default (a branch's currency starts at EGP), like every other kind
        const value = typeof initial === 'string' ? initial : typeof field.defaultValue === 'string' ? field.defaultValue : '';
        return new FormControl<string>({ value, disabled }, { nonNullable: true, validators });
      }
      default: {
        const validators = [Validators.maxLength(field.maxLength ?? (field.kind === 'textarea' ? 2000 : 200)), ...this.patternValidators(field)];
        if (field.required) {
          validators.unshift(requiredText);
        }

        const value = typeof initial === 'string' ? initial : typeof field.defaultValue === 'string' ? field.defaultValue : '';
        return new FormControl<string>({ value, disabled }, { nonNullable: true, validators });
      }
    }
  }

  /**
   * The `pattern` of a text or code field: `{ pattern }` plus the field's own message under the kit's ready-text key
   * (`SERVER_ERROR_KEY`), which `app-form-field` shows as it is. An empty value is left to `required`.
   */
  private patternValidators(field: FieldSchema): ValidatorFn[] {
    const pattern = field.pattern;
    if (pattern === undefined || (field.kind !== 'text' && field.kind !== 'code')) {
      return [];
    }

    const message = field.patternMessage ?? 'shared.validation.pattern';
    return [
      (control: AbstractControl): ValidationErrors | null => {
        const value: unknown = control.value;
        const text = typeof value === 'string' ? value.trim() : '';
        pattern.lastIndex = 0;   // a global or sticky expression keeps state between calls
        return text === '' || pattern.test(text) ? null : { pattern: { requiredPattern: String(pattern) }, [SERVER_ERROR_KEY]: this.i18n.t(message) };
      },
    ];
  }

  private readValue(form: DynamicForm): FormValue {
    const value: Record<string, unknown> = {};
    for (const field of this.config().fields) {
      const control = form.controls[field.name];
      if (!control) {
        continue;
      }

      const raw: unknown = control.getRawValue();
      value[field.name] = typeof raw === 'string' && field.kind !== 'localizedText' ? raw.trim() : raw;
    }

    return value;
  }

  private readonly defaultToForm = (item: TItem): FormValue => {
    const source = item as unknown as Readonly<Record<string, unknown>>;
    const value: Record<string, unknown> = {};
    for (const field of this.config().fields) {
      value[field.name] = field.kind === 'localizedText' ? localizedFromDto(source, field.name) : source[field.name];
    }

    return value;
  };

  private readonly defaultToDto = (value: FormValue): TDto => {
    const dto: Record<string, unknown> = {};
    for (const field of this.config().fields) {
      const raw = value[field.name];
      if (field.kind === 'localizedText') {
        Object.assign(dto, localizedToDto(field.name, raw as { en: string; ar: string }));
      } else {
        dto[field.name] = raw === '' && (field.kind === 'select' || field.kind === 'date') ? null : raw;
      }
    }

    return dto as TDto;
  };

  private async loadOptions(fields: readonly FieldSchema[]): Promise<void> {
    for (const field of fields) {
      if ((field.kind !== 'select' && field.kind !== 'multiSelect') || typeof field.options !== 'function') {
        continue;
      }

      try {
        const options = await field.options();
        this.options.update((current) => ({ ...current, [field.name]: options }));
      } catch {
        this.options.update((current) => ({ ...current, [field.name]: [] }));
      }
    }
  }
}
