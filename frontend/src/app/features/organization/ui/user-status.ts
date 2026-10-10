import type { UserStatus } from '../../../core/api/organization.contracts';
import type { BadgeTone } from '../../../shared/ui/badge/status-tones';

/** Badge tone of a user status: active ok, locked bad, expired warn, inactive neutral. */
export const USER_STATUS_TONES: Readonly<Record<UserStatus, BadgeTone>> = { Active: 'ok', Locked: 'bad', Expired: 'warn', Inactive: 'neutral' };
