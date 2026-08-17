export default {
	components: {
		firstRun: {
			welcome: {
				title: "欢迎来到 Nori",
				subtitle: "一只会陪你上班、学习、摸鱼的桌面伙伴。先认识一下它吧。",
				links: {
					steam: {
						label: "Steam 页面",
						sub: "加入愿望单支持老大!",
					},
					noriOS: {
						label: "Nori 先导页",
						sub: "在 NoriOS 上体验 Nori 的世界",
					},
					qq: {
						label: "QQ 交流群",
						sub: "点击复制群号: 1041616195",
					},
					bilibili: {
						label: "Bilibili",
						sub: "关注老大的更新和开发日志",
					}
				}
			},
			languageSelect: {
				title: "选择语言",
				langEmpty: "暂无可用语言"
			},
			modelSelect: {
				title: "选择模型",
				sub: "可后期切换"
			},
			llmConnect: {
				error: {
					apiBaseUrl: "请填写 API 地址",
					apiKey: "请填写 API Key",
				},
				title: "连接 LLM 模型",
				sub: "仅支持 OpenAI 协议接口",
				apiBaseUrl: "API 地址",
				apiKey: "API Key",
				model: "模型",
				modelEmpty: "暂无可用模型",
				getModel: "获取模型",
				getting: "获取中...",
			},
			ready: {
				title: "准备就绪",
				desc: "点击「开始」完成初始化，Nori 期待与你见面。",
				initDesc: "🐾 初始化大约只需1分钟"
			}
		}
	},
	views: {
		firstRun: {
			back: "上一步",
			next: "下一步",
			start: "开始"
		}
	}
}