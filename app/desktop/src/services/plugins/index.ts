import {invoke} from "../host/invoke"

export type PluginState =
	| "installed"
	| "loading"
	| "active"
	| "stopping"
	| "disabled"
	| "failed"
	| "incompatible"
	| "pending_restart"

export interface PluginCapabilityStatus {
	id: string
	declared: boolean
	granted: boolean
	available: boolean
}

export interface PluginInfo {
	id: string
	name: string
	description: string
	version: string
	author: string
	homepage: string | null
	repository: string | null
	license: string | null
	state: PluginState
	enabled: boolean
	capabilities: string[]
	optionalCapabilities: string[]
	capabilityStatuses: PluginCapabilityStatus[]
	errorCode: string | null
	errorMessage: string | null
	requiresRestart: boolean
	iconUrl: string | null
}

export interface PluginInstallResult {
	cancelled: boolean
	plugin: PluginInfo | null
}

export interface PluginUninstallResult {
	success: boolean
	requiresRestart: boolean
	plugin: PluginInfo | null
}

export const listPlugins = async (): Promise<PluginInfo[]> =>
	(await invoke("plugin_list")).plugins

export const installLocalPlugin = async (): Promise<PluginInstallResult> =>
	invoke("plugin_install_local")

export const enablePlugin = async (id: string): Promise<PluginInfo> =>
	invoke("plugin_enable", {id})

export const disablePlugin = async (id: string): Promise<PluginInfo> =>
	invoke("plugin_disable", {id})

export const uninstallPlugin = async (id: string, deleteData = false): Promise<PluginUninstallResult> =>
	invoke("plugin_uninstall", {id, deleteData})
