import type { BadgeTone } from '../../../shared/ui/badge/status-tones';
import { toneOf } from '../../../shared/ui/badge/status-tones';

/** Tones of the platform's own status names, beyond the kit's shared map (which already knows Active/Inactive/Pending/Completed). */
const PLATFORM_TONES: Readonly<Record<string, BadgeTone>> = {
  Trial: 'info',
  Subscription: 'violet',
  Manual: 'neutral',
  Idle: 'neutral',
  Declined: 'bad',
  TopUp: 'ok',
  Usage: 'neutral',
  Sms: 'info',
  Email: 'violet',
};

export function statusTone(status: string): BadgeTone {
  return PLATFORM_TONES[status] ?? toneOf(status);
}
