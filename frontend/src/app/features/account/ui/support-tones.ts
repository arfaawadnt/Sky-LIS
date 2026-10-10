import type { SupportTicketPriority, SupportTicketStatus } from '../../../core/api/account.contracts';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';

/**
 * Badge tones of the ticket statuses and priorities. `InProgress` and `Resolved` are not in the kit's
 * `STATUS_TONES` yet (requested from the UI kit owner); until then the module maps them here.
 */
export function statusTone(status: SupportTicketStatus): BadgeTone {
  switch (status) {
    case 'Open':
      return 'info';
    case 'InProgress':
      return 'warn';
    case 'Resolved':
      return 'ok';
    case 'Closed':
      return 'neutral';
  }
}

export function priorityTone(priority: SupportTicketPriority): BadgeTone {
  switch (priority) {
    case 'High':
      return 'bad';
    case 'Normal':
      return 'info';
    case 'Low':
      return 'neutral';
  }
}
