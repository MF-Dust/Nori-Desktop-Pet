import eslint from "@eslint/js"
import globals from "globals"
import tseslint from "typescript-eslint"
import vue from "eslint-plugin-vue"

export default tseslint.config(
	{
		ignores: [
			"coverage/**",
			"dist/**",
			"node_modules/**",
		],
		linterOptions: {
			reportUnusedDisableDirectives: "off",
		},
	},
	eslint.configs.recommended,
	...tseslint.configs.recommended,
	...vue.configs["flat/essential"],
	{
		files: ["src/**/*.{ts,vue}"],
		languageOptions: {
			globals: globals.browser,
			parserOptions: {
				projectService: true,
				tsconfigRootDir: import.meta.dirname,
				extraFileExtensions: [".vue"],
			},
		},
		rules: {
			// 只守住正确性，不启用会改写现有命名与缩进约定的风格规则。
			"no-constant-condition": "error",
			"no-duplicate-case": "error",
			"no-empty": ["error", {"allowEmptyCatch": true}],
			"no-self-assign": "error",
			"no-unreachable": "error",
			"no-unsafe-finally": "error",
			"no-unreachable-loop": "error",
			"prefer-const": "off",
			"@typescript-eslint/await-thenable": "error",
			"@typescript-eslint/no-explicit-any": "off",
			"@typescript-eslint/no-empty-object-type": "off",
			"@typescript-eslint/no-floating-promises": ["error", {"ignoreIIFE": true}],
			"@typescript-eslint/no-misused-promises": ["error", {"checksVoidReturn": {"arguments": false, "attributes": false}}],
			"@typescript-eslint/no-unused-vars": ["error", {"argsIgnorePattern": "^_", "varsIgnorePattern": "^_"}],
		},
	},
	{
		files: ["src/services/live2d/plugins/beat-sync.ts"],
		rules: {
			// 该已有赋值用于触发 Vue ref setter，保留现有运行时语义。
			"no-self-assign": "off",
		},
	},
	{
		files: ["src/**/*.vue"],
		languageOptions: {
			parserOptions: {
				parser: tseslint.parser,
			},
		},
		rules: {
			// essential 配置负责模板语法；这里补充最容易造成运行时错误的 Vue 规则。
			"vue/multi-word-component-names": "off",
			"vue/no-async-in-computed-properties": "error",
			"vue/no-ref-as-operand": "error",
			"vue/no-side-effects-in-computed-properties": "error",
			"vue/no-watch-after-await": "error",
		},
	},
)
