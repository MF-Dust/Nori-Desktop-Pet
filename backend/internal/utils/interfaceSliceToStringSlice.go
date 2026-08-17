package utils

import "fmt"

// interfaceSliceToStringSlice 将接口切片转换为字符串切片
func interfaceSliceToStringSlice(raw interface{}) ([]string, bool, error) {
	if raw == nil {
		// 不存在
		return nil, false, nil
	}
	rawSlice, ok := raw.([]interface{})
	if !ok {
		return nil, true, fmt.Errorf("不是数组")
	}
	result := make([]string, len(rawSlice))
	for i, v := range rawSlice {
		s, ok := v.(string)
		if !ok {
			return nil, true, fmt.Errorf("数组元素不是字符串")
		}
		result[i] = s
	}
	return result, true, nil
}
