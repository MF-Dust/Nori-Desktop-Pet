package main

import (
	"backend/internal/config"
	"backend/internal/logger"
	"backend/internal/middleware"
	"backend/internal/router"
	"backend/internal/utils"
	"context"
	"errors"
	"net/http"
	"os"
	"os/signal"
	"strconv"
	"syscall"
	"time"

	"go.uber.org/zap"
)

// init 初始化
func init() {
	// 加载基础配置
	cfg, err := config.Load()
	if err != nil {
		return
	}
	config.Set(cfg)

	// 初始化日志
	logger.InitLogger()
	defer func(Log *zap.Logger) {
		_ = Log.Sync()
	}(logger.Log)

	// 创建目录
	utils.CreateFolder()
}

// main 主函数
func main() {
	cfg := config.Get()

	// 获取路由处理器
	handler := setupRouter()
	port := strconv.Itoa(cfg.Gateway.Port)
	srv := &http.Server{
		Addr:    ":" + port,
		Handler: handler,
	}
	logger.Log.Info("服务启动完成")

	// 监听退出信号
	quit := make(chan os.Signal, 1)
	signal.Notify(quit, syscall.SIGINT, syscall.SIGTERM)

	go func() {
		if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
			logger.Log.Fatal("服务启动失败", zap.Error(err))
		}
	}()

	<-quit
	logger.Log.Info("正在关闭服务")
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := srv.Shutdown(ctx); err != nil {
		logger.Log.Fatal("服务关闭失败", zap.Error(err))
	}
	logger.Log.Info("服务已关闭")
}

// setupRouter 设置路由
func setupRouter() http.Handler {
	r := router.New()
	router.Register(r)
	handler := middleware.Logger()(r)
	handler = middleware.RequestID()(handler)
	handler = middleware.CORS(handler)
	return handler
}
