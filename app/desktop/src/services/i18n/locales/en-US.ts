export default {
	components: {
		firstRun: {
			welcome: {
				title: "Welcome to Nori",
				subtitle: "A desktop companion for work, study, and slacking off. Get to know it first.",
				links: {
					steam: {
						label: "Steam Page",
						sub: "Wishlist to support the dev!",
					},
					noriOS: {
						label: "Nori Landing",
						sub: "Experience Nori's world on NoriOS",
					},
					qq: {
						label: "QQ Group",
						sub: "Click to copy group number: 1041616195",
					},
					bilibili: {
						label: "Bilibili",
						sub: "Follow updates and dev logs",
					}
				}
			},
			languageSelect: {
				title: "Select Language",
				langEmpty: "No languages available"
			},
			modelSelect: {
				title: "Select Model",
				sub: "Switchable later"
			},
			llmConnect: {
				error: {
					apiBaseUrl: "Please fill API base URL",
					apiKey: "Please fill API Key",
				},
				title: "Connect LLM",
				sub: "OpenAI-compatible endpoints only",
				apiBaseUrl: "API Base URL",
				apiKey: "API Key",
				model: "Model",
				modelEmpty: "No models available",
				getModel: "Fetch Models",
				getting: "Fetching...",
			},
			ready: {
				title: "Ready",
				desc: "Click \"Start\" to finish setup. Nori can't wait to meet you.",
				initDesc: "🐾 Setup takes only a few seconds"
			}
		},
		main: {
			live2d: {
				title: "Live2D",
				notReady: "Renderer not integrated · placeholder preview",
				hint: "Display module for AI to call. Renders the model once the SDK is plugged in",
				state: {
					unmounted: "Unmounted",
					loading: "Loading...",
					ready: "Ready",
					missing: "Model not installed",
					error: "Error",
				}
			},
			settings: {
				model: {
					title: "Model",
					sub: "Switch pet skin. Uninstalled models fall back to placeholder, no error",
					notInstalled: "Not installed",
					installed: "Installed",
					current: "Current",
				},
				language: {
					title: "Language",
					sub: "UI and conversation language",
					current: "Current",
				}
			}
		}
	},
	views: {
		firstRun: {
			back: "Back",
			next: "Next",
			start: "Start"
		},
		main: {
			title: "Nori",
			collapse: "Collapse navigation",
			expand: "Expand navigation",
			empty: "Pick an item from the left to start",
			close: "Close",
			minimize: "Minimize",
			nav: {
				live2d: "Live2D",
				settings: "Settings",
			}
		}
	},
	log: {
		pet: {
			mounted: "Pet window mounted, window: {label}, model: {model}",
			unmounted: "Pet window unmounted",
		},
		main: {
			mounted: "Main interface mounted, window: {label}",
			moduleSwitch: "Module switch: {id}",
			navCollapse: "Navigation collapsed",
			navExpand: "Navigation expanded",
		},
		canvas: {
			loadModel: "Loading model: {id}",
			mountComplete: "Mount complete, state: {state}",
			unmountComplete: "Unmount complete",
		},
		l2d: {
			rendererRegistered: "Renderer factory registered ({name})",
			canvasMounted: "Mount canvas",
			rendererNotInjected: "Renderer not injected, running in placeholder mode (call registerRenderer to enable)",
			canvasUnmounted: "Unmount canvas",
			rendererUnmountFailed: "Renderer unmount failed: {error}",
			backendListFailed: "Failed to fetch backend model list: {error}",
			listModels: "Listed models: {list}",
			loadModelRequest: "Request to load model: {id}",
			resolvePathFailed: "Failed to resolve model path: {error}",
			modelNotInstalled: "Model not installed, entering placeholder state: {id}",
			rendererNotReadyLoad: "Renderer not ready, cached model path (will load when renderer is plugged in): {id}",
			modelLoadComplete: "Model loaded: {id}",
			modelLoadFailed: "Model load failed: {error}",
			unloadNoModel: "Unload model: no model loaded, skipping",
			unloadModel: "Unload model: {model}",
			unloadModelFailed: "Unload model failed: {error}",
			reloadNoModel: "Reload: no model loaded",
			reloadModel: "Reloading model: {model}",
			reloadFailed: "Reload failed: {error}",
			playMotion: "Play motion: group={group} index={index}",
			playMotionRandom: "Play motion: group={group} (random)",
			playMotionFailed: "Play motion failed: {error}",
			stopMotion: "Stop motion",
			stopMotionFailed: "Stop motion failed: {error}",
			setExpression: "Set expression: {name}",
			setExpressionFailed: "Set expression failed: {error}",
			clearExpression: "Clear expression",
			clearExpressionFailed: "Clear expression failed: {error}",
			setEmotion: "Set emotion: {emotion} intensity={intensity}",
			clearEmotion: "Clear emotion → neutral",
			decayEmotion: "Emotion decayed → {intensity}",
			setParameter: "Set parameter: {id}={value}",
			setParameterFailed: "Set parameter failed: {error}",
			resetParameters: "Reset parameters",
			resetParametersFailed: "Reset parameters failed: {error}",
			startIdle: "Start Idle",
			stopIdle: "Stop Idle",
			enableMouseFollow: "Enable mouse follow",
			disableMouseFollow: "Disable mouse follow",
			resize: "Resize: {width}x{height}",
			setZoom: "Zoom: {scale}",
			setAnchor: "Anchor: ({x}, {y})",
			setAutoBlink: "Auto blink: {enabled}",
			setAutoBreath: "Auto breath: {enabled}",
			setPhysics: "Physics: {enabled}",
			setFps: "FPS: {fps}",
			setLipSync: "Lip sync: {value}",
			destroy: "Destroy controller",
			destroyFailed: "Destroy renderer failed: {error}",
			rendererMounted: "Renderer mounted: {id}",
			rendererMountFailed: "Renderer mount failed: {error}",
			applyCachedFailed: "Apply cached settings failed: {error}",
			assureRendererFailed: "{caller}: renderer not ready, placeholder return (call registerRenderer to enable)",
			stateChange: "State: {prev} → {next}",
			eventListenerError: "Event {event} listener error: {error}",
			setEyeOpen: "Set eye open: {open}",
			setBlinkInterval: "Set blink interval: {seconds}s",
			setHeadPose: "Set head pose: ({x}, {y}, {z})",
			setBodyPose: "Set body pose: ({x}, {y}, {z})",
			setMouthOpen: "Set mouth open: {open}",
			setEyebrowState: "Set eyebrow: left={left} right={right}",
			setHandGesture: "Set hand gesture: {name}",
			getPartIds: "Get part IDs",
			setPartOpacity: "Set part opacity: {id}={opacity}",
			getPartOpacity: "Get part opacity: {id}",
			setGravity: "Set gravity: ({x}, {y})",
			setTimeScale: "Set time scale: {scale}",
			captureScreenshot: "Screenshot",
			setAccessoryVisible: "Set accessory visible: {name}={visible}",
			speak: "Speak: {text}",
			getControllerState: "Get controller state",
			playSequence: "Play sequence: {count} actions",
		},
		model: {
			switch: "Switch model: {id}",
		},
		settings: {
			modelListFailed: "Model list load failed, falling back to catalog placeholder",
		},
		language: {
			switch: "Switch language: {lang}",
			list: "Available languages: {list}",
		},
		app: {
			windowNavigated: "Window {label} mounted, navigating to {target}",
		},
		icon: {
			unsupportedMode: "Icon {name} does not support {mode} mode",
			logWriteFailed: "Failed to write log: {error}",
		},
		store: {
			modelConfigReadFailed: "Failed to read model config: {error}",
			modelConfigSaveFailed: "Failed to save model config: {error}",
		},
		firstRun: {
			initComplete: "Initialization complete",
			finishFailed: "Failed to finish initialization: {error}",
			configSaved: "Saved config key {key} as: {value}",
			languageListFailed: "Failed to load language list: {error}",
			languageSwitchFailed: "Failed to switch language: {error}",
			llmConfigReadFailed: "Failed to read LLM config: {error}",
			llmConfigSaveFailed: "Failed to save LLM config: {error}",
			modelSaveFailed: "Failed to save model: {error}",
			modelFetchFailed: "Failed to fetch models: {error}",
			modelConfigReadFailed: "Failed to read model config: {error}",
			modelConfigSaveFailed: "Failed to save model config: {error}",
			qqCopySuccess: "Copied QQ group number {qq} successfully",
			qqCopyFailed: "Failed to copy QQ group number {qq}",
			l2dModuleEnter: "Entered Live2D module, current model: {model}",
		},
		i18n: {
			systemLanguageFailed: "Failed to get system language: {error}",
			languageConfigReadFailed: "Failed to read language config: {error}",
			languageConfigSaveFailed: "Failed to save language config: {error}",
			availableLanguagesFailed: "Failed to get available languages: {error}",
		},
	}
}
