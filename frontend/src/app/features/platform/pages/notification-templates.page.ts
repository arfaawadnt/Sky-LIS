import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { map, of, startWith, switchMap } from 'rxjs';

import type { PageRequest } from '../../../core/api/paging';
import { type NotificationTemplateListItem, PLATFORM_PERMISSIONS, TEMPLATE_CHANNELS, type TemplateChannel } from '../../../core/api/platform.contracts';
import type { ProblemDetails } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { requiredText } from '../../../shared/forms/validators';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective, RowActionsTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { DrawerComponent } from '../../../shared/ui/drawer/drawer.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { PaginatorComponent } from '../../../shared/ui/paginator/paginator.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { CsvExportService } from '../data/csv-export';
import { TemplatesFacade } from '../state/reference.facades';
import { fileStamp, pick } from '../ui/format';
import { statusTone } from '../ui/status';

/** Sample values of the preview, per placeholder (canonical names and SRS aliases). */
const SAMPLES: Readonly<Record<string, string>> = {
  code: '482913',
  minutes: '5',
  url: 'https://al-noor.skylis.app',
  username: 'noor.admin',
  labName: 'Al Noor Lab',
  lab: 'Al Noor Lab',
  patient: 'Ahmed Ali',
  accession: 'ACC-2026-00042',
  acc: 'ACC-2026-00042',
  test: 'Potassium',
  value: '7.1 mmol/L',
  number: 'TRQ-2026-00007',
  requester: 'Mona Hassan',
  treasury: 'MAIN',
};

const CODE_PATTERN = /^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$/;

type TemplateForm = ReturnType<NotificationTemplatesPage['buildForm']>;
type TextControl = 'textEn' | 'textAr';

/**
 * SA-20 Notification templates: SMS and e-mail texts in English and Arabic with `{placeholder}` tokens. Clicking a
 * placeholder inserts it at the cursor of the last text field; the preview fills sample values. An inactive
 * template is not used (the built-in text of the code is sent instead).
 */
@Component({
  selector: 'app-platform-notification-templates-page',
  imports: [
    ReactiveFormsModule,
    CanDirective,
    SaveShortcutDirective,
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    RowActionsTemplateDirective,
    PaginatorComponent,
    BadgeComponent,
    DrawerComponent,
    FormFieldComponent,
    IconComponent,
  ],
  templateUrl: './notification-templates.page.html',
  styleUrl: './platform-pages.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationTemplatesPage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(TemplatesFacade);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly csv = inject(CsvExportService);

  protected readonly permissions = PLATFORM_PERMISSIONS;
  protected readonly channels = TEMPLATE_CHANNELS;

  protected readonly columns: readonly TableColumn<NotificationTemplateListItem>[] = [
    { key: 'code', header: 'platform.templates.column.code', mono: true, sortable: true, width: '190px' },
    { key: 'channel', header: 'platform.templates.column.channel', width: '100px' },
    { key: 'text', header: 'platform.templates.column.text', value: (row) => pick(row.textEn, row.textAr, this.i18n.lang()) },
    { key: 'status', header: 'platform.templates.column.status', width: '110px' },
  ];

  /** Canonical placeholders plus the SRS aliases, from the server. */
  protected readonly tokens = computed<readonly string[]>(() => {
    const help = this.facade.placeholders.value();
    return help ? [...help.placeholders, ...Object.keys(help.aliases)] : [];
  });

  protected readonly drawerOpen = signal(false);
  protected readonly editing = signal<NotificationTemplateListItem | null>(null);
  protected readonly form = signal<TemplateForm | null>(null);
  protected readonly saveProblem = signal<ProblemDetails | null>(null);
  private lastFocused: { control: TextControl; element: HTMLTextAreaElement } | null = null;

  /** The live values of the open form, for the preview and the subject fields. */
  private readonly formValue = toSignal(
    toObservable(this.form).pipe(switchMap((form) => (form === null ? of(null) : form.valueChanges.pipe(startWith(null), map(() => form.getRawValue()))))),
    { initialValue: null },
  );

  protected readonly isEmail = computed(() => this.formValue()?.channel === 'Email');
  protected readonly previewEn = computed(() => render(this.formValue()?.textEn ?? ''));
  protected readonly previewAr = computed(() => render(this.formValue()?.textAr ?? ''));

  constructor() {
    void this.i18n.load('platform');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
  }

  protected tokenText(token: string): string {
    return `{${token}}`;
  }

  protected tone(value: string): ReturnType<typeof statusTone> {
    return statusTone(value);
  }

  protected onActive(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setActiveFilter(value === 'all' ? null : value === 'active');
  }

  protected onPage(page: PageRequest): void {
    this.facade.setPage(page);
  }

  protected onFocus(control: TextControl, event: FocusEvent): void {
    this.lastFocused = { control, element: event.target as HTMLTextAreaElement };
  }

  /** Inserts `{token}` at the cursor of the last focused text (English by default). */
  protected insert(form: TemplateForm, token: string): void {
    const target = this.lastFocused;
    const name: TextControl = target?.control ?? 'textEn';
    const control = form.controls[name];
    const text = control.value;
    const start = target?.element.selectionStart ?? text.length;
    const end = target?.element.selectionEnd ?? text.length;
    const insertion = `{${token}}`;
    control.setValue(text.slice(0, start) + insertion + text.slice(end));
    control.markAsDirty();
    if (target) {
      const caret = start + insertion.length;
      queueMicrotask(() => {
        target.element.focus();
        target.element.setSelectionRange(caret, caret);
      });
    }
  }

  protected openEdit(template: NotificationTemplateListItem | null): void {
    this.editing.set(template);
    this.saveProblem.set(null);
    this.lastFocused = null;
    this.form.set(this.buildForm(template));
    this.drawerOpen.set(true);
  }

  protected async save(): Promise<void> {
    const form = this.form();
    if (form === null || this.facade.save.pending() || !markAllTouched(form)) {
      return;
    }

    const value = form.getRawValue();
    const email = value.channel === 'Email';
    this.saveProblem.set(null);
    const done = await this.facade.save.execute({
      id: this.editing()?.id ?? null,
      request: {
        code: value.code.trim().toLowerCase(),
        channel: value.channel,
        textEn: value.textEn.trim(),
        textAr: value.textAr.trim(),
        subjectEn: email ? value.subjectEn.trim() || null : null,
        subjectAr: email ? value.subjectAr.trim() || null : null,
      },
    });
    if (done !== null) {
      this.drawerOpen.set(false);
      this.toast.success(this.i18n.t('platform.templates.saved'));
      return;
    }

    const problem = this.facade.save.problem();
    if (problem !== null) {
      problemToFormErrors(problem, form);
      this.saveProblem.set(problem);
    }
  }

  protected async toggle(template: NotificationTemplateListItem): Promise<void> {
    const key = template.isActive ? 'deactivate' : 'activate';
    const confirmed = await this.confirm.confirm({ title: this.i18n.t(`platform.${key}`), message: this.i18n.tf(`platform.templates.confirm.${key}`, { code: template.code }), danger: template.isActive });
    if (confirmed && (await this.facade.setActive.execute({ id: template.id, active: !template.isActive })) !== null) {
      this.toast.success(this.i18n.t(template.isActive ? 'platform.deactivated' : 'platform.activated'));
    }
  }

  protected problemText(problem: ProblemDetails): string {
    return problemMessage(this.i18n, 'platform.templates', problem);
  }

  protected channelLabel(channel: TemplateChannel): string {
    return this.i18n.t(`platform.templates.channel.${channel}`);
  }

  protected exportCsv(): void {
    this.csv.download(`notification-templates-${fileStamp()}`, [
      { header: this.i18n.t('platform.templates.column.code'), value: (row: NotificationTemplateListItem) => row.code },
      { header: this.i18n.t('platform.templates.column.channel'), value: (row) => row.channel },
      { header: this.i18n.t('platform.templates.field.subjectEn'), value: (row) => row.subjectEn },
      { header: this.i18n.t('platform.templates.field.subjectAr'), value: (row) => row.subjectAr },
      { header: this.i18n.t('platform.templates.field.textEn'), value: (row) => row.textEn },
      { header: this.i18n.t('platform.templates.field.textAr'), value: (row) => row.textAr },
      { header: this.i18n.t('platform.active'), value: (row) => (row.isActive ? this.i18n.t('platform.yes') : this.i18n.t('platform.no')) },
    ], this.facade.rows());
  }

  private buildForm(template: NotificationTemplateListItem | null) {
    return new FormGroup({
      code: new FormControl({ value: template?.code ?? '', disabled: template !== null }, { nonNullable: true, validators: [requiredText, Validators.pattern(CODE_PATTERN), Validators.maxLength(40)] }),
      channel: new FormControl<TemplateChannel>(template?.channel ?? 'Sms', { nonNullable: true }),
      subjectEn: new FormControl(template?.subjectEn ?? '', { nonNullable: true, validators: [Validators.maxLength(200)] }),
      subjectAr: new FormControl(template?.subjectAr ?? '', { nonNullable: true, validators: [Validators.maxLength(200)] }),
      textEn: new FormControl(template?.textEn ?? '', { nonNullable: true, validators: [requiredText, Validators.maxLength(2000)] }),
      textAr: new FormControl(template?.textAr ?? '', { nonNullable: true, validators: [requiredText, Validators.maxLength(2000)] }),
    });
  }
}

/** Fills `{token}` with a sample value; unknown tokens stay visible, as the sender leaves them. */
function render(text: string): string {
  return text.replace(/\{([A-Za-z][A-Za-z0-9]*)\}/g, (match, name: string) => SAMPLES[name] ?? match);
}
