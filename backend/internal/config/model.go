package config

// gatewayConfig 网关配置
type gatewayConfig struct {
	Port     int    `yaml:"port"`
	DataPath string `yaml:"data-path"`
	TempPath string `yaml:"temp-path"`
}

// LoggerConfig 日志配置
type LoggerConfig struct {
	Output         string `yaml:"output"`
	Level          string `yaml:"level"`
	LogPath        string `yaml:"log-path"`
	RequestLogPath string `yaml:"request-log-path"`
	MaxSize        int    `yaml:"max-size"`
	MaxBackups     int    `yaml:"max-backups"`
	MaxAge         int    `yaml:"max-age"`
	Compress       bool   `yaml:"compress"`
}

// Config 配置
type Config struct {
	Gateway gatewayConfig `yaml:"gateway"`
	Logger  LoggerConfig  `yaml:"logger"`
}
