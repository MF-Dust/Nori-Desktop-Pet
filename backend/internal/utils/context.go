package utils

import (
	"context"
	"net/http"
)

// ctxKey 上下文键
type ctxKey string

const (
	// RequestIDKey 请求ID键
	RequestIDKey ctxKey = "request_id"
	// ParamKey 路由参数键
	ParamKey ctxKey = "route_param"
)

// GetRouteParam 从请求上下文获取路由参数
func GetRouteParam(r *http.Request, key string) string {
	params, _ := r.Context().Value(ParamKey).(map[string]string)
	return params[key]
}

// SetRouteParam 设置路由参数到请求上下文
func SetRouteParam(r *http.Request, params map[string]string) *http.Request {
	ctx := context.WithValue(r.Context(), ParamKey, params)
	return r.WithContext(ctx)
}
