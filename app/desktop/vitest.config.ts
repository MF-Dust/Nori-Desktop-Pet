import {defineConfig} from "vitest/config"
import vue from "@vitejs/plugin-vue"

export default defineConfig({
	plugins: [vue()],
	test: {
		include: ["tests/**/*.test.ts"],
		environment: "jsdom",
		setupFiles: ["tests/setup.ts"],
		coverage: {
			provider: "v8",
			reporter: ["text", "json-summary", "lcov"],
			reportsDirectory: "coverage",
			include: ["src/**/*.ts", "src/**/*.vue"],
			exclude: ["src/**/*.d.ts"],
			// 本地全量基线 (44 个测试文件 / 228 个用例): statements 55.81%、branches 45.10%、functions 48.46%、lines 58.50%。阈值保留约 3 个百分点以上的波动空间。
			thresholds: {
				statements: 50,
				branches: 40,
				functions: 45,
				lines: 50,
			},
		},
	},
})
