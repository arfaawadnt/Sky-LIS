import { csvCell, csvText } from './csv';

describe('csv (CP-10 export)', () => {
  it('quotes RFC 4180 style and leaves plain cells alone', () => {
    expect(csvCell('CBC')).toBe('CBC');
    expect(csvCell('Adel, Mona')).toBe('"Adel, Mona"');
    expect(csvCell('say "hi"')).toBe('"say ""hi"""');
    expect(csvCell(12.5)).toBe('12.5');
    expect(csvCell(true)).toBe('true');
    expect(csvCell(null)).toBe('');
    expect(csvCell(undefined)).toBe('');
  });

  it('neutralises formula injection', () => {
    expect(csvCell('=SUM(A1)')).toBe("'=SUM(A1)");
    expect(csvCell('+1')).toBe("'+1");
    expect(csvCell('-x')).toBe("'-x");
    expect(csvCell('@cmd')).toBe("'@cmd");
  });

  it('joins headers and rows with CRLF', () => {
    expect(csvText(['a', 'b'], [['1', 'x,y'], [2, null]])).toBe('a,b\r\n1,"x,y"\r\n2,');
  });
});
