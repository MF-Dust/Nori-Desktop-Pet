package middleware

import (
	"net/http"
)

type responseWriter struct {
	http.ResponseWriter
	StatusCode  int
	Size        int
	WroteHeader bool
}

// WriteHeader 捕获状态码
func (rw *responseWriter) WriteHeader(code int) {
	if rw.WroteHeader {
		return
	}
	rw.StatusCode = code
	rw.ResponseWriter.WriteHeader(code)
	rw.WroteHeader = true
}

// Write 捕获响应体
func (rw *responseWriter) Write(data []byte) (int, error) {
	if !rw.WroteHeader {
		rw.WriteHeader(http.StatusOK)
	}
	n, err := rw.ResponseWriter.Write(data)
	rw.Size += n
	return n, err
}
