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
			// 以当前全量源码基线留出小幅波动空间，避免把未覆盖模块一次性变成硬阻塞。
			thresholds: {
				statements: 50,
				branches: 40,
				functions: 45,
				lines: 50,
			},
		},
	},
})
