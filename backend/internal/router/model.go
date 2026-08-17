package router

import (
	"net/http"
)

// groupType 路由组
type groupType struct {
	Prefix string
	Router *Router
}

// Router 路由器
type Router struct {
	Routes map[string]map[string]http.Handler
}

// handle 注册路由到组
func (g *groupType) handle(method, path string, handler http.Handler) {
	fullPath := g.Prefix + path
	g.Router.handle(method, fullPath, handler)
}

// handleFunc 注册路由处理函数到组
func (g *groupType) handleFunc(method, path string, handler http.HandlerFunc) {
	g.handle(method, path, handler)
}
