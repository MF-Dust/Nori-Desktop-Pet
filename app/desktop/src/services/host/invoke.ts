/**
 * 命令调用
 *
 * 对应原 @tauri-apps/api/core 的 invoke.
 * 宿主不存在时 (纯 vite 调试) 一律 reject, 上层沿用原来的 try/catch 兜底逻辑.
 */
import {host} from "./index"

/**
 * 调用宿主命令
 * @param cmd 命令名 (snake_case, 与宿主 BridgeCommands 一致)
 * @param args 命令参数 (camelCase)
 */
export const invoke = async <T = unknown>(cmd: string, args?: Record<string, unknown>): Promise<T> => {
	const HOST = host()
	if (!HOST) throw new Error(`宿主不可用, 无法调用命令: ${cmd}`)
	return (await HOST.invoke(cmd, args)) as T
}
