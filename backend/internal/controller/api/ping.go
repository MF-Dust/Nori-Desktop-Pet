package api

import (
	"backend/internal/utils"
	"net/http"
	"strconv"
	"time"
)

// Ping 测试路由
func Ping() http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		q := utils.NewQueryParser(r)
		clientTsStr := q.String("timestamp")
		if clientTsStr == "" {
			utils.BadRequest(w, "timestamp 不能为空")
			return
		}
		clientTs, err := strconv.ParseInt(clientTsStr, 10, 64)
		if err != nil {
			utils.BadRequest(w, "无效的时间戳格式")
			return
		}
		// 兼容秒级时间戳
		if clientTs < 10000000000 {
			clientTs *= 1000
		}
		serverTime := time.Now().UnixMilli()
		diff := serverTime - clientTs
		latency := diff
		if diff < 0 {
			latency = -diff
		}
		utils.Success(w, map[string]any{
			"latency": strconv.FormatInt(latency, 10) + "ms",
		})
	}
}
