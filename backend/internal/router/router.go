package router

import (
	"backend/internal/utils"
	"net/http"
	"strings"
)

// New 创建新的路由
func New() *Router {
	return &Router{
		Routes: make(map[string]map[string]http.Handler),
	}
}

// group 创建路由组
func (r *Router) group(prefix string) *groupType {
	return &groupType{
		Prefix: prefix,
		Router: r,
	}
}

// handle 注册路由
func (r *Router) handle(method, path string, handler http.Handler) {
	if r.Routes[path] == nil {
		r.Routes[path] = make(map[string]http.Handler)
	}
	r.Routes[path][method] = handler
}

// handleFunc 注册路由处理函数
func (r *Router) handleFunc(method, path string, handler http.HandlerFunc) {
	r.handle(method, path, handler)
}

// ServeHTTP 路由服务
func (r *Router) ServeHTTP(w http.ResponseWriter, req *http.Request) {
	path := req.URL.Path
	method := req.Method

	// 1. 精确匹配
	if methods, ok := r.Routes[path]; ok {
		if handler, ok := methods[method]; ok {
			handler.ServeHTTP(w, req)
			return
		}
		utils.BadRequest(w, "方法不允许")
		return
	}

	// 2. 参数路由匹配
	reqPathSegs := splitPath(path)
	for routePath, methods := range r.Routes {
		routeSegs := splitPath(routePath)
		if len(routeSegs) != len(reqPathSegs) {
			continue
		}
		params, matched := matchSegments(routeSegs, reqPathSegs)
		if matched {
			if handler, ok := methods[method]; ok {
				if len(params) > 0 {
					req = utils.SetRouteParam(req, params)
				}
				handler.ServeHTTP(w, req)
				return
			}
		}
	}

	utils.NotFound(w, "资源未找到")
}

// splitPath 分割路径为段
func splitPath(path string) []string {
	var segs []string
	for _, s := range strings.Split(path, "/") {
		if s != "" {
			segs = append(segs, s)
		}
	}
	return segs
}

// matchSegments 匹配路由段, 返回提取的参数
func matchSegments(routeSegs, reqSegs []string) (map[string]string, bool) {
	params := make(map[string]string)
	for i, seg := range routeSegs {
		if strings.HasPrefix(seg, ":") {
			params[seg[1:]] = reqSegs[i]
		} else if seg != reqSegs[i] {
			return nil, false
		}
	}
	return params, true
}
