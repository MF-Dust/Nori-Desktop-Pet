package utils

import (
	"net/http"
	"strconv"
)

// IPLocation IP 位置信息
type IPLocation struct {
	IP           string  `json:"ip"`
	CountryShort string  `json:"country_short"`
	CountryLong  string  `json:"country_long"`
	Region       string  `json:"region"`
	City         string  `json:"city"`
	Latitude     float64 `json:"latitude"`
	Longitude    float64 `json:"longitude"`
	Zipcode      string  `json:"zipcode"`
	Timezone     string  `json:"timezone"`
}

// userAgentInfo 用户代理信息
type userAgentInfo struct {
	UserAgent string `json:"user_agent"`
	Device    string `json:"device"`
}

// UpstreamInfo 上游信息
type UpstreamInfo struct {
	RequestID string         `json:"request_id"`
	IPInfo    *IPLocation    `json:"ip_info"`
	UAInfo    *userAgentInfo `json:"ua_info"`
	Origin    string         `json:"origin"`
}

// GetUpstreamInfo 获取上游信息
func GetUpstreamInfo(r *http.Request) *UpstreamInfo {
	info := &UpstreamInfo{}

	// 获取 Request ID
	info.RequestID = r.Header.Get("X-Request-ID")

	// 获取 IP 位置信息
	ip := r.Header.Get("X-Real-IP")
	if ip != "" {
		info.IPInfo = &IPLocation{
			IP:           ip,
			CountryShort: r.Header.Get("G-Country-Short"),
			CountryLong:  r.Header.Get("G-Country-Long"),
			Region:       r.Header.Get("G-Region"),
			City:         r.Header.Get("G-City"),
			Zipcode:      r.Header.Get("G-Zipcode"),
			Timezone:     r.Header.Get("G-Timezone"),
		}

		// 解析经纬度
		if lat, err := strconv.ParseFloat(r.Header.Get("G-Latitude"), 64); err == nil {
			info.IPInfo.Latitude = lat
		}
		if lon, err := strconv.ParseFloat(r.Header.Get("G-Longitude"), 64); err == nil {
			info.IPInfo.Longitude = lon
		}
	}

	// 获取 UserAgent 信息
	ua := r.Header.Get("User-Agent")
	if ua != "" {
		info.UAInfo = &userAgentInfo{
			UserAgent: ua,
			Device:    r.Header.Get("G-Device"),
		}
	}

	// 获取 Origin
	info.Origin = r.Header.Get("Origin")

	return info
}
