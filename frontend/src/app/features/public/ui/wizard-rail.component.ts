import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { IconComponent } from '../../../shared/ui/icon/icon.component';

export interface WizardRailStep {
  readonly step: number;
  readonly label: string;
}

/**
 * The step rail of the subscription wizard (CP-02). A done step the wizard can return to (`openable`, LIS-CR-014 S3) is a
 * button that emits `open`; the others are plain text. The current step carries `aria-current="step"`.
 */
@Component({
  selector: 'app-wizard-rail',
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol class="steps" [attr.aria-label]="label()">
      @for (item of steps(); track item.step) {
        <li class="step" [class.active]="current() === item.step" [class.done]="current() > item.step" [attr.aria-current]="current() === item.step ? 'step' : null">
          @if (openable().includes(item.step)) {
            <button type="button" class="step-link" [attr.title]="openHint()" (click)="open.emit(item.step)">
              <span class="step-dot"><app-icon name="check" [size]="14" /></span>
              <span>{{ item.label }}</span>
            </button>
          } @else {
            <span class="step-dot">
              @if (current() > item.step) {
                <app-icon name="check" [size]="14" />
              } @else {
                {{ item.step }}
              }
            </span>
            <span>{{ item.label }}</span>
          }
        </li>
      }
    </ol>
  `,
  styles: `
    .steps {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: var(--sp-2);
      list-style: none;
      padding: 0;
      margin: var(--sp-4) 0 var(--sp-5);
    }

    .step,
    .step-link {
      display: flex;
      align-items: center;
      gap: var(--sp-2);
      color: var(--muted);
      font-size: var(--fs-sm);
      font-weight: 600;
    }

    .step {
      padding-block-end: var(--sp-2);
      border-block-end: 3px solid var(--line-soft);
    }

    .step-link {
      padding: 0;
      border: 0;
      background: none;
      color: inherit;
      font: inherit;
      cursor: pointer;
      text-align: start;
    }

    .step-link:hover span:last-child,
    .step-link:focus-visible span:last-child {
      text-decoration: underline;
    }

    .step.active {
      color: var(--blue-2);
      border-block-end-color: var(--blue);
    }

    .step.done {
      color: var(--ok);
      border-block-end-color: var(--ok);
    }

    .step-dot {
      display: inline-grid;
      place-items: center;
      flex: none;
      width: 26px;
      height: 26px;
      border-radius: 50%;
      background: var(--line-soft);
      color: var(--ink-2);
      font-size: var(--fs-xs);
    }

    .step.active .step-dot {
      background: var(--grad);
      color: #fff;
    }

    .step.done .step-dot {
      background: var(--ok-soft);
      color: var(--ok);
    }

    @media (max-width: 720px) {
      .steps {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }
  `,
})
export class WizardRailComponent {
  readonly steps = input.required<readonly WizardRailStep[]>();
  readonly current = input.required<number>();
  /** The done steps the user may return to. */
  readonly openable = input<readonly number[]>([]);
  readonly label = input('');
  readonly openHint = input('');

  readonly open = output<number>();
}
