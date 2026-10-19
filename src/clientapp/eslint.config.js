import { globalIgnores } from 'eslint/config'
import { defineConfigWithVueTs, vueTsConfigs } from '@vue/eslint-config-typescript'
import globals from 'globals'
import pluginVue from 'eslint-plugin-vue'

// TypeScript-aware: the app is written in .ts and <script lang="ts">, which the plain JS parser
// could not read — every such file was a parsing error, so lint checked next to nothing.
export default defineConfigWithVueTs(
  {
    name: 'app/files-to-lint',
    files: ['**/*.{js,mjs,ts,mts,vue}'],
  },

  // generated/ is written by TsGen and checked by TsContractTests, not by hand.
  globalIgnores(['**/dist/**', '**/dist-ssr/**', '**/coverage/**', 'archive/**', 'src/generated/**']),

  {
    languageOptions: {
      globals: {
        ...globals.browser,
      },
    },
  },

  pluginVue.configs['flat/essential'],
  vueTsConfigs.recommended,

  // PrimeVue's own component names (Button, Select, Dialog…), registered globally — not ours to rename.
  {
    files: ['src/main.js'],
    rules: {
      'vue/multi-word-component-names': 'off',
      'vue/no-reserved-component-names': 'off',
    },
  },

  // The stock Vue SFC shim: DefineComponent<{}, {}, any> is its documented shape.
  {
    files: ['src/shims-vue.d.ts'],
    rules: {
      '@typescript-eslint/no-explicit-any': 'off',
      '@typescript-eslint/no-empty-object-type': 'off',
    },
  },
)
