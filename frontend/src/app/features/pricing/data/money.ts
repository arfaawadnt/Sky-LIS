/** Money helpers of the pricing screens, matching the server's rules (PriceMath): two decimals, half away from zero. */

/** Rounds to two decimals, half away from zero (as the server rounds shares). */
export function round2(value: number): number {
  const sign = value < 0 ? -1 : 1;
  return (sign * Math.round(Math.abs(value) * 100 + Number.EPSILON * 100)) / 100;
}

/** Rounds to the nearest multiple of `step`, half away from zero (copy list, intake Q13). */
export function roundToStep(value: number, step: number): number {
  if (!(step > 0)) {
    return round2(value);
  }

  const sign = value < 0 ? -1 : 1;
  const units = Math.round(Math.abs(value) / step + 1e-9);
  return round2(sign * units * step);
}

/** True when the number has at most `scale` decimals (the server refuses more). */
export function hasScale(value: number, scale: number): boolean {
  if (!Number.isFinite(value)) {
    return false;
  }

  const factor = 10 ** scale;
  return Math.abs(Math.round(value * factor) - value * factor) < 1e-6;
}

/** `1234.5` → `1,234.50` (en) or `١٬٢٣٤٫٥٠` (ar): always two decimals. */
export function formatAmount(value: number, lang: string): string {
  return new Intl.NumberFormat(lang === 'ar' ? 'ar-EG' : 'en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);
}

/** Factors are shown with up to four decimals. */
export function formatFactor(value: number, lang: string): string {
  return new Intl.NumberFormat(lang === 'ar' ? 'ar-EG' : 'en-US', { minimumFractionDigits: 0, maximumFractionDigits: 4 }).format(value);
}

/** The server's bulk adjustment (PriceMath.Adjust, C21): + amount or × (1 + percent / 100), rounded, a negative result held at 0. */
export function adjustPrice(price: number, mode: 'Amount' | 'Percent', value: number, rounding: 'None' | 'Whole' | 'Nearest5' | 'Nearest10'): { price: number; clamped: boolean } {
  const raw = mode === 'Amount' ? price + value : price * (1 + value / 100);
  const step = rounding === 'Whole' ? 1 : rounding === 'Nearest5' ? 5 : rounding === 'Nearest10' ? 10 : 0;
  const rounded = step > 0 ? roundToStep(raw, step) : round2(raw);
  return rounded < 0 ? { price: 0, clamped: true } : { price: rounded, clamped: false };
}
