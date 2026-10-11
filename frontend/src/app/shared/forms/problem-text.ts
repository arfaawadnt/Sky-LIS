import type { FieldRule, ProblemDetails } from '../../core/api/problem-details';
import type { I18nService } from '../../core/i18n/i18n.service';

const VALIDATION_FAILED = 'VALIDATION-400';

/**
 * The message for a problem code, looked up in this order: `<prefix>.error.<CODE>` (the module's own text), then
 * `shared.error.<CODE>` (platform-wide codes), then `shared.error.generic`. Modules translate their own codes in
 * their dictionary (MODULE-PATTERN §4) and never show the server's English title.
 *
 * A `VALIDATION-400` with field errors also names each field and its localized reason (C-06): "Please check the entered
 * values. Currency: Exactly 3 characters." The field label is the module's `<prefix>.field.<name>` when it has one.
 */
export function problemMessage(i18n: I18nService, prefix: string | null, problem: ProblemDetails | { code: string } | null): string {
  if (problem === null) {
    return '';
  }

  const candidates = [prefix ? `${prefix}.error.${problem.code}` : null, `shared.error.${problem.code}`].filter((key): key is string => key !== null);
  let text = i18n.t('shared.error.generic');
  for (const key of candidates) {
    if (i18n.has(key)) {
      text = i18n.t(key);
      break;
    }
  }

  if (problem.code !== VALIDATION_FAILED || !('errors' in problem) || !problem.errors) {
    return text;
  }

  const lines = Object.entries(problem.errors)
    .filter(([, messages]) => messages.length > 0)
    .map(([key, messages]) => {
      const reason = fieldErrorText(i18n, problem.rules?.[key], messages);
      const label = fieldLabel(i18n, prefix, key);
      return label === null ? reason : i18n.tf('shared.validation.field', { field: label, message: reason });
    });

  return lines.length === 0 ? text : [text, ...lines].join(' ');
}

/** `Lines[0].NameAr` → `lines.0.nameAr`. Kept local so this file stays free of the form helpers. */
function controlPath(serverKey: string): string {
  return serverKey
    .replace(/\[(\d+)\]/g, '.$1')
    .split('.')
    .filter((segment) => segment.length > 0)
    .map((segment) => segment.charAt(0).toLowerCase() + segment.slice(1))
    .join('.');
}

/** The module's label of a server field (`<prefix>.field.<name>`; `nameAr` → "Name (Arabic)"), or `null` when it has none. */
function fieldLabel(i18n: I18nService, prefix: string | null, serverKey: string): string | null {
  if (prefix === null) {
    return null;
  }

  const name = controlPath(serverKey).split('.').filter((segment) => !/^\d+$/.test(segment)).at(-1) ?? '';
  if (name === '') {
    return null;
  }

  const direct = `${prefix}.field.${name}`;
  if (i18n.has(direct)) {
    return i18n.t(direct);
  }

  const localized = /^(.*)(En|Ar)$/.exec(name);
  if (localized) {
    const base = `${prefix}.field.${localized[1] ?? ''}`;
    if (i18n.has(base)) {
      return `${i18n.t(base)} ${i18n.t(localized[2] === 'Ar' ? 'shared.localized.arSuffix' : 'shared.localized.enSuffix')}`;
    }
  }

  return null;
}

function numberArgument(rule: FieldRule, name: string): number | null {
  const value = rule[name];
  return typeof value === 'number' ? value : null;
}

/** The localized text of one failed server rule (FluentValidation codes without "Validator"), or `null` when unknown. */
export function ruleText(i18n: I18nService, rule: FieldRule): string | null {
  const min = numberArgument(rule, 'minLength');
  const max = numberArgument(rule, 'maxLength');
  const comparison = numberArgument(rule, 'comparisonValue');
  const from = numberArgument(rule, 'from');
  const to = numberArgument(rule, 'to');
  switch (rule.rule) {
    case 'NotEmpty':
    case 'NotNull':
      return i18n.t('shared.validation.required');
    case 'MaximumLength':
      return max === null ? null : i18n.tf('shared.validation.maxlength', { max });
    case 'MinimumLength':
      return min === null ? null : i18n.tf('shared.validation.minlength', { min });
    case 'ExactLength':
      return max === null ? null : i18n.tf('shared.validation.exactLength', { length: max });
    case 'Length':
      if (min !== null && max !== null && max >= 0) {
        if (min === max) {
          return i18n.tf('shared.validation.exactLength', { length: max });
        }

        return min > 0 ? i18n.tf('shared.validation.lengthBetween', { min, max }) : i18n.tf('shared.validation.maxlength', { max });
      }

      return min === null ? null : i18n.tf('shared.validation.minlength', { min });
    case 'Email':
    case 'EmailAddress':
      return i18n.t('shared.validation.email');
    case 'GreaterThanOrEqual':
      return comparison === null ? null : i18n.tf('shared.validation.min', { min: comparison });
    case 'GreaterThan':
      return comparison === null ? null : i18n.tf('shared.validation.greaterThan', { value: comparison });
    case 'LessThanOrEqual':
      return comparison === null ? null : i18n.tf('shared.validation.max', { max: comparison });
    case 'LessThan':
      return comparison === null ? null : i18n.tf('shared.validation.lessThan', { value: comparison });
    case 'InclusiveBetween':
      return from === null || to === null ? null : i18n.tf('shared.validation.between', { from, to });
    case 'ExclusiveBetween':
      return from === null || to === null ? null : i18n.tf('shared.validation.betweenExclusive', { from, to });
    case 'RegularExpression':
    case 'Format':
      return i18n.t('shared.validation.pattern');
    case 'Enum':
    case 'StringEnum':
      return i18n.t('shared.validation.choice');
    case 'ScalePrecision':
    case 'PrecisionScale': {
      const precision = numberArgument(rule, 'precision');
      const scale = numberArgument(rule, 'scale');
      return precision === null || scale === null ? i18n.t('shared.validation.invalid') : i18n.tf('shared.validation.decimals', { precision, scale });
    }
    case 'Equal':
    case 'NotEqual':
    case 'Empty':
    case 'Null':
    case 'CreditCard':
    case 'Invalid':
      return i18n.t('shared.validation.invalid');
    default:
      return null;
  }
}

/** FluentValidation's own default messages quote the property name (`'Header Text Ar' must not be empty.`). */
const DEFAULT_VALIDATOR_MESSAGE = /'[A-Z][^']*'/;

/**
 * The localized, field-level text of a server validation failure (C-06). Known rules are translated with their
 * arguments in both languages. For a rule the client does not know (a custom `Must`), the server's own sentence is
 * kept in English when it was written for people (`WithMessage`); a validator default and every Arabic reading get the
 * generic "The value is not valid." Without rules (an older answer) the messages stay as the server sent them.
 */
export function fieldErrorText(i18n: I18nService, rules: readonly FieldRule[] | null | undefined, messages: readonly string[]): string {
  if (!rules || rules.length === 0) {
    return messages.join(' ');
  }

  const texts: string[] = [];
  rules.forEach((rule, index) => {
    const known = ruleText(i18n, rule);
    const message = messages[index] ?? '';
    const text = known ?? (i18n.lang() === 'en' && message !== '' && !DEFAULT_VALIDATOR_MESSAGE.test(message) ? message : i18n.t('shared.validation.invalid'));
    if (!texts.includes(text)) {
      texts.push(text);
    }
  });

  return texts.join(' ');
}
