package middleware

import (
	"backend/internal/logger"
	"backend/internal/utils"
	"net/http"
	"time"

	"go.uber.org/zap"
)

// Logger 日志记录器
func Logger() func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			start := time.Now()
			wrapped := &responseWriter{ResponseWriter: w, StatusCode: http.StatusOK}
			next.ServeHTTP(wrapped, r)
			duration := time.Since(start)
			info := utils.GetUpstreamInfo(r)
			// 获取日志信息
			method := r.Method
			path := r.URL.Path
			status := wrapped.StatusCode
			durationNs := duration.Nanoseconds()
			responseSize := int64(wrapped.Size)
			// 系统日志
			logger.Log.Info("HTTP 请求",
				zap.String("requestID", info.RequestID),
				zap.String("ip", info.IPInfo.IP),
				zap.String("country", info.IPInfo.CountryLong),
				zap.String("region", info.IPInfo.Region),
				zap.String("method", method),
				zap.String("path", path),
				zap.Int("status", status),
				zap.String("origin", info.Origin),
				zap.String("userAgent", info.UAInfo.UserAgent),
				zap.Int64("durationNs", durationNs),
				zap.Int64("sizeBytes", responseSize),
			)
		})
	}
}
