/**
 * Code 128 (ISO/IEC 15417) symbol encoding, code sets B and C with automatic switching: digit runs of four or
 * more (or an even-length run that ends the text) go to set C, everything else to set B. Produces the symbol
 * values (for checks) and the bar/space module widths (for drawing). Written here so labels need no library.
 */

/** Module widths of the 107 symbols: six elements (bar, space, …) each, the stop symbol seven. */
const PATTERNS: readonly string[] = [
  '212222', '222122', '222221', '121223', '121322', '131222', '122213', '122312', '132212', '221213',
  '221312', '231212', '112232', '122132', '122231', '113222', '123122', '123221', '223211', '221132',
  '221231', '213212', '223112', '312131', '311222', '321122', '321221', '312212', '322112', '322211',
  '212123', '212321', '232121', '111323', '131123', '131321', '112313', '132113', '132311', '211313',
  '231113', '231311', '112133', '112331', '132131', '113123', '113321', '133121', '313121', '211331',
  '231131', '213113', '213311', '213131', '311123', '311321', '331121', '312113', '312311', '332111',
  '314111', '221411', '431111', '111224', '111422', '121124', '121421', '141122', '141221', '112214',
  '112412', '122114', '122411', '142112', '142211', '241211', '221114', '413111', '241112', '134111',
  '111242', '121142', '121241', '114212', '124112', '124211', '411212', '421112', '421211', '212141',
  '214121', '412121', '111143', '111341', '131141', '114113', '114311', '411113', '411311', '113141',
  '114131', '311141', '411131', '211412', '211214', '211232', '2331112',
];

export const CODE128 = {
  codeC: 99,
  codeB: 100,
  startB: 104,
  startC: 105,
  stop: 106,
  /** Modules of one symbol; the stop symbol has 13. */
  symbolModules: 11,
  stopModules: 13,
  /** Quiet zone on each side, in modules. */
  quietZone: 10,
} as const;

export interface Code128Bar {
  /** Offset in modules from the start of the symbol (quiet zone excluded). */
  readonly x: number;
  readonly width: number;
}

export interface Code128Symbol {
  /** Symbol values from the start code to the stop code, checksum included. */
  readonly values: readonly number[];
  /** Concatenated element widths (bar, space, bar, …). */
  readonly widths: string;
  /** Total width in modules, quiet zones excluded. */
  readonly modules: number;
  readonly bars: readonly Code128Bar[];
}

function isDigit(code: number): boolean {
  return code >= 48 && code <= 57;
}

function digitRun(text: string, from: number): number {
  let count = 0;
  while (from + count < text.length && isDigit(text.charCodeAt(from + count))) {
    count++;
  }

  return count;
}

/** The symbol values of `text` (start, data with set switches, checksum, stop). Throws on characters outside 32…126. */
export function code128Values(text: string): readonly number[] {
  if (text.length === 0) {
    throw new Error('Code 128: empty text.');
  }

  for (let i = 0; i < text.length; i++) {
    const code = text.charCodeAt(i);
    if (code < 32 || code > 126) {
      throw new Error(`Code 128: character ${JSON.stringify(text[i])} at ${i} is outside code set B.`);
    }
  }

  const values: number[] = [];
  const leadingDigits = digitRun(text, 0);
  let setC = leadingDigits >= 4 || (leadingDigits === text.length && leadingDigits >= 2 && leadingDigits % 2 === 0);
  values.push(setC ? CODE128.startC : CODE128.startB);

  let i = 0;
  while (i < text.length) {
    const run = digitRun(text, i);
    if (setC) {
      if (run >= 2) {
        values.push(Number(text.slice(i, i + 2)));
        i += 2;
      } else {
        values.push(CODE128.codeB);
        setC = false;
      }

      continue;
    }

    if (run >= 4 || (run >= 2 && i + run === text.length)) {
      if (run % 2 === 1) {
        values.push(text.charCodeAt(i) - 32);
        i++;
      }

      values.push(CODE128.codeC);
      setC = true;
      continue;
    }

    values.push(text.charCodeAt(i) - 32);
    i++;
  }

  let checksum = values[0] ?? 0;
  for (let k = 1; k < values.length; k++) {
    checksum += k * (values[k] ?? 0);
  }

  values.push(checksum % 103, CODE128.stop);
  return values;
}

/** The full symbol: values, element widths and bar geometry in modules. */
export function encodeCode128(text: string): Code128Symbol {
  const values = code128Values(text);
  const widths = values.map((value) => PATTERNS[value] ?? '').join('');
  const bars: Code128Bar[] = [];
  let x = 0;
  for (let i = 0; i < widths.length; i++) {
    const width = Number(widths[i]);
    if (i % 2 === 0) {
      bars.push({ x, width });
    }

    x += width;
  }

  return { values, widths, modules: x, bars };
}
