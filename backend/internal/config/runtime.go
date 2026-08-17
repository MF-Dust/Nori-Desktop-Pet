package config

import "sync/atomic"

// runtimeConfig 运行时配置
var runtimeConfig atomic.Value

// Set 设置运行时配置
func Set(cfg *Config) {
	runtimeConfig.Store(cfg)
}

// Get 获取运行时配置
func Get() *Config {
	return runtimeConfig.Load().(*Config)
}
