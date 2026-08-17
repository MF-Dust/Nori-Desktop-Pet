package utils

import (
	"encoding/json"
	"net/http"
	"time"
)

// jsonResponse 统一的JSON响应
func jsonResponse(w http.ResponseWriter, status int, data any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	err := json.NewEncoder(w).Encode(data)
	if err != nil {
		return
	}
}

// Success 成功响应
func Success(w http.ResponseWriter, body any) {
	jsonResponse(w, http.StatusOK, map[string]any{
		"error":     false,
		"message":   "",
		"body":      body,
		"timestamp": time.Now().UnixMilli(),
	})
}

// Error 错误响应
func Error(w http.ResponseWriter, status int, message string) {
	jsonResponse(w, status, map[string]any{
		"error":     true,
		"message":   message,
		"body":      nil,
		"timestamp": time.Now().UnixMilli(),
	})
}

// BadRequest 错误的请求响应
func BadRequest(w http.ResponseWriter, message ...string) {
	messageStr := "bad request"
	if len(message) > 0 {
		messageStr = message[0]
	}
	Error(w, http.StatusBadRequest, messageStr)
}

// Unauthorized 未授权响应
func Unauthorized(w http.ResponseWriter, message ...string) {
	messageStr := "unauthorized"
	if len(message) > 0 {
		messageStr = message[0]
	}
	Error(w, http.StatusUnauthorized, messageStr)
}

// Forbidden 禁止响应
func Forbidden(w http.ResponseWriter, message ...string) {
	messageStr := "forbidden"
	if len(message) > 0 {
		messageStr = message[0]
	}
	Error(w, http.StatusForbidden, messageStr)
}

// NotFound 路由不存在响应
func NotFound(w http.ResponseWriter, message ...string) {
	messageStr := "not found route"
	if len(message) > 0 {
		messageStr = message[0]
	}
	Error(w, http.StatusNotFound, messageStr)
}

// Conflict 冲突响应
func Conflict(w http.ResponseWriter, message ...string) {
	messageStr := "conflict"
	if len(message) > 0 {
		messageStr = message[0]
	}
	Error(w, http.StatusConflict, messageStr)
}

// TooManyRequests 请求过多
func TooManyRequests(w http.ResponseWriter, message ...string) {
	messageStr := "too many requests"
	if len(message) > 0 {
		messageStr = message[0]
	}
	Error(w, http.StatusTooManyRequests, messageStr)
}

// InternalServerError 内部服务器错误
func InternalServerError(w http.ResponseWriter, message ...string) {
	messageStr := "internal server error"
	if len(message) > 0 {
		messageStr = message[0]
	}
	Error(w, http.StatusInternalServerError, messageStr)
}

// BadGateway 网关错误
func BadGateway(w http.ResponseWriter, message ...string) {
	messageStr := "bad gateway"
	if len(message) > 0 {
		messageStr = message[0]
	}
	Error(w, http.StatusBadGateway, messageStr)
}

// ServerBusy 服务器匆忙
func ServerBusy(w http.ResponseWriter, message ...string) {
	messageStr := "server busy"
	if len(message) > 0 {
		messageStr = message[0]
	}
	Error(w, http.StatusServiceUnavailable, messageStr)
}

// Customize 自定义JSON响应
func Customize(w http.ResponseWriter, status int, data any) {
	jsonResponse(w, status, data)
}
