import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';

import { I18nService } from '../../../core/i18n/i18n.service';
import { IconComponent } from '../../../shared/ui/icon/icon.component';

export type FormulaToolGroup = 'operators' | 'compare' | 'functions' | 'patient';

/**
 * One button of the formula tools (LIS-CR-015 M3a): `before` is inserted at the caret, a selection goes between `before` and
 * `after`, and the caret lands after `before` (+ the selection) — inside the parentheses of a function.
 */
export interface FormulaTool {
  readonly id: string;
  readonly group: FormulaToolGroup;
  readonly label: string;
  readonly before: string;
  readonly after: string;
}

const tool = (id: string, group: FormulaToolGroup, label: string, before: string, after = ''): FormulaTool => ({ id, group, label, before, after });

/** The tools in their groups, in the order they show (symbols stay as they are in both languages). */
export const FORMULA_TOOLS: readonly FormulaTool[] = [
  tool('plus', 'operators', '+', ' + '),
  tool('minus', 'operators', '−', ' - '),
  tool('times', 'operators', '×', ' * '),
  tool('divide', 'operators', '÷', ' / '),
  tool('power', 'operators', '^', ' ^ '),
  tool('open', 'operators', '(', '('),
  tool('close', 'operators', ')', ')'),
  tool('comma', 'operators', ',', ', '),
  tool('lt', 'compare', '<', ' < '),
  tool('le', 'compare', '<=', ' <= '),
  tool('gt', 'compare', '>', ' > '),
  tool('ge', 'compare', '>=', ' >= '),
  tool('eq', 'compare', '=', ' = '),
  tool('ne', 'compare', '<>', ' <> '),
  tool('round', 'functions', 'ROUND( )', 'ROUND(', ')'),
  tool('if', 'functions', 'IF( , , )', 'IF(', ', , )'),
  tool('min', 'functions', 'MIN( )', 'MIN(', ')'),
  tool('max', 'functions', 'MAX( )', 'MAX(', ')'),
  tool('abs', 'functions', 'ABS( )', 'ABS(', ')'),
  tool('age', 'patient', '@AGE', '@AGE'),
  tool('female', 'patient', '@FEMALE', '@FEMALE'),
];

export const FORMULA_TOOL_GROUPS: readonly FormulaToolGroup[] = ['operators', 'compare', 'functions', 'patient'];

/** The text after a tool is applied to `value` with the selection `start…end`, and where the caret goes. */
export function applyFormulaTool(value: string, start: number, end: number, applied: Pick<FormulaTool, 'before' | 'after'>, maxLength = 1000): { readonly value: string; readonly caret: number } {
  const from = Math.max(0, Math.min(start, end, value.length));
  const to = Math.min(value.length, Math.max(start, end, from));
  const selected = applied.after === '' ? '' : value.slice(from, to);
  const next = value.slice(0, from) + applied.before + selected + applied.after + value.slice(to);
  if (next.length > maxLength) {
    return { value, caret: to };
  }

  return { value: next, caret: from + applied.before.length + selected.length };
}

/**
 * `<app-formula-tools (apply)="…" (clear)="…" />`: the formula tools above the expression (LIS-CR-015 M3a) — operators, comparisons,
 * functions and the patient variables as buttons, grouped, each with a title explaining it, plus *Clear*. The page applies a tool at
 * the caret ({@link applyFormulaTool}). Buttons are plain, so Tab reaches every one.
 */
@Component({
  selector: 'app-formula-tools',
  imports: [IconComponent],
  template: `
    <div class="tools" role="toolbar" [attr.aria-label]="i18n.t('catalogue-a.tests.formula.tools.label')">
      @for (group of groups; track group) {
        <div class="group" role="group" [attr.aria-label]="i18n.t('catalogue-a.tests.formula.tools.' + group)">
          <span class="name" aria-hidden="true">{{ i18n.t('catalogue-a.tests.formula.tools.' + group) }}</span>
          @for (item of toolsOf(group); track item.id) {
            <button
              type="button"
              class="tool"
              dir="ltr"
              [disabled]="disabled()"
              [title]="i18n.t('catalogue-a.tests.formula.tools.title.' + item.id)"
              [attr.aria-label]="i18n.t('catalogue-a.tests.formula.tools.title.' + item.id)"
              [attr.data-tool]="item.id"
              (mousedown)="$event.preventDefault()"
              (click)="apply.emit(item)"
            >
              {{ item.label }}
            </button>
          }
        </div>
      }
      <button type="button" class="btn sm ghost clear" [disabled]="disabled()" [title]="i18n.t('catalogue-a.tests.formula.tools.title.clear')" (click)="clear.emit()">
        <app-icon name="x" [size]="13" />{{ i18n.t('catalogue-a.tests.formula.tools.clear') }}
      </button>
    </div>
  `,
  styles: `
    .tools {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--sp-2) var(--sp-3);
      margin-block-end: var(--sp-2);
    }

    .group {
      display: inline-flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 4px;
    }

    .name {
      font-size: var(--fs-xs);
      font-weight: 600;
      color: var(--muted);
      margin-inline-end: 2px;
    }

    .tool {
      min-width: 2rem;
      padding: 3px 7px;
      border: 1px solid var(--line);
      border-radius: var(--r-sm);
      background: var(--surface);
      color: var(--ink);
      font-family: var(--mono);
      font-size: var(--fs-sm);
      cursor: pointer;
    }

    .tool:hover:not(:disabled) {
      border-color: var(--blue);
      background: var(--blue-soft);
    }

    .tool:focus-visible {
      outline: none;
      box-shadow: var(--focus-ring);
    }

    .tool:disabled {
      opacity: 0.5;
      cursor: default;
    }

    .clear {
      margin-inline-start: auto;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FormulaToolsComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly groups = FORMULA_TOOL_GROUPS;

  readonly disabled = input(false);
  readonly apply = output<FormulaTool>();
  readonly clear = output();

  protected toolsOf(group: FormulaToolGroup): readonly FormulaTool[] {
    return FORMULA_TOOLS.filter((item) => item.group === group);
  }
}
