package utils

import (
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"
	"time"
)

type RequestOptions struct {
	URL       string
	Method    string
	Timeout   time.Duration
	Headers   map[string]string
	Cookies   map[string]string
	Params    map[string]string
	UserAgent string
	Body      io.Reader
}

type Response struct {
	StatusCode  int
	ContentType string
	Headers     http.Header
	Body        []byte
}

// Fetch 网络请求响应
func Fetch(opts *RequestOptions) (*Response, error) {
	if opts == nil {
		return nil, fmt.Errorf("请求选项不能为空")
	}
	if opts.URL == "" {
		return nil, fmt.Errorf("URL不能为空")
	}
	// 默认 Method
	method := opts.Method
	if method == "" {
		method = http.MethodGet
	}
	// 默认 UA
	userAgent := opts.UserAgent
	if userAgent == "" {
		userAgent = "Mozilla/5.0"
	}
	// 默认超时
	timeout := opts.Timeout
	if timeout == 0 {
		timeout = 10 * time.Second
	}
	// 处理查询参数
	finalURL := opts.URL
	if len(opts.Params) > 0 {
		parsedURL, err := url.Parse(opts.URL)
		if err != nil {
			return nil, err
		}
		query := parsedURL.Query()
		for k, v := range opts.Params {
			query.Set(k, v)
		}
		parsedURL.RawQuery = query.Encode()
		finalURL = parsedURL.String()
	}
	// 创建 HTTP 客户端
	client := &http.Client{
		Timeout: timeout,
	}
	// 创建 HTTP 请求
	req, err := http.NewRequest(method, finalURL, opts.Body)
	if err != nil {
		return nil, err
	}
	// 设置 UA
	req.Header.Set("User-Agent", userAgent)
	// 自定义 Headers
	for k, v := range opts.Headers {
		req.Header.Set(k, v)
	}
	// 自定义 Cookies
	if len(opts.Cookies) > 0 {
		var cookiePairs []string
		for k, v := range opts.Cookies {
			cookiePairs = append(cookiePairs, k+"="+v)
		}
		req.Header.Set("Cookie", strings.Join(cookiePairs, "; "))
	}
	// 发送请求
	resp, err := client.Do(req)
	if err != nil {
		return nil, err
	}
	defer func(Body io.ReadCloser) {
		_ = Body.Close()
	}(resp.Body)
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}
	response := &Response{
		StatusCode:  resp.StatusCode,
		ContentType: resp.Header.Get("Content-Type"),
		Headers:     resp.Header,
		Body:        body,
	}
	if resp.StatusCode >= 400 {
		return response, fmt.Errorf("HTTP错误: %d", resp.StatusCode)
	}
	return response, nil
}
