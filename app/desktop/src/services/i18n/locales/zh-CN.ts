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
				initDesc: "🐾 初始化大约只需几秒钟"
			}
		},
		main: {
			live2d: {
				title: "Live2D",
				notReady: "渲染器未接入 · 占位预览",
				hint: "这是给 AI 调用的显示模块, 渲染 SDK 接入后将在此呈现模型",
				state: {
					unmounted: "未挂载",
					loading: "加载中...",
					ready: "就绪",
					missing: "模型未安装",
					error: "出错",
				}
			},
			settings: {
				model: {
					title: "模型",
					sub: "切换桌宠皮肤。未安装的模型选择后会进入占位, 不会报错",
					notInstalled: "未安装",
					installed: "已安装",
					current: "当前",
				},
				language: {
					title: "语言",
					sub: "界面与对话语言",
					current: "当前",
				}
			}
		},
		pet: {
			bubble: {
				default: "你好呀~有什么想聊的吗?",
				thinking: "...让我想想",
			},
			dialog: {
				placeholder: "输入消息...",
				send: "发送",
			},
			hint: "点击我对话",
		}
	},
		views: {
		firstRun: {
			back: "上一步",
			next: "下一步",
			start: "开始"
		},
		main: {
			title: "Nori",
			collapse: "收起导航",
			expand: "展开导航",
			empty: "从左侧选择一项开始",
			nav: {
				live2d: "模型展示",
				settings: "设置",
			}
		}
	},
	log: {
		pet: {
			mounted: "桌宠窗口挂载, 窗口: {label}, 模型: {model}",
			unmounted: "桌宠窗口卸载",
			bubbleShown: "显示聊天气泡: {message}",
			bubbleHidden: "隐藏聊天气泡",
			dialogShown: "显示对话框",
			dialogHidden: "隐藏对话框",
			messageSent: "发送消息: {text}",
			windowMinimized: "窗口最小化",
			windowClosed: "窗口关闭",
		},
		main: {
			mounted: "主界面挂载, 窗口: {label}",
			moduleSwitch: "切换模块: {id}",
			navCollapse: "导航收起",
			navExpand: "导航展开",
		},
		canvas: {
			loadModel: "加载模型: {id}",
			mountComplete: "挂载完成, 状态: {state}",
			unmountComplete: "卸载完成",
		},
		l2d: {
			rendererRegistered: "渲染器工厂已注册 ({name})",
			canvasMounted: "挂载 canvas",
			rendererNotInjected: "渲染器未注入, 以占位模式运行 (调用 registerRenderer 接通真实渲染)",
			canvasUnmounted: "卸载 canvas",
			rendererUnmountFailed: "卸载渲染器失败: {error}",
			backendListFailed: "读取后端模型列表失败: {error}",
			listModels: "列出模型: {list}",
			loadModelRequest: "请求加载模型: {id}",
			resolvePathFailed: "解析模型路径失败: {error}",
			modelNotInstalled: "模型未安装, 进入占位状态: {id}",
			rendererNotReadyLoad: "渲染器未就绪, 暂存模型路径 (待渲染器接入后加载): {id}",
			modelLoadComplete: "模型加载完成: {id}",
			modelLoadFailed: "模型加载失败: {error}",
			unloadNoModel: "卸载模型: 无已加载模型, 跳过",
			unloadModel: "卸载模型: {model}",
			unloadModelFailed: "卸载模型失败: {error}",
			reloadNoModel: "重新加载: 无已加载模型",
			reloadModel: "重新加载模型: {model}",
			reloadFailed: "重新加载失败: {error}",
			playMotion: "播放动作: group={group} index={index}",
			playMotionRandom: "播放动作: group={group} (随机)",
			playMotionFailed: "播放动作失败: {error}",
			stopMotion: "停止动作",
			stopMotionFailed: "停止动作失败: {error}",
			setExpression: "设置表情: {name}",
			setExpressionFailed: "设置表情失败: {error}",
			clearExpression: "清除表情",
			clearExpressionFailed: "清除表情失败: {error}",
			setEmotion: "设置情绪: {emotion} intensity={intensity}",
			clearEmotion: "清除情绪 → neutral",
			decayEmotion: "情绪衰减 → {intensity}",
			setParameter: "设置参数: {id}={value}",
			setParameterFailed: "设置参数失败: {error}",
			resetParameters: "重置参数",
			resetParametersFailed: "重置参数失败: {error}",
			startIdle: "启动 Idle",
			stopIdle: "停止 Idle",
			enableMouseFollow: "启用鼠标跟随",
			disableMouseFollow: "禁用鼠标跟随",
			resize: "尺寸变更: {width}x{height}",
			setZoom: "缩放: {scale}",
			setAnchor: "锚点: ({x}, {y})",
			setAutoBlink: "自动眨眼: {enabled}",
			setAutoBreath: "自动呼吸: {enabled}",
			setPhysics: "物理: {enabled}",
			setFps: "帧率: {fps}",
			setLipSync: "口型: {value}",
			destroy: "销毁控制器",
			destroyFailed: "销毁渲染器失败: {error}",
			rendererMounted: "渲染器已挂载: {id}",
			rendererMountFailed: "渲染器挂载失败: {error}",
			applyCachedFailed: "应用缓存设置失败: {error}",
			assureRendererFailed: "{caller}: 渲染器未就绪, 占位返回 (调用 registerRenderer 接通真实渲染)",
			stateChange: "状态: {prev} → {next}",
			eventListenerError: "事件 {event} 监听器异常: {error}",
			setEyeOpen: "设置眼睛开合: {open}",
			setBlinkInterval: "设置眨眼间隔: {seconds}s",
			setHeadPose: "设置头部姿态: ({x}, {y}, {z})",
			setBodyPose: "设置身体姿态: ({x}, {y}, {z})",
			setMouthOpen: "设置嘴巴开合: {open}",
			setEyebrowState: "设置眉毛: left={left} right={right}",
			setHandGesture: "设置手势: {name}",
			getPartIds: "获取部件列表",
			setPartOpacity: "设置部件透明度: {id}={opacity}",
			getPartOpacity: "获取部件透明度: {id}",
			setGravity: "设置重力: ({x}, {y})",
			setTimeScale: "设置时间缩放: {scale}",
			captureScreenshot: "截屏",
			setAccessoryVisible: "设置配件可见性: {name}={visible}",
			speak: "语音: {text}",
			getControllerState: "获取控制器状态",
			playSequence: "播放动作序列: {count}个动作",
		},
		model: {
			switch: "切换模型: {id}",
		},
		settings: {
			modelListFailed: "模型列表加载失败, 回退到目录占位",
		},
		language: {
			switch: "切换语言: {lang}",
			list: "可用语言列表: {list}",
		},
		app: {
			windowNavigated: "窗口 {label} 已挂载, 跳转到 {target}",
		},
		icon: {
			unsupportedMode: "图标 {name} 不支持 {mode} 模式",
			logWriteFailed: "写入日志失败: {error}",
		},
		store: {
			modelConfigReadFailed: "读取模型配置失败: {error}",
			modelConfigSaveFailed: "保存模型配置失败: {error}",
		},
		firstRun: {
			initComplete: "初始化完成",
			finishFailed: "完成初始化失败: {error}",
			configSaved: "保存配置键 {key} 为: {value}",
			languageListFailed: "加载语言列表失败: {error}",
			languageSwitchFailed: "切换语言失败: {error}",
			llmConfigReadFailed: "读取 LLM 配置失败: {error}",
			llmConfigSaveFailed: "保存 LLM 配置失败: {error}",
			modelSaveFailed: "保存模型失败: {error}",
			modelFetchFailed: "获取模型失败: {error}",
			modelConfigReadFailed: "读取模型配置失败: {error}",
			modelConfigSaveFailed: "保存模型配置失败: {error}",
			qqCopySuccess: "复制 QQ 群号 {qq} 成功",
			qqCopyFailed: "复制 QQ 群号 {qq} 失败",
			l2dModuleEnter: "进入 Live2D 模块, 当前模型: {model}",
		},
		i18n: {
			systemLanguageFailed: "获取系统语言失败: {error}",
			languageConfigReadFailed: "读取语言配置失败: {error}",
			languageConfigSaveFailed: "保存语言配置失败: {error}",
			availableLanguagesFailed: "获取可用语言列表失败: {error}",
		},
	}
}
