import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, computed, inject, linkedSignal, signal } from '@angular/core';

import { CATALOGUE_A_PERMISSIONS, SERVICE_TYPES, type ServiceSequenceChange, type ServiceSequenceRow, type ServiceType } from '../../../core/api/catalogue-a.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { localizedPick } from '../../../shared/forms/localized-text';
import { problemMessage } from '../../../shared/forms/problem-text';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { CanDirective } from '../../../shared/ui/can.directive';
import { CellTemplateDirective } from '../../../shared/ui/data-table/cell-template.directive';
import { DataTableComponent } from '../../../shared/ui/data-table/data-table.component';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';
import { PageHeaderComponent } from '../../../shared/ui/page-header/page-header.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { SectionCardComponent } from '../../../shared/ui/section-card/section-card.component';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import { ToolbarComponent } from '../../../shared/ui/toolbar/toolbar.component';
import { csvFileName, downloadCsv, toCsv, yesNo } from '../data-a/csv-export';
import { CatalogueAOptions, optionLabel } from '../state-a/catalogue-a-options';
import { ServiceSequenceFacade } from '../state-a/service-sequence.facade';
import { CatalogueExportButtonComponent } from '../ui-a/export-button.component';
import { type DropPlacement, applyOrder, assignmentsOf, dropIndex, moveItem, occupiedNumbers, parseTypedNumber, positionOfNumber } from '../ui-a/sequence-order';

/** The row under a dragged one and the side the drop lands on. */
interface DropTarget {
  readonly id: string;
  readonly placement: DropPlacement;
}

/** Which control of a row to focus again after the rows re-render (a move re-inserts the row's element). */
interface FocusRequest {
  readonly id: string;
  readonly control: string | null;
}

/**
 * Service sequence (SRS CP-42 / CP-42b): the global display and report order of services. The rows are reordered by
 * dragging (native HTML5 drag and drop), with the up / down buttons, Alt+↑ / Alt+↓ on the focused row, or by typing a
 * number: the row takes the place of that number and the rows in between shift. The visible rows keep the set of numbers
 * they already occupy — only their order changes — so a reorder inside a search, type or clinical-group filter never
 * collides with the rows outside it (INV-CAT-004); the server still checks the whole catalogue (CAT-033 / CAT-105). The
 * new order is saved in one request (F10) or discarded; leaving with an unsaved order asks first (`canLeave`).
 */
@Component({
  selector: 'app-service-sequence-page',
  imports: [
    PageHeaderComponent,
    SectionCardComponent,
    ToolbarComponent,
    SearchBoxComponent,
    DataTableComponent,
    CellTemplateDirective,
    BadgeComponent,
    IconComponent,
    CanDirective,
    SaveShortcutDirective,
    CatalogueExportButtonComponent,
  ],
  templateUrl: './service-sequence.page.html',
  styleUrls: ['./catalogue-list.css', './service-sequence.page.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(keydown)': 'onKeydown($event)' },
})
export class ServiceSequencePage {
  protected readonly i18n = inject(I18nService);
  protected readonly facade = inject(ServiceSequenceFacade);
  protected readonly options = inject(CatalogueAOptions);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly access = inject(PermissionsService);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  protected readonly permissions = CATALOGUE_A_PERMISSIONS;

  /** Viewers (no edit right) see the sequence read-only (UAT B-12); the server decides regardless. */
  protected readonly canEdit = computed(() => this.access.can(CATALOGUE_A_PERMISSIONS.edit));
  protected readonly types = SERVICE_TYPES;
  protected readonly groups = computed(() => (this.options.groups.hasValue() ? this.options.groups.value() : []));

  /** The stored rows, in the server's sequence order. */
  protected readonly rows = computed<readonly ServiceSequenceRow[]>(() => (this.facade.rows.hasValue() ? this.facade.rows.value() : []));

  /** The order on screen (ids); starts again from the stored order whenever the rows are (re)loaded. */
  private readonly order = linkedSignal<readonly ServiceSequenceRow[], readonly string[]>({ source: this.rows, computation: (rows) => rows.map((row) => row.id) });

  /** The rows in the on-screen order, each carrying the number its position gives it. */
  protected readonly view = computed(() => applyOrder(this.rows(), this.order()));

  /** What a save sends: the rows whose number differs from the stored one. */
  protected readonly changes = computed<readonly ServiceSequenceChange[]>(() => assignmentsOf(this.rows(), this.view()));
  protected readonly changedIds = computed(() => new Set(this.changes().map((change) => change.serviceId)));
  protected readonly dirty = computed(() => this.changes().length > 0);

  /** Text being typed into a position input, by row id, until it is committed (change / Enter) or abandoned. */
  private readonly typed = signal<Readonly<Record<string, string>>>({});
  protected readonly invalidIds = computed(() => new Set(Object.entries(this.typed()).filter(([, text]) => text !== '' && parseTypedNumber(text) === null).map(([id]) => id)));

  protected readonly dragging = signal<string | null>(null);
  protected readonly dropTarget = signal<DropTarget | null>(null);
  /** Announced to screen readers after every move. */
  protected readonly announcement = signal('');

  protected readonly canSave = computed(() => this.canEdit() && this.dirty() && this.invalidIds().size === 0 && !this.facade.save.pending());

  private readonly moveColumn: TableColumn<ServiceSequenceRow> = { key: 'move', header: 'catalogue-a.sequence.column.move', width: '118px' };

  private readonly baseColumns: readonly TableColumn<ServiceSequenceRow>[] = [
    { key: 'sequence', header: 'catalogue-a.sequence.column.sequence', width: '110px' },
    { key: 'code', header: 'catalogue-a.sequence.column.code', mono: true, width: '130px' },
    { key: 'name', header: 'catalogue-a.sequence.column.name', value: (row) => localizedPick({ en: row.nameEn, ar: row.nameAr }, this.i18n.lang()) },
    { key: 'type', header: 'catalogue-a.sequence.column.type', width: '130px' },
    { key: 'group', header: 'catalogue-a.sequence.column.group', secondary: true, value: (row) => this.groupName(row) },
    { key: 'status', header: 'catalogue-a.sequence.column.status', width: '110px' },
  ];

  protected readonly columns = computed<readonly TableColumn<ServiceSequenceRow>[]>(() => (this.canEdit() ? [this.moveColumn, ...this.baseColumns] : this.baseColumns));

  /** Row classes the table applies: the dragged row and the drop side of the row under the pointer. */
  protected readonly rowClass = (row: ServiceSequenceRow): string | null => {
    const classes: string[] = [];
    if (this.dragging() === row.id) {
      classes.push('dragging');
    }

    const target = this.dropTarget();
    if (target?.id === row.id) {
      classes.push(`drop-${target.placement}`);
    }

    if (this.changedIds().has(row.id)) {
      classes.push('moved');
    }

    return classes.length > 0 ? classes.join(' ') : null;
  };

  protected readonly exportCsv = (): Promise<void> => {
    const t = (key: string): string => this.i18n.t(`catalogue-a.sequence.column.${key}`);
    downloadCsv(
      csvFileName('service-sequence'),
      toCsv(this.rows(), [
        { header: t('sequence'), value: (row) => row.sequence },
        { header: t('code'), value: (row) => row.code },
        { header: this.i18n.t('catalogue-a.csv.nameEn'), value: (row) => row.nameEn },
        { header: this.i18n.t('catalogue-a.csv.nameAr'), value: (row) => row.nameAr },
        { header: t('type'), value: (row) => this.typeLabel(row.type) },
        { header: t('group'), value: (row) => this.groupName(row) },
        { header: this.i18n.t('catalogue-a.csv.active'), value: (row) => yesNo((key) => this.i18n.t(key), row.isActive) },
      ]),
    );
    return Promise.resolve();
  };

  constructor() {
    void this.i18n.load('catalogue-a');
    this.facade.refresh();   // the facade outlives the page: re-read on open (UAT D-14)
    this.options.refresh();
  }

  /** CanDeactivate: an unsaved order asks before the page is left. */
  canLeave(): Promise<boolean> | boolean {
    if (!this.dirty()) {
      return true;
    }

    return this.confirm.confirm({ title: this.i18n.t('catalogue-a.sequence.leaveTitle'), message: this.i18n.t('catalogue-a.sequence.leaveMessage'), danger: true });
  }

  protected typeLabel(type: ServiceType): string {
    return this.i18n.t(`catalogue-a.serviceType.${type}`);
  }

  protected groupLabel(option: Parameters<typeof optionLabel>[0]): string {
    return optionLabel(option, this.i18n.lang());
  }

  protected groupName(row: ServiceSequenceRow): string {
    return localizedPick({ en: row.clinicalGroupNameEn ?? '', ar: row.clinicalGroupNameAr ?? '' }, this.i18n.lang());
  }

  /** The text of a row's position input: what is being typed, else the row's number in the current order. */
  protected inputValue(row: ServiceSequenceRow): string {
    return this.typed()[row.id] ?? String(row.sequence);
  }

  protected indexOf(row: ServiceSequenceRow): number {
    return this.order().indexOf(row.id);
  }

  protected isFirst(row: ServiceSequenceRow): boolean {
    return this.indexOf(row) === 0;
  }

  protected isLast(row: ServiceSequenceRow): boolean {
    return this.indexOf(row) === this.order().length - 1;
  }

  protected handleLabel(row: ServiceSequenceRow): string {
    return this.i18n.tf('catalogue-a.sequence.dragHandle', { code: row.code, position: this.indexOf(row) + 1, count: this.order().length });
  }

  protected onType(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setType(this.types.find((type) => type === value) ?? null);
  }

  protected onGroup(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setGroup(value === '' ? null : value);
  }

  // --- moving rows -------------------------------------------------------------------------------------------------

  protected moveUp(row: ServiceSequenceRow, control: string | null = '.move-up'): void {
    this.moveTo(row, this.indexOf(row) - 1, control);
  }

  protected moveDown(row: ServiceSequenceRow, control: string | null = '.move-down'): void {
    this.moveTo(row, this.indexOf(row) + 1, control);
  }

  /** Alt+↑ / Alt+↓ anywhere inside a row moves that row. */
  protected onKeydown(event: KeyboardEvent): void {
    if (!event.altKey || (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') || !this.canEdit()) {
      return;
    }

    const target = event.target as HTMLElement | null;
    const row = this.rowOf(target);
    if (!row) {
      return;
    }

    event.preventDefault();
    event.stopPropagation();
    const control = target?.closest('tr') === target ? null : this.controlSelectorOf(target);
    if (event.key === 'ArrowUp') {
      this.moveUp(row, control);
    } else {
      this.moveDown(row, control);
    }
  }

  protected onTyped(row: ServiceSequenceRow, event: Event): void {
    if (!this.canEdit()) {
      return;
    }

    const text = (event.target as HTMLInputElement).value;
    this.typed.update((current) => ({ ...current, [row.id]: text }));
  }

  /** Change / Enter in a position input: the row takes the place of the typed number; an invalid entry is dropped. */
  protected commitTyped(row: ServiceSequenceRow, event: Event): void {
    if (!this.canEdit()) {
      return;
    }

    const input = event.target as HTMLInputElement;
    const text = this.typed()[row.id];
    this.typed.update((current) => Object.fromEntries(Object.entries(current).filter(([id]) => id !== row.id)));
    if (text === undefined) {
      return;
    }

    const value = parseTypedNumber(text);
    if (value !== null) {
      this.moveTo(row, positionOfNumber(occupiedNumbers(this.rows()), value) - 1, '.seq');
    }

    // the binding does not change when the row keeps its number, so the element is set straight
    input.value = String(this.view().find((candidate) => candidate.id === row.id)?.sequence ?? row.sequence);
  }

  // --- drag and drop (native HTML5) ----------------------------------------------------------------------------------

  protected onDragStart(row: ServiceSequenceRow, event: DragEvent): void {
    if (!this.canEdit()) {
      event.preventDefault();
      return;
    }

    event.dataTransfer?.setData('text/plain', row.id);
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move';
    }

    this.dragging.set(row.id);
  }

  protected onDragOver(event: DragEvent): void {
    const dragged = this.dragging();
    if (dragged === null) {
      return;
    }

    const tr = (event.target as HTMLElement | null)?.closest<HTMLTableRowElement>('tr[data-row]') ?? null;
    const id = tr?.getAttribute('data-row') ?? null;
    if (!tr || id === null || id === dragged) {
      this.dropTarget.set(null);
      return;
    }

    event.preventDefault();   // allows the drop
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }

    const rect = tr.getBoundingClientRect();
    const placement: DropPlacement = event.clientY < rect.top + rect.height / 2 ? 'before' : 'after';
    const current = this.dropTarget();
    if (current?.id !== id || current.placement !== placement) {
      this.dropTarget.set({ id, placement });
    }
  }

  protected onDragLeave(event: DragEvent): void {
    const next = event.relatedTarget as Node | null;
    if (!next || !(event.currentTarget as HTMLElement).contains(next)) {
      this.dropTarget.set(null);
    }
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    const dragged = this.dragging();
    const target = this.dropTarget();
    this.dragging.set(null);
    this.dropTarget.set(null);
    if (dragged === null || target === null) {
      return;
    }

    const order = this.order();
    const from = order.indexOf(dragged);
    const to = order.indexOf(target.id);
    const row = this.rows().find((candidate) => candidate.id === dragged);
    if (from < 0 || to < 0 || !row) {
      return;
    }

    this.moveTo(row, dropIndex(from, to, target.placement), '.handle');
  }

  protected onDragEnd(): void {
    this.dragging.set(null);
    this.dropTarget.set(null);
  }

  // --- saving ----------------------------------------------------------------------------------------------------

  protected async discard(): Promise<void> {
    if (!this.dirty()) {
      return;
    }

    if (!(await this.confirm.confirm({ title: this.i18n.t('catalogue-a.sequence.discard'), message: this.i18n.t('catalogue-a.sequence.confirmDiscard'), danger: true }))) {
      return;
    }

    this.order.set(this.rows().map((row) => row.id));
    this.typed.set({});
    this.facade.save.reset();
  }

  async save(): Promise<void> {
    if (!this.canSave()) {
      return;
    }

    const changes = this.changes();
    if ((await this.facade.save.execute(changes)) !== null) {
      this.typed.set({});
      this.toast.success(this.i18n.tc('catalogue-a.sequence.saved', changes.length));
    }
  }

  protected problemText(): string {
    return problemMessage(this.i18n, 'catalogue-a', this.facade.save.problem());
  }

  // --- helpers -----------------------------------------------------------------------------------------------------

  /** Moves a row to a final index of the on-screen order, announces it and keeps the focus on the moved row. */
  private moveTo(row: ServiceSequenceRow, to: number, control: string | null): void {
    if (!this.canEdit()) {
      return;
    }

    const order = this.order();
    const from = order.indexOf(row.id);
    const next = moveItem(order, from, to);
    if (next === order) {
      return;
    }

    this.order.set(next);
    this.facade.save.reset();
    const position = next.indexOf(row.id) + 1;
    this.announcement.set(this.i18n.tf('catalogue-a.sequence.moved', { code: row.code, position, count: next.length }));
    this.focusAfterRender({ id: row.id, control });
  }

  private rowOf(element: HTMLElement | null): ServiceSequenceRow | null {
    const id = element?.closest('tr[data-row]')?.getAttribute('data-row') ?? null;
    return id === null ? null : (this.rows().find((row) => row.id === id) ?? null);
  }

  private controlSelectorOf(element: HTMLElement | null): string | null {
    for (const selector of ['.handle', '.move-up', '.move-down', '.seq']) {
      if (element?.closest(selector)) {
        return selector;
      }
    }

    return null;
  }

  private focusAfterRender(request: FocusRequest): void {
    afterNextRender(
      {
        read: () => {
          const tr = Array.from(this.host.querySelectorAll<HTMLTableRowElement>('tr[data-row]')).find((candidate) => candidate.getAttribute('data-row') === request.id);
          const target = request.control === null ? tr : tr?.querySelector<HTMLElement>(request.control);
          target?.focus();
        },
      },
      { injector: this.injector },
    );
  }
}
