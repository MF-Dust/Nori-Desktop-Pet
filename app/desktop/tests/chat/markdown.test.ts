// @vitest-environment jsdom
import {describe, expect, it} from "vitest"
import {renderMarkdown} from "../../src/services/chat/markdown"

describe("聊天 Markdown 渲染与消毒", () => {
	it("常规排版正常渲染", () => {
		const HTML = renderMarkdown("**粗体** 与 `代码`\n\n- 项目一\n- 项目二")
		expect(HTML).toContain("<strong>粗体</strong>")
		expect(HTML).toContain("<code>代码</code>")
		expect(HTML).toContain("<li>项目一</li>")
	})

	it("代码块保留结构", () => {
		const HTML = renderMarkdown("```ts\nconst a = 1\n```")
		expect(HTML).toContain("<pre>")
		expect(HTML).toContain("const a = 1")
	})

	it("脚本与事件属性不会进入 DOM", () => {
		const HTML = renderMarkdown('<script>alert(1)<\/script>\n\n<img src=x onerror="alert(1)">\n\n<iframe src="https://evil.test"></iframe>')
		const DOC = new DOMParser().parseFromString(`<div>${HTML}</div>`, "text/html")

		// 原始标签被转义成文本, 不会生成可执行元素
		expect(DOC.querySelector("script")).toBeNull()
		expect(DOC.querySelector("iframe")).toBeNull()
		expect(DOC.querySelector("img")).toBeNull()

		// 任何元素都不带 on* 事件属性
		for (const element of DOC.querySelectorAll("*")) {
			for (const attribute of element.attributes) {
				expect(attribute.name.startsWith("on")).toBe(false)
			}
		}
	})

	it("javascript: 伪协议不会渲染成可点链接", () => {
		const HTML = renderMarkdown("[点我](javascript:alert(1))")
		const DOC = new DOMParser().parseFromString(`<div>${HTML}</div>`, "text/html")
		// 链接直接被丢弃 (残留的只是纯文本)
		expect(DOC.querySelector("a")).toBeNull()
	})

	it("外链带 data-external 与 rel, 交给宿主打开", () => {
		const HTML = renderMarkdown("看这里 https://example.com/docs")
		expect(HTML).toContain('data-external="1"')
		expect(HTML).toContain('rel="noopener noreferrer"')
	})

	it("行内 HTML 被转义而不是执行", () => {
		const HTML = renderMarkdown("<b>加粗</b>")
		expect(HTML).toContain("&lt;b&gt;")
	})
})
