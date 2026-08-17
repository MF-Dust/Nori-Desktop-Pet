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
			}
		}
	},
	views: {
		firstRun: {
			back: "上一步",
			next: "下一步"
		}
	}
}