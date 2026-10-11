import { userBranchesCell } from './users.page';

/** UAT R1-06: the users CSV names the assigned branches by code, as the doctors export does since B-09. */
describe('Users export: Branches cell', () => {
  it('writes the codes of the assigned branches', () => {
    expect(userBranchesCell({ branchCount: 2, branchCodes: ['MAIN', 'NORTH'] }, 'All')).toBe('MAIN; NORTH');
  });

  it('writes "All" for an unrestricted user', () => {
    expect(userBranchesCell({ branchCount: 0, branchCodes: [] }, 'All')).toBe('All');
  });

  it('falls back to the count when an older server sends no codes', () => {
    expect(userBranchesCell({ branchCount: 1 }, 'All')).toBe('1');
  });
});
