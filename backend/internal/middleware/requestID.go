package middleware

import (
	"backend/internal/utils"
	"net/http"
)

// RequestID 请求ID中间件
func RequestID() func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			// 沿用上游 RequestID
			requestID := utils.GetUpstreamInfo(r).RequestID
			// 设置响应头
			w.Header().Set("X-Request-ID", requestID)
			// 继续处理请求
			next.ServeHTTP(w, r)
		})
	}
}
