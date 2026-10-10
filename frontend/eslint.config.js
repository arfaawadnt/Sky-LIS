// @ts-check
const eslint = require('@eslint/js');
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

// Rules of 01 §6 and pitfalls P-60, P-63, P-65: no any, no $any(), no subscribe() in components, no imports from
// features into core/shared. The cross-feature rule needs path context and lives in scripts/check-boundaries.mjs.
module.exports = defineConfig([
  {
    files: ['**/*.ts'],
    extends: [eslint.configs.recommended, tseslint.configs.recommended, tseslint.configs.stylistic, angular.configs.tsRecommended],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': ['error', { type: 'attribute', prefix: 'app', style: 'camelCase' }],
      '@angular-eslint/component-selector': ['error', { type: 'element', prefix: 'app', style: 'kebab-case' }],
      '@angular-eslint/prefer-on-push-component-change-detection': 'error',
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/no-unsafe-argument': 'off',
      '@typescript-eslint/consistent-type-definitions': ['error', 'interface'],
      'no-restricted-globals': ['error', { name: 'localStorage', message: 'Tokens and session state never touch web storage (D-01, P-61).' }, { name: 'sessionStorage', message: 'Tokens and session state never touch web storage (D-01, P-61).' }],
    },
  },
  {
    files: ['src/app/core/**/*.ts', 'src/app/shared/**/*.ts'],
    rules: {
      'no-restricted-imports': ['error', { patterns: [{ group: ['**/features/**'], message: 'core/ and shared/ never import features/ (01 §6.2).' }] }],
    },
  },
  {
    files: ['src/app/**/*.component.ts', 'src/app/**/*.page.ts', 'src/app/app.ts'],
    rules: {
      'no-restricted-syntax': ['error', { selector: "CallExpression[callee.property.name='subscribe']", message: 'Components read signals and resources; no subscribe() in components (P-63).' }],
    },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {
      '@angular-eslint/template/no-any': 'error',
    },
  },
]);
