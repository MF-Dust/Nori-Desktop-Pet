package logger

import (
	"backend/internal/config"
	"backend/internal/utils"
	"context"
	"os"

	"go.uber.org/zap"
	"go.uber.org/zap/zapcore"
	"gopkg.in/natefinch/lumberjack.v2"
)

var (
	// Log 系统日志记录器
	Log *zap.Logger
	// requestLog 请求日志记录器
	requestLog *zap.Logger
)

// InitLogger 初始化日志
func InitLogger() {
	cfg := config.Get().Logger

	// 系统日志
	Log = createLogger(cfg.LogPath, cfg)
	// 请求日志
	requestLog = createLogger(cfg.RequestLogPath, cfg)
}

// createLogger 创建日志记录器
func createLogger(logPath string, cfg config.LoggerConfig) *zap.Logger {
	level := parseLevel(cfg.Level)
	// 配置文件输出
	writeSyncer := zapcore.AddSync(&lumberjack.Logger{
		Filename:   logPath,
		MaxSize:    cfg.MaxSize,
		MaxBackups: cfg.MaxBackups,
		MaxAge:     cfg.MaxAge,
		Compress:   cfg.Compress,
	})
	// 配置控制台输出
	consoleSyncer := zapcore.AddSync(os.Stdout)
	// 配置编码器
	encoderCfg := zap.NewProductionEncoderConfig()
	encoderCfg.TimeKey = "time"
	encoderCfg.EncodeTime = zapcore.ISO8601TimeEncoder
	encoderCfg.LevelKey = "level"
	encoderCfg.MessageKey = "msg"
	var cores []zapcore.Core
	switch cfg.Output {
	case "file":
		cores = append(cores, zapcore.NewCore(zapcore.NewJSONEncoder(encoderCfg), writeSyncer, level))
	case "console":
		cores = append(cores, zapcore.NewCore(zapcore.NewConsoleEncoder(encoderCfg), consoleSyncer, level))
	case "both":
		fallthrough
	default:
		cores = append(cores,
			zapcore.NewCore(zapcore.NewJSONEncoder(encoderCfg), writeSyncer, level),
			zapcore.NewCore(zapcore.NewConsoleEncoder(encoderCfg), consoleSyncer, level),
		)
	}
	core := zapcore.NewTee(cores...)
	return zap.New(core, zap.AddCaller(), zap.AddStacktrace(zapcore.ErrorLevel))
}

// parseLevel 解析日志级别
func parseLevel(level string) zapcore.Level {
	switch level {
	case "debug":
		return zapcore.DebugLevel
	case "info":
		return zapcore.InfoLevel
	case "warn":
		return zapcore.WarnLevel
	case "error":
		return zapcore.ErrorLevel
	default:
		return zapcore.InfoLevel
	}
}

// WithRequestLogCtx 从 context 中获取 requestID 并添加到日志中
func WithRequestLogCtx(ctx context.Context) *zap.Logger {
	if ctx == nil {
		return requestLog
	}
	// 从 context 中获取 requestID
	if id, ok := ctx.Value(utils.RequestIDKey).(string); ok {
		return requestLog.With(zap.String("request_id", id))
	}
	return requestLog
}
