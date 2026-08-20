export default {
	views: {
		pet: {
			hint: "Click the pet to chat",
			contextMenu: {
				openMain: "Open Main Window",
				playMotion: "Play Motion",
				resetPos: "Reset Position",
				hidePet: "Hide Pet",
				exitApp: "Exit Nori",
			},
		},
		main: {
			nav: {
				home: "Home",
				talk: "Chat",
				model: "Model",
				settings: "Settings",
				about: "About",
			},
			home: {
				petStatusOnline: "Pet Active",
				petStatusOffline: "Pet Standby",
				petStatusDescOnline: "Nori is currently active on your desktop",
				petStatusDescOffline: "Click summon to bring Nori to your desktop",
				summonPet: "Summon to Desktop",
				hidePet: "Hide Pet",
				quickMotion: "Say Hello",
				quickMotionDone: "Motion sent",
				cards: {
					chat: {
						title: "AI Companion",
						desc: "Chat with Nori about anything on your mind",
						statusConfigured: "AI brain connected",
						statusNotConfigured: "API Key required",
						action: "Start Chat",
					},
					model: {
						title: "Model & Outfits",
						desc: "Switch outfits, costumes and manage expressions",
						current: "Current model",
						action: "Manage Models",
					},
					ai: {
						title: "AI Brain Settings",
						desc: "Connect OpenAI, Claude, Gemini and more LLMs",
						provider: "Provider",
						action: "Configure",
					},
				},
				links: {
					title: "Community & Links",
					steam: "Steam Store",
					noriOS: "NoriOS Official",
					qq: "QQ Group",
					bilibili: "Bilibili",
				},
				system: {
					title: "System Status",
					appVersion: "Version",
					webview: "Engine",
					statusNormal: "Normal",
				},
			},
			settingsTabs: {
				ai: "AI Brain",
				voice: "Voice & Audio",
				proactive: "Proactive & Routine",
				memory: "Memory",
				skills: "Skills",
				mcp: "Tools & MCP",
				general: "General",
			},
		},
	},
}
