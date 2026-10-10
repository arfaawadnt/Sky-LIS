import { ChangeDetectionStrategy, Component, effect, inject, input, model, output, untracked } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';

import type { CommentTemplateOption, ResultRowView } from '../../../core/api/results.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { SaveShortcutDirective } from '../../../shared/forms/save-shortcut.directive';
import { DialogComponent } from '../../../shared/ui/dialog/dialog.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { SearchBoxComponent } from '../../../shared/ui/search-box/search-box.component';
import { pickName } from './result-format';

/**
 * The report comment of a result (T10; prints under the test, LDM §5.4 "Comment"): free text up to 2 000
 * characters and the comment library (CP-47) to insert a template's text in the report language. An empty text
 * removes the comment. Ctrl+S saves, Esc closes.
 */
@Component({
  selector: 'app-comment-dialog',
  imports: [ReactiveFormsModule, DialogComponent, FormFieldComponent, SearchBoxComponent, SaveShortcutDirective],
  template: `
    <app-dialog [(open)]="open" [title]="i18n.t('results.comment.title')" size="lg">
      @if (row(); as current) {
        <form appSaveShortcut (appSaveShortcut)="submit()" (ngSubmit)="submit()" novalidate>
          <p class="muted small">{{ testName(current) }} · {{ i18n.t('results.comment.printsOnReport') }}</p>
          <div class="layout">
            <app-form-field [label]="i18n.t('results.comment.text')">
              <textarea rows="8" [formControl]="text" maxlength="2000"></textarea>
            </app-form-field>
            <aside class="library">
              <app-search-box [placeholder]="i18n.t('results.comment.searchLibrary')" [label]="i18n.t('results.comment.library')" [debounce]="200" (searchChange)="searchChange.emit($event)" />
              <ul class="templates">
                @for (template of templates(); track template.id) {
                  <li>
                    <button type="button" class="template" (click)="insert(template)">
                      <b>{{ template.shortName }}</b>
                      <span class="small muted">{{ textOf(template) }}</span>
                    </button>
                  </li>
                } @empty {
                  <li class="small muted">{{ i18n.t('results.comment.noTemplates') }}</li>
                }
              </ul>
            </aside>
          </div>
        </form>
      }
      <ng-container dialogFooter>
        <button type="button" class="btn" (click)="open.set(false)">{{ i18n.t('results.cancel') }}</button>
        <button type="button" class="btn primary" [disabled]="busy()" (click)="submit()">{{ i18n.t('results.save') }}</button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    .layout { display: grid; grid-template-columns: minmax(0, 3fr) minmax(0, 2fr); gap: var(--sp-4); }
    .library { display: grid; gap: var(--sp-2); align-content: start; }
    .templates { list-style: none; margin: 0; padding: 0; max-height: 260px; overflow: auto; display: grid; gap: 4px; }
    .template { display: grid; gap: 2px; width: 100%; text-align: start; padding: 6px 8px; border: 1px solid var(--line-soft); border-radius: var(--r-sm); background: var(--surface); cursor: pointer; font: inherit; color: inherit; }
    .template:hover { background: var(--surface-2); }
    .template span { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    @media (max-width: 720px) { .layout { grid-template-columns: 1fr; } }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommentDialogComponent {
  protected readonly i18n = inject(I18nService);

  readonly open = model(false);
  readonly row = input<ResultRowView | null>(null);
  readonly templates = input<readonly CommentTemplateOption[]>([]);
  readonly busy = input(false);

  readonly searchChange = output<string>();
  readonly submitted = output<{ readonly row: ResultRowView; readonly text: string | null }>();

  protected readonly text = new FormControl('', { nonNullable: true, validators: [Validators.maxLength(2000)] });

  constructor() {
    effect(() => {
      const row = this.row();
      if (this.open() && row) {
        untracked(() => this.text.setValue(row.reportComment ?? ''));
      }
    });
  }

  protected insert(template: CommentTemplateOption): void {
    const current = this.text.value.trimEnd();
    this.text.setValue(current.length === 0 ? this.textOf(template) : `${current}\n${this.textOf(template)}`);
  }

  protected submit(): void {
    const row = this.row();
    if (!row) {
      return;
    }

    const value = this.text.value.trim();
    this.submitted.emit({ row, text: value.length === 0 ? null : value });
  }

  protected textOf(template: CommentTemplateOption): string {
    return pickName(template.textEn, template.textAr, this.i18n.lang());
  }

  protected testName(row: ResultRowView): string {
    return pickName(row.reportName ?? row.testNameEn, row.testNameAr, this.i18n.lang());
  }
}
