import antfu from '@antfu/eslint-config';

const topIdAndName = {
  groups: ['top', 'member', 'multiline-member', 'unknown', 'method', 'multiline-method'],
  partitionByComment: true,
  customGroups: [
    {
      elementNamePattern: '^(?:id|name)$',
      groupName: 'top',
      selector: 'property' as const,
    },
  ],
};

export default antfu(
  {
    ignores: ['dist/**', 'coverage/**', 'docs/**'],
    markdown: false,
    type: 'app',
    typescript: { tsconfigPath: 'tsconfig.json' },
    stylistic: {
      indent: 2,
      quotes: 'single',
      semi: true,
    },
  },
  {
    rules: {
      'perfectionist/sort-array-includes': ['warn', { partitionByComment: true }],
      'perfectionist/sort-imports': ['error', { partitionByComment: true }],
      'perfectionist/sort-interfaces': ['error', { ...topIdAndName, type: 'natural' }],
    },
  },
);
