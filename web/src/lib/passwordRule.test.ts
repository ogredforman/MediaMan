import { describe, expect, it } from 'vitest';
import { passwordHint, passwordProblem } from './passwordRule';

const required = { requirePasswords: true, strongPasswords: false };
const strong = { requirePasswords: true, strongPasswords: true };
const optional = { requirePasswords: false, strongPasswords: false };

describe('password rule', () => {
  it('needs 8 characters by default, counting what people see', () => {
    expect(passwordProblem(required, 'seven77')).toBe('Use a password of at least 8 characters.');
    expect(passwordProblem(required, 'eight888')).toBeUndefined();
    // Seven emoji are 14 UTF-16 units but 7 characters.
    expect(passwordProblem(required, '😀'.repeat(7))).toBe('Use a password of at least 8 characters.');
    expect(passwordProblem(required, '')).toBe('Enter a password.');
    expect(passwordProblem(required, 'a'.repeat(1025))).toBe('Use a password of at most 1024 characters.');
  });

  it('needs 12 characters mixing 3 kinds when strong', () => {
    expect(passwordProblem(strong, 'Short1!')).toMatch(/^Use a password of at least 12 characters, mixing at least 3 of/);
    expect(passwordProblem(strong, 'alllowercaseletters')).toBe(
      'Mix at least 3 of: uppercase letters, lowercase letters, digits and symbols.',
    );
    expect(passwordProblem(strong, 'lowercase and spaces')).toBe(
      'Mix at least 3 of: uppercase letters, lowercase letters, digits and symbols.',
    );
    expect(passwordProblem(strong, 'Correct horse battery')).toBeUndefined();
    expect(passwordProblem(strong, 'correcthorse42!')).toBeUndefined();
  });

  it('lets the password be left out while passwords are off, but checks one that is set', () => {
    expect(passwordProblem(optional, '')).toBeUndefined();
    expect(passwordProblem(optional, 'short')).toBe('Use a password of at least 8 characters.');
    expect(passwordProblem({ ...optional, strongPasswords: true }, 'alllowercaseletters')).toMatch(/^Mix/);
  });

  it('states the rule in force', () => {
    expect(passwordHint(required)).toBe('At least 8 characters.');
    expect(passwordHint(strong)).toBe(
      'At least 12 characters, mixing at least 3 of: uppercase letters, lowercase letters, digits and symbols.',
    );
    expect(passwordHint(optional)).toBe('Optional. If you set one: at least 8 characters.');
  });
});
