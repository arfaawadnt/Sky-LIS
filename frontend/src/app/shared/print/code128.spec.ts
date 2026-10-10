import { CODE128, code128Values, encodeCode128 } from './code128';

describe('Code 128 encoding (shared/print)', () => {
  it('encodes a single set-B character with the documented checksum', () => {
    // Start B (104) + 1 × 'A' (33) = 137 → 137 mod 103 = 34
    expect(code128Values('A')).toEqual([CODE128.startB, 33, 34, CODE128.stop]);
  });

  it('encodes an even run of digits in set C', () => {
    // Start C (105) + 1×12 + 2×34 + 3×56 = 353 → 353 mod 103 = 44
    expect(code128Values('123456')).toEqual([CODE128.startC, 12, 34, 56, 44, CODE128.stop]);
  });

  it('computes the checksum of the reference word "Wikipedia" (88)', () => {
    const values = code128Values('Wikipedia');
    expect(values[0]).toBe(CODE128.startB);
    expect(values.slice(1, -2)).toEqual(['W', 'i', 'k', 'i', 'p', 'e', 'd', 'i', 'a'].map((c) => c.charCodeAt(0) - 32));
    expect(values[values.length - 2]).toBe(88);
    expect(values[values.length - 1]).toBe(CODE128.stop);
  });

  it('switches to set C for a run of four or more digits and back to B afterwards', () => {
    // "AB1234C": B, A, B, Code C, 12, 34, Code B, C
    const values = code128Values('AB1234C');
    expect(values.slice(0, -2)).toEqual([CODE128.startB, 33, 34, CODE128.codeC, 12, 34, CODE128.codeB, 35]);
  });

  it('keeps an odd digit in set B before switching to C', () => {
    // "X12345": B, X, '1' in B, Code C, 23, 45
    const values = code128Values('X12345');
    expect(values.slice(0, -2)).toEqual([CODE128.startB, 56, 17, CODE128.codeC, 23, 45]);
  });

  it('starts in set C when the text begins with four digits', () => {
    const values = code128Values('2024ACC');
    expect(values[0]).toBe(CODE128.startC);
    expect(values.slice(1, 4)).toEqual([20, 24, CODE128.codeB]);
  });

  it('produces 11 modules per symbol plus the 13-module stop, and bars at alternating positions', () => {
    const symbol = encodeCode128('SMP-2026-00042');
    const symbols = symbol.values.length;
    expect(symbol.widths.length).toBe(6 * (symbols - 1) + 7);
    expect(symbol.modules).toBe(CODE128.symbolModules * (symbols - 1) + CODE128.stopModules);
    expect(symbol.bars[0]).toEqual({ x: 0, width: 2 });
    const last = symbol.bars[symbol.bars.length - 1];
    expect(last).toBeDefined();
    expect((last?.x ?? 0) + (last?.width ?? 0)).toBe(symbol.modules);
  });

  it('refuses characters outside code set B and empty text', () => {
    expect(() => code128Values('')).toThrow();
    expect(() => code128Values('عينة')).toThrow();
    expect(() => code128Values('a\tb')).toThrow();
  });
});
