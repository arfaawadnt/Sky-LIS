import { Directive, type OnInit, computed, inject, signal } from '@angular/core';
import type { FormGroup } from '@angular/forms';

import { CATALOGUE_B_PERMISSIONS } from '../../../core/api/catalogue-b.contracts';
import { type ProblemDetails, ProblemCodes } from '../../../core/api/problem-details';
import { I18nService } from '../../../core/i18n/i18n.service';
import { PermissionsService } from '../../../core/permissions/permissions.service';
import { markAllTouched } from '../../../shared/forms/mark-all-touched';
import { problemMessage } from '../../../shared/forms/problem-text';
import { problemToFormErrors } from '../../../shared/forms/problem-to-form-errors';
import type { TableColumn } from '../../../shared/ui/data-table/data-table.types';
import { ConfirmService } from '../../../shared/ui/dialog/confirm.service';
import { ToastService } from '../../../shared/ui/toast/toast.service';
import type { CatalogueBListFacade } from '../state-b/catalogue-b-list.facade';

/** Every Catalogue B record carries an id and the active flag. */
export interface CatalogueBRow {
  readonly id: string;
  readonly isActive: boolean;
}

/** The module dictionary's root; problem codes resolve as `catalogue-b.error.<CODE>`. */
export const CATALOGUE_B_I18N = 'catalogue-b';

/**
 * The behaviour shared by the hand-written Catalogue B registers (the lists whose editor holds pickers, which the
 * kit's master-data page cannot host): search and status filter, paging, an add/edit drawer with Ctrl+S and Esc,
 * `VALIDATION-400` on the fields and coded refusals above the form, confirmed deactivation, CSV export, toasts. Each
 * page supplies its form, how a row fills it and how the form becomes a request.
 */
@Directive()
export abstract class CatalogueBEditorPage<TItem extends CatalogueBRow, TCreate, TUpdate> implements OnInit {
  protected readonly i18n = inject(I18nService);
  protected readonly confirm = inject(ConfirmService);
  protected readonly toast = inject(ToastService);
  protected readonly permissions = CATALOGUE_B_PERMISSIONS;
  private readonly access = inject(PermissionsService);

  protected abstract readonly facade: CatalogueBListFacade<TItem, TCreate, TUpdate>;
  /** `catalogue-b.<list>`: title, subtitle, add, edit, column.*, field.*, hint.* of the page. */
  protected abstract readonly prefix: string;
  protected abstract readonly form: FormGroup;
  protected abstract readonly columns: readonly TableColumn<TItem>[];

  protected readonly drawerOpen = signal(false);
  protected readonly editing = signal<TItem | null>(null);
  protected readonly saveProblem = signal<ProblemDetails | null>(null);
  protected readonly saving = computed(() => this.facade.create.pending() || this.facade.update.pending());
  /**
   * R-07: a row opened without `catalogue.edit` is a read-only view (fields disabled, no Save nor F10, title "View"); the
   * server refuses the change anyway, the screen no longer offers it.
   */
  protected readonly readOnly = computed(() => this.editing() !== null && !this.access.can(this.permissions.edit));
  protected readonly drawerTitle = computed(() => this.text(this.editing() === null ? 'add' : this.readOnly() ? 'view' : 'edit'));

  constructor() {
    void this.i18n.load(CATALOGUE_B_I18N);
  }

  /** The facade outlives the page (route provider): re-read the list on open (UAT D-14). Runs once the subclass fields exist. */
  ngOnInit(): void {
    this.facade.refresh();
  }

  /** `text('add')` → `<prefix>.add`, else `catalogue-b.common.add`. */
  protected text(key: string): string {
    const own = `${this.prefix}.${key}`;
    return this.i18n.has(own) ? this.i18n.t(own) : this.i18n.t(`${CATALOGUE_B_I18N}.common.${key}`);
  }

  protected problemText(problem: ProblemDetails | { readonly code: string }): string {
    return problemMessage(this.i18n, CATALOGUE_B_I18N, problem);
  }

  protected onStatusFilter(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.facade.setActiveFilter(value === 'all' ? null : value === 'active');
  }

  openCreate(): void {
    this.editing.set(null);
    this.saveProblem.set(null);
    this.form.reset();
    this.fill(null);
    this.form.enable();
    this.lockOnEdit(false);
    this.drawerOpen.set(true);
  }

  openEdit(item: TItem): void {
    this.editing.set(item);
    this.saveProblem.set(null);
    this.form.reset();
    this.fill(item);
    this.form.enable();
    this.lockOnEdit(true);
    if (this.readOnly()) {
      this.form.disable();
    }

    this.drawerOpen.set(true);
  }

  protected closeDrawer(): void {
    this.drawerOpen.set(false);
  }

  protected onDrawerClosed(): void {
    this.editing.set(null);
    this.saveProblem.set(null);
  }

  async save(): Promise<void> {
    if (this.saving() || this.readOnly() || !markAllTouched(this.form)) {
      return;
    }

    this.saveProblem.set(null);
    const item = this.editing();
    const succeeded = item === null
      ? (await this.facade.create.execute(this.createRequest())) !== null
      : (await this.facade.update.execute({ id: item.id, request: this.updateRequest() })) !== null;
    if (succeeded) {
      this.toast.success(this.text('saved'));
      this.drawerOpen.set(false);
      return;
    }

    const problem = item === null ? this.facade.create.problem() : this.facade.update.problem();
    if (problem === null) {
      return;
    }

    if (problem.code === ProblemCodes.validationFailed) {
      const unmatched = problemToFormErrors(problem, this.form);
      if (Object.keys(unmatched).length === 0 && problem.errors && Object.keys(problem.errors).length > 0) {
        return;
      }
    }

    this.saveProblem.set(problem);
  }

  protected async toggleActive(item: TItem): Promise<void> {
    const key = item.isActive ? 'confirmDeactivate' : 'confirmActivate';
    const confirmed = await this.confirm.confirm({ title: this.text(`${key}.title`), message: this.text(`${key}.message`), danger: item.isActive });
    if (!confirmed) {
      return;
    }

    if ((await this.facade.activation.execute({ id: item.id, active: !item.isActive })) !== null) {
      this.toast.success(this.text(item.isActive ? 'deactivated' : 'activated'));
      return;
    }

    const problem = this.facade.activation.problem();
    this.toast.error(problem ? this.problemText(problem) : this.i18n.t('shared.error.generic'));
  }

  protected async exportCsv(): Promise<void> {
    if ((await this.facade.exportCsv.execute(undefined)) !== null) {
      this.toast.success(this.text('exported'));
      return;
    }

    const problem = this.facade.exportCsv.problem();
    this.toast.error(problem ? this.problemText(problem) : this.text('exportError'));
  }

  /** Fills the form for a new record (`null`) or for the row being edited. */
  protected abstract fill(item: TItem | null): void;

  protected abstract createRequest(): TCreate;

  protected abstract updateRequest(): TUpdate;

  /** Disables what never changes after creation (the code). */
  protected abstract lockOnEdit(editing: boolean): void;
}
