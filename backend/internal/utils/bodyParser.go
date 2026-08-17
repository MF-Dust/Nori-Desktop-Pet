package utils

import (
	"encoding/json"
	"io"
	"net/http"
	"strconv"
)

// BodyParser JSON Body 解析器
type BodyParser struct {
	data map[string]any
	w    http.ResponseWriter
	ok   bool
}

// NewBodyParser 创建 Body 解析器, 解析失败自动返回 BadRequest
func NewBodyParser(w http.ResponseWriter, r *http.Request) *BodyParser {
	r.Body = http.MaxBytesReader(w, r.Body, 1<<20)
	defer func(Body io.ReadCloser) {
		_ = Body.Close()
	}(r.Body)
	var body map[string]any
	decoder := json.NewDecoder(r.Body)
	decoder.UseNumber()
	if err := decoder.Decode(&body); err != nil {
		BadRequest(w, "Body 为空")
		return &BodyParser{w: w, ok: false}
	}
	return &BodyParser{data: body, w: w, ok: true}
}

// broken 返回一个失败的解析器
func broken(w http.ResponseWriter) *BodyParser {
	return &BodyParser{w: w, ok: false}
}

// OK 是否解析成功且无校验错误
func (p *BodyParser) OK() bool {
	return p.ok
}

// String 必填字符串
func (p *BodyParser) String(key string) string {
	if !p.ok {
		return ""
	}
	v, exists := p.data[key]
	if !exists {
		p.badRequest(key + " 不能为空")
		return ""
	}
	s, ok := v.(string)
	if !ok {
		p.badRequest(key + " 必须是字符串")
		return ""
	}
	if s == "" {
		p.badRequest(key + " 不能为空")
		return ""
	}
	return s
}

// StringOpt 可选字符串, 空值返回默认值
func (p *BodyParser) StringOpt(key string, defaultVal ...string) string {
	if !p.ok {
		return ""
	}
	v, ok := p.data[key].(string)
	if !ok || v == "" {
		if len(defaultVal) > 0 {
			return defaultVal[0]
		}
		return ""
	}
	return v
}

// Bool 可选布尔, 不存在或类型错误返回默认值
func (p *BodyParser) Bool(key string, defaultVal ...bool) bool {
	if !p.ok {
		return false
	}
	v, ok := p.data[key].(bool)
	if !ok {
		if len(defaultVal) > 0 {
			return defaultVal[0]
		}
		return false
	}
	return v
}

// Int 必填整数
func (p *BodyParser) Int(key string) int {
	if !p.ok {
		return 0
	}
	v, exists := p.data[key]
	if !exists {
		p.badRequest(key + " 不能为空")
		return 0
	}
	n, ok := v.(json.Number)
	if !ok {
		p.badRequest(key + " 必须是整数")
		return 0
	}
	i, err := n.Int64()
	if err != nil {
		p.badRequest(key + " 必须是整数")
		return 0
	}
	return int(i)
}

// IntOpt 可选整数, 不存在或为零值返回默认值
func (p *BodyParser) IntOpt(key string, defaultVal ...int) int {
	if !p.ok {
		return 0
	}
	v, ok := p.data[key].(json.Number)
	if !ok {
		if len(defaultVal) > 0 {
			return defaultVal[0]
		}
		return 0
	}
	i, _ := v.Int64()
	if i == 0 {
		if len(defaultVal) > 0 {
			return defaultVal[0]
		}
		return 0
	}
	return int(i)
}

// IntRange 必填整数, 带范围校验
func (p *BodyParser) IntRange(key string, min, max int) int {
	if !p.ok {
		return 0
	}
	v, exists := p.data[key]
	if !exists {
		p.badRequest(key + " 不能为空")
		return 0
	}
	n, ok := v.(json.Number)
	if !ok {
		p.badRequest(key + " 必须是整数")
		return 0
	}
	i, err := n.Int64()
	if err != nil {
		p.badRequest(key + " 必须是整数")
		return 0
	}
	if int(i) < min || int(i) > max {
		p.badRequest(key + " 必须在 " + itoa(min) + " 到 " + itoa(max) + " 之间")
		return 0
	}
	return int(i)
}

// IntRangeOpt 可选整数, 带默认值和范围校验
func (p *BodyParser) IntRangeOpt(key string, defaultVal, min, max int) int {
	if !p.ok {
		return 0
	}
	v, ok := p.data[key].(json.Number)
	if !ok {
		return defaultVal
	}
	i, _ := v.Int64()
	if i == 0 {
		return defaultVal
	}
	if int(i) < min || int(i) > max {
		p.badRequest(key + " 必须在 " + itoa(min) + " 到 " + itoa(max) + " 之间")
		return 0
	}
	return int(i)
}

// Float 必填浮点数
func (p *BodyParser) Float(key string) float64 {
	if !p.ok {
		return 0
	}
	v, exists := p.data[key]
	if !exists {
		p.badRequest(key + " 不能为空")
		return 0
	}
	n, ok := v.(json.Number)
	if !ok {
		p.badRequest(key + " 必须是数字")
		return 0
	}
	f, err := n.Float64()
	if err != nil {
		p.badRequest(key + " 必须是数字")
		return 0
	}
	return f
}

// FloatOpt 可选浮点数, 不存在返回默认值
func (p *BodyParser) FloatOpt(key string, defaultVal ...float64) float64 {
	if !p.ok {
		return 0
	}
	v, ok := p.data[key].(json.Number)
	if !ok {
		if len(defaultVal) > 0 {
			return defaultVal[0]
		}
		return 0
	}
	f, _ := v.Float64()
	return f
}

// FloatRangeOpt 可选浮点数, 带默认值和范围校验
func (p *BodyParser) FloatRangeOpt(key string, defaultVal, min, max float64) float64 {
	if !p.ok {
		return 0
	}
	v, ok := p.data[key].(json.Number)
	if !ok {
		return defaultVal
	}
	f, _ := v.Float64()
	if f == 0 {
		return defaultVal
	}
	if f < min || f > max {
		p.badRequest(key + " 必须在 " + ftoa(min) + " 到 " + ftoa(max) + " 之间")
		return 0
	}
	return f
}

// Strings 必填字符串数组
func (p *BodyParser) Strings(key string) []string {
	if !p.ok {
		return nil
	}
	v, exists := p.data[key]
	if !exists {
		p.badRequest(key + " 不能为空")
		return nil
	}
	arr, ok, err := interfaceSliceToStringSlice(v)
	if !ok || err != nil {
		p.badRequest(key + " 必须是字符串数组")
		return nil
	}
	return arr
}

// StringsOpt 可选字符串数组, 不存在返回 nil
func (p *BodyParser) StringsOpt(key string) []string {
	if !p.ok {
		return nil
	}
	v, exists := p.data[key]
	if !exists {
		return nil
	}
	arr, ok, err := interfaceSliceToStringSlice(v)
	if !ok || err != nil {
		p.badRequest(key + " 必须是字符串数组")
		return nil
	}
	return arr
}

// StringOrStrings 必填, 支持字符串或字符串数组
func (p *BodyParser) StringOrStrings(key string) ([]string, bool) {
	if !p.ok {
		return nil, false
	}
	v, exists := p.data[key]
	if !exists {
		p.badRequest(key + " 不能为空")
		return nil, false
	}
	// 尝试字符串
	if s, ok := v.(string); ok {
		if s == "" {
			p.badRequest(key + " 不能为空")
			return nil, false
		}
		return []string{s}, false
	}
	// 尝试字符串数组
	arr, ok, err := interfaceSliceToStringSlice(v)
	if !ok || err != nil {
		p.badRequest(key + " 必须是字符串或字符串数组")
		return nil, false
	}
	if len(arr) == 0 {
		p.badRequest(key + " 不能为空")
		return nil, false
	}
	return arr, true
}

// Object 必填嵌套对象, 返回子解析器
func (p *BodyParser) Object(key string) *BodyParser {
	if !p.ok {
		return broken(p.w)
	}
	v, exists := p.data[key]
	if !exists {
		p.badRequest(key + " 不能为空")
		return broken(p.w)
	}
	m, ok := v.(map[string]interface{})
	if !ok {
		p.badRequest(key + " 必须是对象")
		return broken(p.w)
	}
	return &BodyParser{data: m, w: p.w, ok: true}
}

// ObjectOpt 可选嵌套对象, 不存在返回 nil
func (p *BodyParser) ObjectOpt(key string) *BodyParser {
	if !p.ok {
		return nil
	}
	v, ok := p.data[key].(map[string]interface{})
	if !ok {
		return nil
	}
	return &BodyParser{data: v, w: p.w, ok: true}
}

// Has 检查字段是否存在
func (p *BodyParser) Has(key string) bool {
	if !p.ok {
		return false
	}
	_, exists := p.data[key]
	return exists
}

// Raw 获取原始值
func (p *BodyParser) Raw(key string) interface{} {
	if !p.ok {
		return nil
	}
	return p.data[key]
}

// Whitelist 白名单校验, 不在白名单中返回空字符串
func (p *BodyParser) Whitelist(key string, whitelist []string, defaultVal ...string) string {
	v, _ := p.data[key].(string)
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
func (p *BodyParser) Blacklist(key string, blacklist []string, defaultVal ...string) string {
	v, _ := p.data[key].(string)
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

// badRequest 设置错误状态并返回响应
func (p *BodyParser) badRequest(msg string) {
	p.ok = false
	BadRequest(p.w, msg)
}

// itoa 整数转字符串
func itoa(i int) string {
	return strconv.Itoa(i)
}

// ftoa 浮点数转字符串
func ftoa(f float64) string {
	return strconv.FormatFloat(f, 'f', -1, 64)
}
