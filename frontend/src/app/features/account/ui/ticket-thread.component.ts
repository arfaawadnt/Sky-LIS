import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import type { SupportTicketDetail } from '../../../core/api/account.contracts';
import { I18nService } from '../../../core/i18n/i18n.service';
import { BadgeComponent } from '../../../shared/ui/badge/badge.component';
import { FormFieldComponent } from '../../../shared/ui/form-field/form-field.component';
import { formatDateTime, pick } from '../data/account-format';
import { priorityTone, statusTone } from './support-tones';

/**
 * One support ticket as a conversation (CP-67 and the desk): the header (number, laboratory, status, priority,
 * opened by), the description, the replies (the platform's on the other side) and the reply box. Dumb: the page
 * passes the ticket and handles `reply`; transition buttons are projected into `[threadActions]`.
 * Ctrl+Enter sends the reply.
 */
@Component({
  selector: 'app-ticket-thread',
  imports: [ReactiveFormsModule, BadgeComponent, FormFieldComponent],
  template: `
    @let t = ticket();
    <header class="meta">
      <span class="mono number">{{ t.number }}</span>
      <app-badge [tone]="statusTone(t.status)">{{ i18n.t('account.support.status.' + t.status) }}</app-badge>
      <app-badge [tone]="priorityTone(t.priority)">{{ i18n.t('account.support.priority.' + t.priority) }}</app-badge>
      @if (showLaboratory()) {
        <span class="lab">{{ laboratory() }}</span>
      }
    </header>
    <h3 class="subject">{{ t.subject }}</h3>
    <p class="muted small">{{ i18n.tf('account.support.openedBy', { name: t.openedByName || '—', at: when(t.openedAtUtc) }) }}</p>
    <div class="message description">{{ t.description }}</div>

    <ol class="replies" [attr.aria-label]="i18n.t('account.support.replies')">
      @for (reply of t.replies; track reply.id) {
        <li class="message" [class.platform]="reply.fromPlatform">
          <div class="who">
            <b>{{ reply.authorName || '—' }}</b>
            @if (reply.fromPlatform) {
              <span class="tag">{{ i18n.t('account.support.fromPlatform') }}</span>
            }
            <time [attr.datetime]="reply.atUtc">{{ when(reply.atUtc) }}</time>
          </div>
          <div class="text">{{ reply.text }}</div>
        </li>
      } @empty {
        <li class="muted small none">{{ i18n.t('account.support.noReplies') }}</li>
      }
    </ol>

    <div class="actions"><ng-content select="[threadActions]" /></div>

    @if (canReply() && t.status !== 'Closed') {
      <form class="reply" [formGroup]="replyForm" (ngSubmit)="send()" novalidate>
        <app-form-field [label]="i18n.t('account.support.yourReply')" [hint]="i18n.t('account.support.replyHint')">
          <textarea formControlName="text" rows="4" maxlength="4000" (keydown.control.enter)="send()"></textarea>
        </app-form-field>
        @if (problem(); as message) {
          <p class="problem" role="alert">{{ message }}</p>
        }
        <button type="submit" class="btn primary" [disabled]="pending()">{{ pending() ? i18n.t('account.saving') : i18n.t('account.support.sendReply') }}</button>
      </form>
    } @else if (t.status === 'Closed') {
      <p class="info-strip">{{ i18n.t('account.support.closedNote') }}</p>
    }
  `,
  styles: `
    :host {
      display: grid;
      gap: var(--sp-3);
    }

    .meta {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--sp-2);
    }

    .number {
      font-weight: 700;
      color: var(--navy);
    }

    .lab {
      margin-inline-start: auto;
      color: var(--ink-2);
      font-weight: 600;
    }

    .subject {
      margin: 0;
      font-size: var(--fs-lg);
      color: var(--ink);
    }

    p {
      margin: 0;
    }

    .message {
      background: var(--surface-2);
      border: 1px solid var(--line-soft);
      border-radius: var(--r);
      padding: var(--sp-3);
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }

    .description {
      background: var(--paper);
      border-color: var(--line);
    }

    .replies {
      list-style: none;
      padding: 0;
      margin: 0;
      display: grid;
      gap: var(--sp-2);
    }

    .replies .message {
      margin-inline-end: 12%;
    }

    .replies .message.platform {
      margin-inline-end: 0;
      margin-inline-start: 12%;
      background: var(--blue-soft);
      border-color: var(--blue-soft);
    }

    .who {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--sp-2);
      margin-bottom: var(--sp-1);
      white-space: normal;
      font-size: var(--fs-sm);
    }

    .who time {
      margin-inline-start: auto;
      color: var(--muted);
      font-size: var(--fs-xs);
    }

    .tag {
      background: var(--violet-soft);
      color: var(--violet-2);
      border-radius: var(--r-pill);
      padding: 1px 8px;
      font-size: var(--fs-xs);
      font-weight: 700;
    }

    .none {
      padding: var(--sp-2) 0;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--sp-2);
    }

    .actions:empty {
      display: none;
    }

    .reply {
      display: grid;
      gap: var(--sp-2);
      justify-items: start;
    }

    .reply app-form-field {
      width: 100%;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TicketThreadComponent {
  protected readonly i18n = inject(I18nService);

  readonly ticket = input.required<SupportTicketDetail>();
  readonly canReply = input(false);
  readonly pending = input(false);
  readonly problem = input<string | null>(null);
  readonly showLaboratory = input(false);

  readonly replied = output<string>();

  protected readonly text = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(4000)] });
  protected readonly replyForm = new FormGroup({ text: this.text });

  protected readonly statusTone = statusTone;
  protected readonly priorityTone = priorityTone;

  private seenReplies = -1;
  private seenTicket: string | null = null;

  constructor() {
    // A reply that reached the server shows up as one more message: the box empties then (and on another ticket).
    effect(() => {
      const ticket = this.ticket();
      if (ticket.id !== this.seenTicket || ticket.replies.length > this.seenReplies) {
        if (this.seenTicket !== null) {
          this.text.reset();
        }

        this.seenTicket = ticket.id;
        this.seenReplies = ticket.replies.length;
      }
    });
  }

  protected readonly laboratory = computed(() => pick(this.ticket().laboratoryNameEn, this.ticket().laboratoryNameAr, this.i18n.lang()) || this.ticket().tenantId);

  protected when(value: string): string {
    return formatDateTime(value, this.i18n.lang());
  }

  protected send(): void {
    const value = this.text.value.trim();
    if (value.length === 0 || this.pending()) {
      this.text.markAsTouched();
      return;
    }

    this.replied.emit(value);
  }

}
