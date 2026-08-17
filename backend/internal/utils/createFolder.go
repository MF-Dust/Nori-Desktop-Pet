package utils

import (
	"backend/internal/config"
	"fmt"
	"os"
)

// CreateFolder 创建必要的目录
func CreateFolder() {
	cfg := config.Get()
	paths := []string{
		cfg.Gateway.DataPath,
		cfg.Gateway.TempPath,
	}
	for _, path := range paths {
		err := isFolderExist(path)
		if err != nil {
			fmt.Printf("创建目录失败 (%s)\n", path)
			_, err := fmt.Scanln()
			if err != nil {
				return
			}
			os.Exit(1)
		}
	}
}

// isFolderExist 判断文件夹是否存在, 不存在则创建
func isFolderExist(name string) error {
	if _, err := os.Stat(name); os.IsNotExist(err) {
		return os.MkdirAll(name, 0755)
	}
	return nil
}
