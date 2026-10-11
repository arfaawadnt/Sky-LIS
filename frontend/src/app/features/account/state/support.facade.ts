import { Injectable, inject, resource, signal } from '@angular/core';

import type { DeskTicketFilter, OpenSupportTicketRequest, SupportTicketFilter, SupportTicketPriority, SupportTicketStatus } from '../../../core/api/account.contracts';
import type { PageRequest } from '../../../core/api/paging';
import { mutation } from '../../../core/state/mutation';
import { AccountApi } from '../data/account.api';

/**
 * CP-67 state of the laboratory side: the tickets register (status, priority, search, mine, page), the open ticket
 * with its replies, and the laboratory's transitions (open, reply, reopen, close). Every write reloads what it changed.
 */
@Injectable()
export class SupportFacade {
  private readonly api = inject(AccountApi);

  readonly filter = signal<SupportTicketFilter>({ status: null, priority: null, search: '', mine: false, page: 1, pageSize: 20 });
  readonly selectedId = signal<string | null>(null);

  readonly tickets = resource({ params: () => this.filter(), loader: ({ params }) => this.api.tickets(params) });

  readonly detail = resource({
    params: () => this.selectedId() ?? undefined,
    loader: ({ params }) => this.api.ticket(params),
  });

  readonly open = mutation((request: OpenSupportTicketRequest) => this.api.openTicket(request), {
    then: (created) => {
      this.tickets.reload();
      this.selectedId.set(created.id);
    },
  });

  readonly reply = mutation((input: { id: string; text: string }) => this.api.replyToTicket(input.id, input.text), { then: () => this.reloadAll() });

  readonly reopen = mutation((id: string) => this.api.reopenTicket(id), { then: () => this.reloadAll() });

  readonly close = mutation((id: string) => this.api.closeTicket(id), { then: () => this.reloadAll() });

  setStatus(status: SupportTicketStatus | null): void {
    this.filter.update((current) => ({ ...current, status, page: 1 }));
  }

  setPriority(priority: SupportTicketPriority | null): void {
    this.filter.update((current) => ({ ...current, priority, page: 1 }));
  }

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search: search.trim(), page: 1 }));
  }

  setMine(mine: boolean): void {
    this.filter.update((current) => ({ ...current, mine, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  select(id: string | null): void {
    this.selectedId.set(id);
    this.reply.reset();
    this.reopen.reset();
    this.close.reset();
  }

  /** Re-reads the tickets and the open ticket when the page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14, A-01/C-13). A load in flight is left alone. */
  refresh(): void {
    this.open.reset();
    this.reloadAll();
  }

  private reloadAll(): void {
    this.tickets.reload();
    this.detail.reload();
  }
}

/**
 * The platform desk's state (Super Admin portal): every laboratory's tickets (laboratory, status, priority, search,
 * page), the open ticket, and the desk's transitions (answer, start, resolve with a note, close).
 */
@Injectable()
export class SupportDeskFacade {
  private readonly api = inject(AccountApi);

  readonly filter = signal<DeskTicketFilter>({ tenantId: null, status: 'Open', priority: null, search: '', page: 1, pageSize: 20 });
  readonly selectedId = signal<string | null>(null);

  readonly tickets = resource({ params: () => this.filter(), loader: ({ params }) => this.api.deskTickets(params) });

  readonly detail = resource({
    params: () => this.selectedId() ?? undefined,
    loader: ({ params }) => this.api.deskTicket(params),
  });

  readonly answer = mutation((input: { id: string; text: string }) => this.api.answerTicket(input.id, input.text), { then: () => this.reloadAll() });

  readonly start = mutation((id: string) => this.api.startTicket(id), { then: () => this.reloadAll() });

  readonly resolve = mutation((input: { id: string; note: string | null }) => this.api.resolveTicket(input.id, input.note), { then: () => this.reloadAll() });

  readonly close = mutation((id: string) => this.api.closeTicketAtDesk(id), { then: () => this.reloadAll() });

  setStatus(status: SupportTicketStatus | null): void {
    this.filter.update((current) => ({ ...current, status, page: 1 }));
  }

  setPriority(priority: SupportTicketPriority | null): void {
    this.filter.update((current) => ({ ...current, priority, page: 1 }));
  }

  setSearch(search: string): void {
    this.filter.update((current) => ({ ...current, search: search.trim(), page: 1 }));
  }

  /** Narrows the register to one laboratory (from a row), or back to all. */
  setLaboratory(tenantId: string | null): void {
    this.filter.update((current) => ({ ...current, tenantId, page: 1 }));
  }

  setPage(page: PageRequest): void {
    this.filter.update((current) => ({ ...current, page: page.page, pageSize: page.pageSize }));
  }

  select(id: string | null): void {
    this.selectedId.set(id);
    this.answer.reset();
    this.start.reset();
    this.resolve.reset();
    this.close.reset();
  }

  /** Re-reads the desk's tickets and the open ticket when the page opens again: the facade outlives the page (route provider), so the previous visit's answer — even another sign-in's — must never stay on screen (UAT D-14, A-01/C-13). A load in flight is left alone. */
  refresh(): void {
    this.reloadAll();
  }

  private reloadAll(): void {
    this.tickets.reload();
    this.detail.reload();
  }
}
