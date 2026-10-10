import { ChangeDetectionStrategy, Component, computed, contentChildren, effect, inject, input, linkedSignal, resource, signal, untracked } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import { DEFAULT_PAGE_SIZE, type PageRequest, firstPage } from '../../../core/api/paging';
import { type ProblemDetails, ProblemError, isProblem, ProblemCodes } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { type LocalizedTextGroup, localizedFromDto, localizedTextGroup, localizedToDto } from '../../forms/localized-text';
import { markAllTouched } from '../../forms/mark-all-touched';
import { problemMessage } from '../../forms/problem-text';
import { problemToFormErrors } from '../../forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../forms/save-shortcut.directive';
import { UppercaseDirective } from '../../forms/uppercase.directive';
import { codeValidator, requiredText } from '../../forms/validators';
import { BadgeComponent } from '../badge/badge.component';
import { CanDirective } from '../can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../data-table/cell-template.directive';
import { DataTableComponent } from '../data-table/data-table.component';
import { cellValue, defaultRowId } from '../data-table/data-table.types';
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

  readonly config = input.required<MasterDataConfig<TItem, TDto>>();

  /** Cell templates projected by the module page, forwarded to the table. */
  protected readonly cellTemplates = contentChildren(CellTemplateDirective);

  protected readonly formId = `app-master-data-${++nextPageId}-form`;

  protected readonly filter = signal<MasterDataFilter>({ search: '', active: true });
  protected readonly page = linkedSignal<PageRequest>(() => firstPage(this.config().pageSize ?? DEFAULT_PAGE_SIZE));
  private readonly reloadTick = signal(0);

  protected readonly list = resource({
    params: () => ({ filter: this.filter(), page: this.page(), tick: this.reloadTick() }),
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
  protected readonly options = signal<Readonly<Record<string, readonly SelectOption[]>>>({});

  protected readonly drawerTitle = computed(() => this.text(this.editing() === null ? 'add' : 'edit'));

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

  reload(): void {
    this.reloadTick.update((tick) => tick + 1);
  }

  openCreate(): void {
    this.editing.set(null);
    this.form.set(this.buildForm(null));
    this.saveProblem.set(null);
    this.drawerOpen.set(true);
  }

  openEdit(item: TItem): void {
    this.editing.set(item);
    this.form.set(this.buildForm(item));
    this.saveProblem.set(null);
    this.drawerOpen.set(true);
  }

  closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  protected onDrawerClosed(): void {
    this.form.set(null);
    this.editing.set(null);
    this.saveProblem.set(null);
  }

  async save(): Promise<void> {
    const form = this.form();
    if (form === null || this.saving()) {
      return;
    }

    if (!markAllTouched(form)) {
      return;
    }

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
      if (Object.keys(unmatched).length === 0 && error.problem.errors && Object.keys(error.problem.errors).length > 0) {
        return;
      }
    }

    this.saveProblem.set(error.problem);
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
      case 'select':
      case 'date': {
        const value = typeof initial === 'string' ? initial : typeof field.defaultValue === 'string' ? field.defaultValue : null;
        return new FormControl<string | null>({ value, disabled }, { validators: field.required ? [Validators.required] : [] });
      }
      case 'code': {
        const validators = [codeValidator, Validators.maxLength(field.maxLength ?? 32)];
        if (field.required ?? true) {
          validators.unshift(requiredText);
        }

        return new FormControl<string>({ value: typeof initial === 'string' ? initial : '', disabled }, { nonNullable: true, validators });
      }
      default: {
        const validators = [Validators.maxLength(field.maxLength ?? (field.kind === 'textarea' ? 2000 : 200))];
        if (field.required) {
          validators.unshift(requiredText);
        }

        const value = typeof initial === 'string' ? initial : typeof field.defaultValue === 'string' ? field.defaultValue : '';
        return new FormControl<string>({ value, disabled }, { nonNullable: true, validators });
      }
    }
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
      if (field.kind !== 'select' || typeof field.options !== 'function') {
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
