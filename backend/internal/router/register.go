package router

import (
	"backend/internal/controller/api"
	"net/http"
)

// Register 注册路由
func Register(r *Router) {
	r.handleFunc(http.MethodGet, "/ping", api.Ping())
}
