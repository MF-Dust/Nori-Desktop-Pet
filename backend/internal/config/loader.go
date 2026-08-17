package config

import (
	"fmt"
	"os"

	"gopkg.in/yaml.v3"
)

// Load 加载基础配置
func Load() (*Config, error) {
	configPath := "configs/config.yaml"
	data, err := os.ReadFile(configPath)
	if err != nil {
		fmt.Printf("读取配置文件失败: %v", err)
		return nil, err
	}
	var config Config
	if err := yaml.Unmarshal(data, &config); err != nil {
		fmt.Printf("解析配置文件失败: %v", err)
		return nil, err
	}
	return &config, nil
}
