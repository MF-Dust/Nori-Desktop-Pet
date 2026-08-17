package utils

import (
	"net/http"
	"net/url"
	"strconv"
)

// QueryParser URL 查询参数解析器
type QueryParser struct {
	query url.Values
}

// NewQueryParser 创建查询参数解析器
func NewQueryParser(r *http.Request) *QueryParser {
	return &QueryParser{query: r.URL.Query()}
}

// String 必填字符串
func (p *QueryParser) String(key string) string {
	return p.query.Get(key)
}

// StringOpt 可选字符串, 空值返回默认值
func (p *QueryParser) StringOpt(key string, defaultVal ...string) string {
	v := p.query.Get(key)
	if v == "" && len(defaultVal) > 0 {
		return defaultVal[0]
	}
	return v
}

// Int 必填整数, 解析失败返回 0
func (p *QueryParser) Int(key string) int {
	v := p.query.Get(key)
	if v == "" {
		return 0
	}
	i, _ := strconv.Atoi(v)
	return i
}

// IntOpt 可选整数, 空值或解析失败返回默认值
func (p *QueryParser) IntOpt(key string, defaultVal ...int) int {
	v := p.query.Get(key)
	if v == "" {
		if len(defaultVal) > 0 {
			return defaultVal[0]
		}
		return 0
	}
	i, err := strconv.Atoi(v)
	if err != nil {
		if len(defaultVal) > 0 {
			return defaultVal[0]
		}
		return 0
	}
	return i
}

// IntRange 必填整数, 带范围校验, 超出范围返回 0
func (p *QueryParser) IntRange(key string, min, max int) int {
	v := p.query.Get(key)
	if v == "" {
		return 0
	}
	i, err := strconv.Atoi(v)
	if err != nil {
		return 0
	}
	if i < min || i > max {
		return 0
	}
	return i
}

// IntRangeOpt 可选整数, 带默认值和范围校验
func (p *QueryParser) IntRangeOpt(key string, defaultVal, min, max int) int {
	v := p.query.Get(key)
	if v == "" {
		return defaultVal
	}
	i, err := strconv.Atoi(v)
	if err != nil {
		return defaultVal
	}
	if i < min || i > max {
		return defaultVal
	}
	return i
}

// Float 必填浮点数, 解析失败返回 0
func (p *QueryParser) Float(key string) float64 {
	v := p.query.Get(key)
	if v == "" {
		return 0
	}
	f, _ := strconv.ParseFloat(v, 64)
	return f
}

// FloatOpt 可选浮点数, 空值或解析失败返回默认值
func (p *QueryParser) FloatOpt(key string, defaultVal ...float64) float64 {
	v := p.query.Get(key)
	if v == "" {
		if len(defaultVal) > 0 {
			return defaultVal[0]
		}
		return 0
	}
	f, err := strconv.ParseFloat(v, 64)
	if err != nil {
		if len(defaultVal) > 0 {
			return defaultVal[0]
		}
		return 0
	}
	return f
}

// FloatRange 必填浮点数, 带范围校验, 超出范围返回 0
func (p *QueryParser) FloatRange(key string, min, max float64) float64 {
	v := p.query.Get(key)
	if v == "" {
		return 0
	}
	f, err := strconv.ParseFloat(v, 64)
	if err != nil {
		return 0
	}
	if f < min || f > max {
		return 0
	}
	return f
}

// FloatRangeOpt 可选浮点数, 带默认值和范围校验
func (p *QueryParser) FloatRangeOpt(key string, defaultVal, min, max float64) float64 {
	v := p.query.Get(key)
	if v == "" {
		return defaultVal
	}
	f, err := strconv.ParseFloat(v, 64)
	if err != nil {
		return defaultVal
	}
	if f < min || f > max {
		return defaultVal
	}
	return f
}

// Bool 必填布尔, 解析失败返回 false
func (p *QueryParser) Bool(key string) bool {
	v := p.query.Get(key)
	if v == "" {
		return false
	}
	b, _ := strconv.ParseBool(v)
	return b
}

// BoolOpt 可选布尔, 空值或解析失败返回默认值
func (p *QueryParser) BoolOpt(key string, defaultVal ...bool) bool {
	v := p.query.Get(key)
	if v == "" {
		if len(defaultVal) > 0 {
			return defaultVal[0]
		}
		return false
	}
	b, err := strconv.ParseBool(v)
	if err != nil {
		if len(defaultVal) > 0 {
			return defaultVal[0]
		}
		return false
	}
	return b
}

// Strings 获取字符串数组
func (p *QueryParser) Strings(key string) []string {
	return p.query[key]
}

// Has 检查参数是否存在
func (p *QueryParser) Has(key string) bool {
	_, ok := p.query[key]
	return ok
}

// Whitelist 白名单校验, 不在白名单中返回空字符串
func (p *QueryParser) Whitelist(key string, whitelist []string, defaultVal ...string) string {
	v := p.query.Get(key)
	if v == "" {
		if len(defaultVal) > 0 {
			return defaultVal[0]
		}
		return ""
	}
	for _, item := range whitelist {
		if item == v {
			return v
		}
	}
	if len(defaultVal) > 0 {
		return defaultVal[0]
	}
	return ""
}

// Blacklist 黑名单校验, 在黑名单中返回空字符串
func (p *QueryParser) Blacklist(key string, blacklist []string, defaultVal ...string) string {
	v := p.query.Get(key)
	if v == "" {
		if len(defaultVal) > 0 {
			return defaultVal[0]
		}
		return ""
	}
	for _, item := range blacklist {
		if item == v {
			if len(defaultVal) > 0 {
				return defaultVal[0]
			}
			return ""
		}
	}
	return v
}

